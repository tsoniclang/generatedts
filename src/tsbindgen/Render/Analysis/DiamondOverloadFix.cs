using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Phase 3: Fixes diamond inheritance conflicts by adding method overloads.
///
/// When an interface extends multiple interfaces that both inherit from a common
/// base with conflicting method signatures, TypeScript requires explicit overloads.
///
/// Example:
/// IQueryable_1&lt;T&gt; extends IEnumerable_1&lt;T&gt;, IQueryable
///   - IEnumerable_1&lt;T&gt; has: GetEnumerator(): IEnumerator_1&lt;T&gt;
///   - IQueryable extends IEnumerable with: GetEnumerator(): IEnumerator
///   - TypeScript error: TS2320 - conflicting signatures
///
/// Fix: Add both overloads to IQueryable_1&lt;T&gt;:
///   - GetEnumerator(): IEnumerator_1&lt;T&gt;
///   - GetEnumerator(): IEnumerator
/// </summary>
public static class DiamondOverloadFix
{
    public static NamespaceModel Apply(NamespaceModel model, IReadOnlyDictionary<string, NamespaceModel> allModels, AnalysisContext ctx)
    {
        // Build global type lookup
        var globalTypeLookup = new Dictionary<string, TypeModel>();
        foreach (var ns in allModels.Values)
        {
            foreach (var type in ns.Types)
            {
                var key = GetTypeKey(type.Binding.Type);
                globalTypeLookup[key] = type;
            }
        }

        // Process each interface type
        var updatedTypes = model.Types.Select(type =>
            type.Kind == TypeKind.Interface
                ? AddDiamondOverloads(type, globalTypeLookup, ctx)
                : type
        ).ToList();

        return model with { Types = updatedTypes };
    }

    private static TypeModel AddDiamondOverloads(TypeModel type, Dictionary<string, TypeModel> typeLookup, AnalysisContext ctx)
    {
        if (type.Implements.Count < 2)
            return type; // No conflicts possible with less than 2 parent interfaces

        // Collect all conflicting methods (both diamond and sibling conflicts)
        var conflictingMethods = new Dictionary<string, List<MethodModel>>();

        for (int i = 0; i < type.Implements.Count; i++)
        {
            for (int j = i + 1; j < type.Implements.Count; j++)
            {
                var interface1 = type.Implements[i];
                var interface2 = type.Implements[j];

                // Get all methods from both interfaces (including inherited)
                var methods1 = GetAllMethods(interface1, typeLookup);
                var methods2 = GetAllMethods(interface2, typeLookup);

                // Build substitution maps for both interfaces
                var interface1Type = FindInterfaceType(interface1, typeLookup);
                var interface2Type = FindInterfaceType(interface2, typeLookup);

                var substitutions1 = interface1Type != null
                    ? GenericSubstitution.BuildSubstitutionMap(interface1, interface1Type.GenericParameters)
                    : new Dictionary<string, TypeReference>();

                var substitutions2 = interface2Type != null
                    ? GenericSubstitution.BuildSubstitutionMap(interface2, interface2Type.GenericParameters)
                    : new Dictionary<string, TypeReference>();

                // Find methods with same name but different signatures
                foreach (var method1 in methods1)
                {
                    foreach (var method2 in methods2)
                    {
                        if (ctx.SameIdentifier(method1, method2) &&
                            !AreSameSignature(method1, method2, ctx))
                        {
                            // Substitute generic parameters before adding
                            var substitutedMethod1 = GenericSubstitution.SubstituteMethod(method1, substitutions1);
                            var substitutedMethod2 = GenericSubstitution.SubstituteMethod(method2, substitutions2);

                            // Check if substituted methods still reference undefined type parameters
                            if (ReferencesUndefinedTypeParams(substitutedMethod1, type.GenericParameters, ctx))
                                continue;

                            if (ReferencesUndefinedTypeParams(substitutedMethod2, type.GenericParameters, ctx))
                                continue;

                            // Found a conflict - need to add both overloads
                            var methodIdentifier = ctx.GetMethodIdentifier(method1);
                            if (!conflictingMethods.ContainsKey(methodIdentifier))
                                conflictingMethods[methodIdentifier] = new List<MethodModel>();

                            if (!conflictingMethods[methodIdentifier].Any(m => AreSameSignature(m, substitutedMethod1, ctx)))
                                conflictingMethods[methodIdentifier].Add(substitutedMethod1);

                            if (!conflictingMethods[methodIdentifier].Any(m => AreSameSignature(m, substitutedMethod2, ctx)))
                                conflictingMethods[methodIdentifier].Add(substitutedMethod2);
                        }
                    }
                }
            }
        }

        // Add synthetic overloads for conflicting methods
        if (conflictingMethods.Count == 0)
            return type; // No conflicts

        var newMethods = type.Members.Methods.ToList();

        foreach (var (methodName, overloads) in conflictingMethods)
        {
            // Skip if already defined on this interface
            if (newMethods.Any(m => ctx.GetMethodIdentifier(m) == methodName))
                continue;

            // Add all overload signatures
            foreach (var overload in overloads)
            {
                newMethods.Add(overload with {
                    // Mark as synthetic
                    SyntheticOverload = new SyntheticOverloadInfo(
                        type.Binding.Type.GetClrType(),
                        methodName,
                        SyntheticOverloadReason.InterfaceSignatureMismatch
                    )
                });
            }
        }

        var updatedMembers = type.Members with { Methods = newMethods };
        return type with { Members = updatedMembers };
    }

    /// <summary>
    /// Gets all methods from an interface and its ancestors.
    /// </summary>
    private static List<MethodModel> GetAllMethods(
        TypeReference typeRef,
        Dictionary<string, TypeModel> typeLookup)
    {
        var methods = new List<MethodModel>();
        var visited = new HashSet<string>();

        CollectMethods(typeRef, typeLookup, methods, visited);

        return methods;
    }

    private static void CollectMethods(
        TypeReference typeRef,
        Dictionary<string, TypeModel> typeLookup,
        List<MethodModel> methods,
        HashSet<string> visited)
    {
        var key = GetTypeKey(typeRef);

        if (visited.Contains(key))
            return;

        visited.Add(key);

        if (!typeLookup.TryGetValue(key, out var typeDef))
            return;

        // Add methods from this type
        methods.AddRange(typeDef.Members.Methods);

        // Recursively collect from parent interfaces
        foreach (var parent in typeDef.Implements)
        {
            CollectMethods(parent, typeLookup, methods, visited);
        }
    }

    private static bool AreSameSignature(MethodModel m1, MethodModel m2, AnalysisContext ctx)
    {
        // Same name
        if (!ctx.SameIdentifier(m1, m2))
            return false;

        // Same parameter count
        if (m1.Parameters.Count != m2.Parameters.Count)
            return false;

        // Same parameter types
        for (int i = 0; i < m1.Parameters.Count; i++)
        {
            if (GetTypeKey(m1.Parameters[i].Type) != GetTypeKey(m2.Parameters[i].Type))
                return false;
        }

        // Same return type
        if (GetTypeKey(m1.ReturnType) != GetTypeKey(m2.ReturnType))
            return false;

        return true;
    }

    private static TypeModel? FindInterfaceType(TypeReference typeRef, Dictionary<string, TypeModel> typeLookup)
    {
        var key = GetTypeKey(typeRef);
        typeLookup.TryGetValue(key, out var type);
        return type;
    }

    private static bool ReferencesUndefinedTypeParams(MethodModel method, IReadOnlyList<GenericParameterModel> availableTypeParams, AnalysisContext ctx)
    {
        var availableNames = new HashSet<string>(availableTypeParams.Select(p => ctx.GetGenericParameterIdentifier(p)));

        // Add method-level type parameters
        foreach (var gp in method.GenericParameters)
            availableNames.Add(ctx.GetGenericParameterIdentifier(gp));

        // Check return type
        if (ReferencesUndefinedTypeParamsInType(method.ReturnType, availableNames))
            return true;

        // Check parameters
        foreach (var param in method.Parameters)
        {
            if (ReferencesUndefinedTypeParamsInType(param.Type, availableNames))
                return true;
        }

        return false;
    }

    private static bool ReferencesUndefinedTypeParamsInType(TypeReference type, HashSet<string> availableNames)
    {
        // Check if this is an undefined type parameter
        if (type.Namespace == null && !availableNames.Contains(type.TypeName))
        {
            // Type parameter names are typically single letters or start with T
            if (type.TypeName.Length <= 2 || type.TypeName.StartsWith("T"))
                return true;
        }

        // Check generic arguments recursively
        foreach (var arg in type.GenericArgs)
        {
            if (ReferencesUndefinedTypeParamsInType(arg, availableNames))
                return true;
        }

        return false;
    }

    private static string GetTypeKey(TypeReference typeRef)
    {
        var ns = typeRef.Namespace != null ? typeRef.Namespace + "." : "";
        return ns + typeRef.TypeName;
    }

    private class TypeKeyComparer : IEqualityComparer<TypeReference>
    {
        public bool Equals(TypeReference? x, TypeReference? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return GetTypeKey(x) == GetTypeKey(y);
        }

        public int GetHashCode(TypeReference obj)
        {
            return GetTypeKey(obj).GetHashCode();
        }
    }
}

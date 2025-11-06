using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Phase 3: Fixes TS2417 errors by adding base class static method overloads.
///
/// When a class defines a static method with the same name as an inherited static method
/// but different return type, TypeScript requires both signatures to be present.
///
/// Example:
/// Object has: static Equals(objA: Object, objB: Object): Boolean
/// SqlDateTime has: static Equals(x: SqlDateTime, y: SqlDateTime): SqlBoolean
///
/// TypeScript sees these as overloads but SqlBoolean != Boolean, causing TS2417.
/// Fix: Add both overloads to SqlDateTime.
/// </summary>
public static class StaticMethodOverloadFix
{
    public static NamespaceModel Apply(NamespaceModel model, IReadOnlyDictionary<string, NamespaceModel> allModels)
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

        // Process each class type (structs and classes)
        var updatedTypes = model.Types.Select(type =>
            type.Kind == TypeKind.Class || type.Kind == TypeKind.Struct
                ? AddStaticMethodOverloads(type, globalTypeLookup)
                : type
        ).ToList();

        return model with { Types = updatedTypes };
    }

    private static TypeModel AddStaticMethodOverloads(TypeModel type, Dictionary<string, TypeModel> typeLookup)
    {
        if (type.BaseType == null)
            return type; // No base class, no inherited static methods

        // Collect all static methods from base class chain
        var baseStaticMethods = GetAllStaticMethods(type.BaseType, typeLookup);
        if (baseStaticMethods.Count == 0)
            return type; // No base static methods

        // Get current static methods
        var currentStaticMethods = type.Members.Methods.Where(m => m.IsStatic).ToList();

        // Find conflicts: methods with same name but different signatures
        var methodsToAdd = new List<MethodModel>();

        foreach (var baseMethod in baseStaticMethods)
        {
            // Check if we have a static method with the same name
            var matchingMethods = currentStaticMethods.Where(m => m.TsAlias == baseMethod.TsAlias).ToList();

            if (matchingMethods.Count > 0)
            {
                // Check if any matching method has the exact same signature
                bool hasExactMatch = matchingMethods.Any(m => AreSameSignature(m, baseMethod));

                if (!hasExactMatch)
                {
                    // We have a method with same name but different signature
                    // Need to add the base signature as an overload
                    methodsToAdd.Add(baseMethod with
                    {
                        SyntheticOverload = new SyntheticOverloadInfo(
                            type.Binding.Type.GetClrType(),
                            baseMethod.TsAlias,
                            SyntheticOverloadReason.BaseClassCovariance
                        )
                    });
                }
            }
        }

        if (methodsToAdd.Count == 0)
            return type; // No conflicts

        // Add the missing overloads
        var newMethods = type.Members.Methods.ToList();
        newMethods.AddRange(methodsToAdd);

        var updatedMembers = type.Members with { Methods = newMethods };
        return type with { Members = updatedMembers };
    }

    /// <summary>
    /// Gets all static methods from a type and its base class chain.
    /// </summary>
    private static List<MethodModel> GetAllStaticMethods(
        TypeReference typeRef,
        Dictionary<string, TypeModel> typeLookup)
    {
        var methods = new List<MethodModel>();
        var visited = new HashSet<string>();

        CollectStaticMethods(typeRef, typeLookup, methods, visited);

        return methods;
    }

    private static void CollectStaticMethods(
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

        // Add static methods from this type
        methods.AddRange(typeDef.Members.Methods.Where(m => m.IsStatic));

        // Recursively collect from base class
        if (typeDef.BaseType != null)
        {
            CollectStaticMethods(typeDef.BaseType, typeLookup, methods, visited);
        }
    }

    private static bool AreSameSignature(MethodModel m1, MethodModel m2)
    {
        // Same name
        if (m1.TsAlias != m2.TsAlias)
            return false;

        // Same static-ness
        if (m1.IsStatic != m2.IsStatic)
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

    private static string GetTypeKey(TypeReference typeRef)
    {
        var ns = typeRef.Namespace != null ? typeRef.Namespace + "." : "";
        return ns + typeRef.TypeName;
    }
}

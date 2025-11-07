using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Phase 3: Adds base class method overloads when method signatures differ (arity mismatch).
///
/// In TypeScript, when a derived class method has the same name as a base class method
/// but different arity (parameter count), both signatures must be present.
///
/// This pass:
/// 1. For each method in a class, checks if base class has methods with same name but different arity
/// 2. Substitutes generic type parameters from base class (e.g., T → System.Byte)
/// 3. Adds base class method signatures as synthetic overloads
/// 4. Skips methods that reference undefined type parameters after substitution
///
/// Example:
/// class EqualityComparer_1&lt;T&gt; {
///     Equals(x: T, y: T): boolean;           // 2 parameters
/// }
///
/// class ByteEqualityComparer extends EqualityComparer_1&lt;System.Byte&gt; {
///     Equals(obj: System.Object): boolean;   // 1 parameter - different arity!
///
///     // This pass adds:
///     Equals(x: System.Byte, y: System.Byte): boolean;  // From base, T substituted
/// }
/// </summary>
public static class BaseClassOverloadFix
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

        // Process each class/struct type
        var updatedTypes = model.Types.Select(type =>
            type.Kind == TypeKind.Class || type.Kind == TypeKind.Struct
                ? AddBaseClassOverloads(type, globalTypeLookup, ctx)
                : type
        ).ToList();

        return model with { Types = updatedTypes };
    }

    private static TypeModel AddBaseClassOverloads(TypeModel type, Dictionary<string, TypeModel> typeLookup, AnalysisContext ctx)
    {
        if (type.BaseType == null)
            return type; // No base class

        var newMethods = new List<MethodModel>();

        // For each method in the current class
        foreach (var method in type.Members.Methods)
        {
            // Skip static methods (static side inheritance works differently)
            if (method.IsStatic)
                continue;

            // Find all base class methods with the same CLR name
            var baseMethods = FindBaseClassMethods(type.BaseType, method.ClrName, typeLookup);

            // Track signatures we've already added to avoid duplicates
            var existingSignatures = new HashSet<string>();

            // Add current method's signature
            existingSignatures.Add(GetMethodSignature(method, ctx));

            // Check each base method
            foreach (var baseMethod in baseMethods)
            {
                // Only add if arity differs (different parameter count)
                if (baseMethod.Parameters.Count == method.Parameters.Count)
                    continue;

                // Build substitution map for base class generic parameters
                var baseType = FindTypeModel(type.BaseType, typeLookup);
                if (baseType == null)
                    continue;

                var substitutions = GenericSubstitution.BuildSubstitutionMap(type.BaseType, baseType.GenericParameters);

                // Substitute generic parameters in the base method
                var substitutedMethod = GenericSubstitution.SubstituteMethod(baseMethod, substitutions);

                // Check if method still references undefined type parameters
                if (ReferencesUndefinedTypeParams(substitutedMethod, type.GenericParameters, ctx))
                    continue;

                // Check if we've already added this signature
                var signature = GetMethodSignature(substitutedMethod, ctx);
                if (existingSignatures.Contains(signature))
                    continue;

                // Add as synthetic overload
                newMethods.Add(substitutedMethod with
                {
                    SyntheticOverload = new SyntheticOverloadInfo(
                        type.Binding.Type.GetClrType(),
                        ctx.GetMethodIdentifier(baseMethod),
                        SyntheticOverloadReason.BaseClassArityMismatch
                    )
                });

                existingSignatures.Add(signature);
            }
        }

        if (newMethods.Count == 0)
            return type; // No new methods to add

        // Add new methods to type
        var updatedMethods = type.Members.Methods.Concat(newMethods).ToList();
        var updatedMembers = type.Members with { Methods = updatedMethods };

        return type with { Members = updatedMembers };
    }

    /// <summary>
    /// Recursively finds all methods with the given CLR name in the base class hierarchy.
    /// </summary>
    private static List<MethodModel> FindBaseClassMethods(
        TypeReference baseTypeRef,
        string clrMethodName,
        Dictionary<string, TypeModel> typeLookup)
    {
        var result = new List<MethodModel>();

        var baseType = FindTypeModel(baseTypeRef, typeLookup);
        if (baseType == null)
            return result;

        // Find methods with matching CLR name
        foreach (var method in baseType.Members.Methods)
        {
            if (method.ClrName == clrMethodName)
            {
                result.Add(method);
            }
        }

        // Recursively check base class's base class
        if (baseType.BaseType != null)
        {
            result.AddRange(FindBaseClassMethods(baseType.BaseType, clrMethodName, typeLookup));
        }

        return result;
    }

    private static TypeModel? FindTypeModel(TypeReference typeRef, Dictionary<string, TypeModel> typeLookup)
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
        if (ReferencesUndefinedTypeParams(method.ReturnType, availableNames))
            return true;

        // Check parameters
        foreach (var param in method.Parameters)
        {
            if (ReferencesUndefinedTypeParams(param.Type, availableNames))
                return true;
        }

        return false;
    }

    private static bool ReferencesUndefinedTypeParams(TypeReference type, HashSet<string> availableNames)
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
            if (ReferencesUndefinedTypeParams(arg, availableNames))
                return true;
        }

        return false;
    }

    private static string GetMethodSignature(MethodModel method, AnalysisContext ctx)
    {
        // Create signature from method name and parameter types
        var paramTypes = string.Join(",", method.Parameters.Select(p => GetTypeSignature(p.Type)));
        return $"{ctx.GetMethodIdentifier(method)}({paramTypes})";
    }

    private static string GetTypeSignature(TypeReference type)
    {
        var ns = type.Namespace != null ? type.Namespace + "." : "";
        var genericArgs = type.GenericArgs.Count > 0
            ? "<" + string.Join(",", type.GenericArgs.Select(GetTypeSignature)) + ">"
            : "";
        return ns + type.TypeName + genericArgs;
    }

    private static string GetTypeKey(TypeReference typeRef)
    {
        var ns = typeRef.Namespace != null ? typeRef.Namespace + "." : "";
        return ns + typeRef.TypeName;
    }
}

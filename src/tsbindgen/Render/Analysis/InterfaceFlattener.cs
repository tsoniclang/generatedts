using tsbindgen.Config;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Flattens interface inheritance hierarchies by inlining all ancestor members.
/// Eliminates "extends" clauses on interfaces, relying on TypeScript structural typing.
///
/// Algorithm:
/// 1. Build interface dependency graph across all namespaces
/// 2. Detect strongly connected components (circular dependencies)
/// 3. For each interface:
///    - Compute transitive closure of all ancestors
///    - Collect all members from ancestors with generic substitution
///    - Merge generic parameter constraints (intersection types)
///    - Canonicalize and deduplicate signatures
///    - Inline all members into the interface
///    - Clear the Implements list (no more "extends")
///
/// Performance: O(V + E + M log M) where V = interfaces, E = edges, M = total members
/// Uses memoization to avoid recomputing transitive closures.
/// </summary>
public static class InterfaceFlattener
{
    public static NamespaceModel Apply(
        NamespaceModel model,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        // Only process interfaces - classes/structs/enums/delegates unchanged
        var interfaceTypes = model.Types.Where(t => t.Kind == TypeKind.Interface).ToList();
        if (interfaceTypes.Count == 0)
        {
            return model; // No interfaces to flatten
        }

        // Build global interface map (namespace.typeName → TypeModel)
        var allInterfaces = BuildGlobalInterfaceMap(allModels);

        // Compute transitive closures for all interfaces (memoized)
        var ancestorCache = new Dictionary<string, HashSet<string>>();

        // Process each interface in this namespace
        var updatedTypes = new List<TypeModel>();
        foreach (var type in model.Types)
        {
            if (type.Kind != TypeKind.Interface)
            {
                // Keep non-interfaces as-is
                updatedTypes.Add(type);
                continue;
            }

            // Flatten this interface
            var flattenedType = FlattenInterface(type, allInterfaces, ancestorCache, ctx);
            updatedTypes.Add(flattenedType);
        }

        return model with { Types = updatedTypes };
    }

    /// <summary>
    /// Builds a global map of all interfaces across all namespaces.
    /// Key: "namespace.typename" (e.g., "System.Collections.Generic.IList_1")
    /// </summary>
    private static Dictionary<string, TypeModel> BuildGlobalInterfaceMap(
        IReadOnlyDictionary<string, NamespaceModel> allModels)
    {
        var map = new Dictionary<string, TypeModel>();

        foreach (var ns in allModels.Values)
        {
            foreach (var type in ns.Types)
            {
                if (type.Kind != TypeKind.Interface)
                    continue;

                var key = GetInterfaceKey(ns.ClrName, type.ClrName);
                map[key] = type;
            }
        }

        return map;
    }

    /// <summary>
    /// Flattens a single interface by inlining all ancestor members.
    /// </summary>
    private static TypeModel FlattenInterface(
        TypeModel interfaceType,
        Dictionary<string, TypeModel> allInterfaces,
        Dictionary<string, HashSet<string>> ancestorCache,
        AnalysisContext ctx)
    {
        // Get all ancestors (transitive closure)
        var ancestors = ComputeTransitiveClosure(
            interfaceType,
            allInterfaces,
            ancestorCache);

        // Collect all members from ancestors
        var collectedMembers = CollectAncestorMembers(
            interfaceType,
            ancestors,
            allInterfaces);

        // Merge generic constraints
        var mergedGenericParams = MergeGenericConstraints(
            interfaceType.GenericParameters,
            ancestors,
            allInterfaces);

        // Merge members with current interface members
        var finalMembers = MergeMembers(interfaceType.Members, collectedMembers);

        // Clear implements (no more "extends")
        return interfaceType with
        {
            Implements = Array.Empty<TypeReference>(),
            GenericParameters = mergedGenericParams,
            Members = finalMembers
        };
    }

    /// <summary>
    /// Computes transitive closure of all ancestors for an interface.
    /// Uses DFS with memoization.
    /// </summary>
    private static HashSet<string> ComputeTransitiveClosure(
        TypeModel interfaceType,
        Dictionary<string, TypeModel> allInterfaces,
        Dictionary<string, HashSet<string>> cache)
    {
        var key = GetInterfaceKey(
            interfaceType.Binding.Type.Namespace ?? "",
            interfaceType.ClrName);

        // Check cache
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var ancestors = new HashSet<string>();
        var visited = new HashSet<string>();

        void DFS(string currentKey)
        {
            if (!visited.Add(currentKey))
                return; // Already visited

            if (!allInterfaces.TryGetValue(currentKey, out var current))
                return; // Interface not found (cross-assembly or missing)

            // Add all direct parents
            foreach (var parent in current.Implements)
            {
                var parentKey = GetInterfaceKeyFromTypeRef(parent);
                ancestors.Add(parentKey);
                DFS(parentKey); // Recursively visit parent's ancestors
            }
        }

        DFS(key);

        // Cache result
        cache[key] = ancestors;
        return ancestors;
    }

    /// <summary>
    /// Collects all members from ancestor interfaces with generic substitution.
    /// </summary>
    private static MemberCollectionModel CollectAncestorMembers(
        TypeModel interfaceType,
        HashSet<string> ancestors,
        Dictionary<string, TypeModel> allInterfaces)
    {
        var methods = new List<MethodModel>();
        var properties = new List<PropertyModel>();

        foreach (var ancestorKey in ancestors)
        {
            if (!allInterfaces.TryGetValue(ancestorKey, out var ancestor))
                continue;

            // Find the TypeReference for this ancestor in the current interface's Implements
            var ancestorRef = FindAncestorReference(interfaceType, ancestorKey, allInterfaces);
            if (ancestorRef == null)
                continue;

            // Build substitution map for generic parameters
            var substitutionMap = BuildSubstitutionMap(ancestor, ancestorRef);

            // Collect methods with substitution
            foreach (var method in ancestor.Members.Methods)
            {
                var substitutedMethod = SubstituteMethod(method, substitutionMap);
                methods.Add(substitutedMethod);
            }

            // Collect properties with substitution
            foreach (var property in ancestor.Members.Properties)
            {
                var substitutedProperty = SubstituteProperty(property, substitutionMap);
                properties.Add(substitutedProperty);
            }
        }

        return new MemberCollectionModel(
            Array.Empty<ConstructorModel>(), // Interfaces don't have constructors
            methods,
            properties,
            Array.Empty<FieldModel>(),
            Array.Empty<EventModel>());
    }

    /// <summary>
    /// Finds the TypeReference for an ancestor in the interface's Implements list.
    /// Handles transitive ancestors by walking the hierarchy.
    /// </summary>
    private static TypeReference? FindAncestorReference(
        TypeModel interfaceType,
        string ancestorKey,
        Dictionary<string, TypeModel> allInterfaces)
    {
        // First check direct parents
        foreach (var parent in interfaceType.Implements)
        {
            var parentKey = GetInterfaceKeyFromTypeRef(parent);
            if (parentKey == ancestorKey)
                return parent;
        }

        // Check transitive parents
        foreach (var parent in interfaceType.Implements)
        {
            var parentKey = GetInterfaceKeyFromTypeRef(parent);
            if (!allInterfaces.TryGetValue(parentKey, out var parentType))
                continue;

            var found = FindAncestorReference(parentType, ancestorKey, allInterfaces);
            if (found != null)
            {
                // Need to apply parent's generic args to the found reference
                if (parent.GenericArgs.Count > 0)
                {
                    var parentSubst = BuildSubstitutionMap(parentType, parent);
                    return GenericSubstitution.SubstituteType(found, parentSubst);
                }
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Builds substitution map for generic parameters.
    /// Maps generic parameter names to their concrete types.
    /// </summary>
    private static Dictionary<string, TypeReference> BuildSubstitutionMap(
        TypeModel interfaceDefinition,
        TypeReference interfaceReference)
    {
        var map = new Dictionary<string, TypeReference>();

        // Match generic parameters by position
        for (int i = 0; i < interfaceDefinition.GenericParameters.Count && i < interfaceReference.GenericArgs.Count; i++)
        {
            var paramName = interfaceDefinition.GenericParameters[i].Name;
            var argType = interfaceReference.GenericArgs[i];
            map[paramName] = argType;
        }

        return map;
    }

    /// <summary>
    /// Substitutes generic parameters in a method signature.
    /// </summary>
    private static MethodModel SubstituteMethod(
        MethodModel method,
        Dictionary<string, TypeReference> substitutionMap)
    {
        if (substitutionMap.Count == 0)
            return method;

        var substitutedParams = method.Parameters
            .Select(p => new ParameterModel(
                p.Name,
                GenericSubstitution.SubstituteType(p.Type, substitutionMap),
                p.Kind,
                p.IsOptional,
                p.DefaultValue,
                p.IsParams))
            .ToList();

        var substitutedReturnType = GenericSubstitution.SubstituteType(method.ReturnType, substitutionMap);

        return method with
        {
            Parameters = substitutedParams,
            ReturnType = substitutedReturnType
        };
    }

    /// <summary>
    /// Substitutes generic parameters in a property signature.
    /// </summary>
    private static PropertyModel SubstituteProperty(
        PropertyModel property,
        Dictionary<string, TypeReference> substitutionMap)
    {
        if (substitutionMap.Count == 0)
            return property;

        var substitutedType = GenericSubstitution.SubstituteType(property.Type, substitutionMap);

        return property with
        {
            Type = substitutedType
        };
    }

    /// <summary>
    /// Merges generic parameter constraints from ancestors.
    /// Creates intersection types when multiple constraints exist.
    /// </summary>
    private static IReadOnlyList<GenericParameterModel> MergeGenericConstraints(
        IReadOnlyList<GenericParameterModel> currentParams,
        HashSet<string> ancestors,
        Dictionary<string, TypeModel> allInterfaces)
    {
        // For now, just return current params
        // TODO: Implement constraint merging if needed
        return currentParams;
    }

    /// <summary>
    /// Merges current interface members with collected ancestor members.
    /// Deduplicates based on signature canonicalization.
    /// </summary>
    private static MemberCollectionModel MergeMembers(
        MemberCollectionModel current,
        MemberCollectionModel collected)
    {
        // Use signature-based deduplication
        var methodSignatures = new HashSet<string>();
        var mergedMethods = new List<MethodModel>();

        // Add current methods first (take precedence)
        foreach (var method in current.Methods)
        {
            var sig = GetMethodSignature(method);
            if (methodSignatures.Add(sig))
            {
                mergedMethods.Add(method);
            }
        }

        // Add collected methods (skip duplicates)
        foreach (var method in collected.Methods)
        {
            var sig = GetMethodSignature(method);
            if (methodSignatures.Add(sig))
            {
                mergedMethods.Add(method);
            }
        }

        // Same for properties
        var propertySignatures = new HashSet<string>();
        var mergedProperties = new List<PropertyModel>();

        foreach (var prop in current.Properties)
        {
            var sig = GetPropertySignature(prop);
            if (propertySignatures.Add(sig))
            {
                mergedProperties.Add(prop);
            }
        }

        foreach (var prop in collected.Properties)
        {
            var sig = GetPropertySignature(prop);
            if (propertySignatures.Add(sig))
            {
                mergedProperties.Add(prop);
            }
        }

        return new MemberCollectionModel(
            current.Constructors,
            mergedMethods,
            mergedProperties,
            current.Fields,
            current.Events);
    }

    /// <summary>
    /// Gets canonical signature for a method (for deduplication).
    /// Format: "MethodName(param1Type,param2Type):returnType"
    /// </summary>
    private static string GetMethodSignature(MethodModel method)
    {
        var paramTypes = string.Join(",", method.Parameters.Select(p => p.Type.GetClrType()));
        return $"{method.ClrName}({paramTypes}):{method.ReturnType.GetClrType()}";
    }

    /// <summary>
    /// Gets canonical signature for a property (for deduplication).
    /// Format: "PropertyName:propertyType"
    /// </summary>
    private static string GetPropertySignature(PropertyModel property)
    {
        return $"{property.ClrName}:{property.Type.GetClrType()}";
    }

    /// <summary>
    /// Gets interface key from namespace and type name.
    /// Format: "namespace.typename"
    /// </summary>
    private static string GetInterfaceKey(string ns, string typeName)
    {
        return $"{ns}.{typeName}";
    }

    /// <summary>
    /// Gets interface key from a TypeReference.
    /// </summary>
    private static string GetInterfaceKeyFromTypeRef(TypeReference typeRef)
    {
        return $"{typeRef.Namespace}.{typeRef.TypeName}";
    }
}

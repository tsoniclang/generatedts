using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Phase 3: Removes redundant interfaces from implements/extends lists.
/// Performs transitive reduction to avoid TS2320 errors.
///
/// Example:
/// - Before: ICollection_1&lt;T&gt; implements IEnumerable_1&lt;T&gt;, IEnumerable
/// - After:  ICollection_1&lt;T&gt; implements IEnumerable_1&lt;T&gt;
///
/// IEnumerable is removed because IEnumerable_1&lt;T&gt; already extends IEnumerable.
/// </summary>
public static class InterfaceReduction
{
    public static NamespaceModel Apply(NamespaceModel model)
    {
        // Build a lookup of all types in this namespace
        var typeLookup = model.Types.ToDictionary(t => GetTypeKey(t.Binding.Type), t => t);

        // Process each type
        var updatedTypes = model.Types.Select(type => ReduceInterfaces(type, typeLookup, model)).ToList();

        return model with { Types = updatedTypes };
    }

    private static TypeModel ReduceInterfaces(TypeModel type, Dictionary<string, TypeModel> typeLookup, NamespaceModel model)
    {
        if (type.Implements.Count <= 1)
            return type; // Nothing to reduce

        var reduced = PerformTransitiveReduction(type.Implements, typeLookup, model);

        return type with { Implements = reduced };
    }

    /// <summary>
    /// Performs transitive reduction: removes interfaces that are already inherited through other interfaces.
    /// </summary>
    private static IReadOnlyList<TypeReference> PerformTransitiveReduction(
        IReadOnlyList<TypeReference> interfaces,
        Dictionary<string, TypeModel> typeLookup,
        NamespaceModel model)
    {
        var toKeep = new List<TypeReference>();

        foreach (var interfaceRef in interfaces)
        {
            // Check if this interface is inherited by any OTHER interface in the list
            bool isRedundant = false;

            foreach (var otherInterfaceRef in interfaces)
            {
                if (GetTypeKey(interfaceRef) == GetTypeKey(otherInterfaceRef))
                    continue; // Skip self

                // Check if otherInterface inherits from interfaceRef
                if (InheritsFrom(otherInterfaceRef, interfaceRef, typeLookup, model, new HashSet<string>()))
                {
                    isRedundant = true;
                    break;
                }
            }

            if (!isRedundant)
            {
                toKeep.Add(interfaceRef);
            }
        }

        return toKeep;
    }

    /// <summary>
    /// Checks if 'derived' inherits from 'base' (directly or transitively).
    /// </summary>
    private static bool InheritsFrom(
        TypeReference derived,
        TypeReference baseType,
        Dictionary<string, TypeModel> typeLookup,
        NamespaceModel model,
        HashSet<string> visited)
    {
        var derivedKey = GetTypeKey(derived);

        // Prevent infinite recursion
        if (visited.Contains(derivedKey))
            return false;

        visited.Add(derivedKey);

        // Try to find the derived type definition
        TypeModel? derivedTypeDef = null;

        // First check in current namespace
        if (typeLookup.TryGetValue(derivedKey, out var localType))
        {
            derivedTypeDef = localType;
        }
        // TODO: Could also check in imported namespaces, but for now we only check locally

        if (derivedTypeDef == null)
            return false; // Can't determine, assume not inherited

        var baseKey = GetTypeKey(baseType);

        // Check direct inheritance
        foreach (var parent in derivedTypeDef.Implements)
        {
            if (GetTypeKey(parent) == baseKey)
                return true; // Direct match

            // Check transitive inheritance
            if (InheritsFrom(parent, baseType, typeLookup, model, visited))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a key for type lookup.
    /// Uses GetClrType() to get full type string including namespace and type name.
    /// </summary>
    private static string GetTypeKey(TypeReference typeRef)
    {
        // Build a key from namespace + type name (without generic arguments)
        // This allows us to match IEnumerable_1<T> with IEnumerable_1<string>, etc.
        var ns = typeRef.Namespace != null ? typeRef.Namespace + "." : "";
        return ns + typeRef.TypeName;
    }
}

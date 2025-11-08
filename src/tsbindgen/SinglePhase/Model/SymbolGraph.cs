using tsbindgen.SinglePhase.Model.Symbols;

namespace tsbindgen.SinglePhase.Model;

/// <summary>
/// The complete symbol graph for all loaded assemblies.
/// Created during Load phase, transformed during Shape phase.
/// Hierarchy: SymbolGraph → Namespaces → Types → Members
/// </summary>
public sealed class SymbolGraph
{
    /// <summary>
    /// All namespaces with their types.
    /// </summary>
    public required IReadOnlyList<NamespaceSymbol> Namespaces { get; init; }

    /// <summary>
    /// Source assembly paths that contributed to this graph.
    /// </summary>
    public required IReadOnlySet<string> SourceAssemblies { get; init; }

    /// <summary>
    /// Quick lookup: namespace name → namespace symbol.
    /// </summary>
    private readonly Dictionary<string, NamespaceSymbol> _namespaceIndex = new();

    /// <summary>
    /// Quick lookup: type full name → type symbol.
    /// </summary>
    private readonly Dictionary<string, TypeSymbol> _typeIndex = new();

    /// <summary>
    /// Initialize with namespaces and build internal indices.
    /// </summary>
    public SymbolGraph()
    {
    }

    /// <summary>
    /// Initialize and build indices after properties are set.
    /// </summary>
    public void BuildIndices()
    {
        foreach (var ns in Namespaces)
        {
            _namespaceIndex[ns.Name] = ns;

            foreach (var type in ns.Types)
            {
                _typeIndex[type.ClrFullName] = type;
                IndexNestedTypes(type);
            }
        }
    }

    private void IndexNestedTypes(TypeSymbol type)
    {
        foreach (var nested in type.NestedTypes)
        {
            _typeIndex[nested.ClrFullName] = nested;
            IndexNestedTypes(nested);
        }
    }

    /// <summary>
    /// Try to find a namespace by name.
    /// </summary>
    public bool TryGetNamespace(string name, out NamespaceSymbol? ns) =>
        _namespaceIndex.TryGetValue(name, out ns);

    /// <summary>
    /// Try to find a type by full CLR name.
    /// </summary>
    public bool TryGetType(string clrFullName, out TypeSymbol? type) =>
        _typeIndex.TryGetValue(clrFullName, out type);

    /// <summary>
    /// Get statistics about the symbol graph.
    /// </summary>
    public SymbolGraphStatistics GetStatistics()
    {
        var totalTypes = 0;
        var totalMethods = 0;
        var totalProperties = 0;
        var totalFields = 0;
        var totalEvents = 0;

        foreach (var ns in Namespaces)
        {
            foreach (var type in ns.Types)
            {
                totalTypes++;
                totalMethods += type.Members.Methods.Count;
                totalProperties += type.Members.Properties.Count;
                totalFields += type.Members.Fields.Count;
                totalEvents += type.Members.Events.Count;

                CountNestedTypes(type, ref totalTypes, ref totalMethods,
                    ref totalProperties, ref totalFields, ref totalEvents);
            }
        }

        return new SymbolGraphStatistics
        {
            NamespaceCount = Namespaces.Count,
            TypeCount = totalTypes,
            MethodCount = totalMethods,
            PropertyCount = totalProperties,
            FieldCount = totalFields,
            EventCount = totalEvents
        };
    }

    private static void CountNestedTypes(TypeSymbol type,
        ref int types, ref int methods, ref int properties, ref int fields, ref int events)
    {
        foreach (var nested in type.NestedTypes)
        {
            types++;
            methods += nested.Members.Methods.Count;
            properties += nested.Members.Properties.Count;
            fields += nested.Members.Fields.Count;
            events += nested.Members.Events.Count;

            CountNestedTypes(nested, ref types, ref methods, ref properties, ref fields, ref events);
        }
    }
}

/// <summary>
/// Statistics about a symbol graph.
/// </summary>
public sealed record SymbolGraphStatistics
{
    public required int NamespaceCount { get; init; }
    public required int TypeCount { get; init; }
    public required int MethodCount { get; init; }
    public required int PropertyCount { get; init; }
    public required int FieldCount { get; init; }
    public required int EventCount { get; init; }

    public int TotalMembers => MethodCount + PropertyCount + FieldCount + EventCount;
}

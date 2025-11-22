using System.Collections.Immutable;
using tsbindgen.Model;
using tsbindgen.Model.Symbols;
using tsbindgen.Model.Symbols.MemberSymbols;

namespace tsbindgen.Library;

/// <summary>
/// Library mode filtering - restricts emission to library contract StableIds.
/// Filtering happens before Shape passes to ensure plans only see allowed symbols.
/// </summary>
public static class LibraryFilter
{
    /// <summary>
    /// Filter symbol graph to only include types and members in library contract.
    /// Returns new graph with filtered types/members.
    /// Must run before Shape passes to ensure plans only see contract surface.
    /// </summary>
    public static SymbolGraph FilterGraph(BuildContext ctx, SymbolGraph graph, LibraryContract contract)
    {
        ctx.Log("LibraryFilter", "Filtering graph to library contract...");
        ctx.Log("LibraryFilter", $"Contract: {contract.TypeCount} types, {contract.MemberCount} members");

        var filteredNamespaces = ImmutableArray.CreateBuilder<NamespaceSymbol>();

        foreach (var ns in graph.Namespaces)
        {
            var filteredTypes = ImmutableArray.CreateBuilder<TypeSymbol>();

            foreach (var type in ns.Types)
            {
                // Check if type is in contract
                if (!IsAllowedType(type, contract))
                {
                    ctx.Log("LibraryFilter", $"Filtered out type: {type.StableId}");
                    continue;
                }

                // Filter members within allowed type
                var filteredType = FilterTypeMembers(ctx, type, contract);
                filteredTypes.Add(filteredType);
            }

            // Only keep namespace if it has types after filtering
            if (filteredTypes.Count > 0)
            {
                var filteredNs = ns with { Types = filteredTypes.ToImmutable() };
                filteredNamespaces.Add(filteredNs);
            }
            else
            {
                ctx.Log("LibraryFilter", $"Filtered out entire namespace: {ns.Name}");
            }
        }

        var result = graph with { Namespaces = filteredNamespaces.ToImmutable() };

        var stats = result.GetStatistics();
        ctx.Log("LibraryFilter", $"After filtering: {stats.NamespaceCount} namespaces, {stats.TypeCount} types, {stats.TotalMembers} members");

        return result;
    }

    /// <summary>
    /// Filter all members of a type by library contract.
    /// </summary>
    private static TypeSymbol FilterTypeMembers(BuildContext ctx, TypeSymbol type, LibraryContract contract)
    {
        var filteredMethods = type.Members.Methods
            .Where(m => IsAllowedMember(m.StableId.ToString(), contract))
            .ToImmutableArray();

        var filteredProperties = type.Members.Properties
            .Where(p => IsAllowedMember(p.StableId.ToString(), contract))
            .ToImmutableArray();

        var filteredFields = type.Members.Fields
            .Where(f => IsAllowedMember(f.StableId.ToString(), contract))
            .ToImmutableArray();

        var filteredEvents = type.Members.Events
            .Where(e => IsAllowedMember(e.StableId.ToString(), contract))
            .ToImmutableArray();

        var filteredConstructors = type.Members.Constructors
            .Where(c => IsAllowedMember(c.StableId.ToString(), contract))
            .ToImmutableArray();

        var filteredMembers = new TypeMembers
        {
            Methods = filteredMethods,
            Properties = filteredProperties,
            Fields = filteredFields,
            Events = filteredEvents,
            Constructors = filteredConstructors
        };

        return type.WithMembers(filteredMembers);
    }

    public static bool IsAllowedType(TypeSymbol type, LibraryContract contract)
    {
        var stableId = type.StableId.ToString();
        return contract.AllowedTypeStableIds.Contains(stableId);
    }

    private static bool IsAllowedMember(string memberStableId, LibraryContract contract)
    {
        return contract.AllowedMemberStableIds.Contains(memberStableId);
    }
}

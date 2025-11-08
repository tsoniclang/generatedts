using System.Collections.Generic;
using System.Linq;
using tsbindgen.Core.Renaming;
using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;

namespace tsbindgen.SinglePhase.Shape;

/// <summary>
/// Plans indexer representation (property vs methods).
/// Single uniform indexers → keep as properties
/// Multiple/heterogeneous indexers → convert to methods with configured name
/// </summary>
public static class IndexerPlanner
{
    public static void Plan(BuildContext ctx, SymbolGraph graph)
    {
        ctx.Log("IndexerPlanner: Planning indexer representations...");

        var typesWithIndexers = graph.Namespaces
            .SelectMany(ns => ns.Types)
            .Where(t => t.Members.Properties.Any(p => p.IsIndexer))
            .ToList();

        ctx.Log($"IndexerPlanner: Found {typesWithIndexers.Count} types with indexers");

        int totalConverted = 0;

        foreach (var type in typesWithIndexers)
        {
            var converted = PlanIndexersForType(ctx, type);
            totalConverted += converted;
        }

        ctx.Log($"IndexerPlanner: Converted {totalConverted} indexer groups to methods");
    }

    private static int PlanIndexersForType(BuildContext ctx, TypeSymbol type)
    {
        var indexers = type.Members.Properties
            .Where(p => p.IsIndexer)
            .ToList();

        if (indexers.Count == 0)
            return 0;

        // Group indexers by signature pattern and sort for deterministic iteration
        var indexerGroups = indexers.GroupBy(idx =>
        {
            var paramTypes = string.Join(",", idx.IndexParameters.Select(p => GetTypeFullName(p.Type)));
            return paramTypes;
        }).OrderBy(g => g.Key).ToList();

        var policy = ctx.Policy.Indexers;

        // Decision logic:
        // - Single indexer → keep as property (if policy allows)
        // - Multiple indexers → convert to methods

        if (indexers.Count == 1 && policy.EmitPropertyWhenSingle)
        {
            // Keep as property
            ctx.Log($"IndexerPlanner: Keeping single indexer as property in {type.ClrFullName}");
            return 0;
        }

        if (!policy.EmitMethodsWhenMultiple)
        {
            // Policy says don't convert - skip these indexers
            ctx.Log($"IndexerPlanner: Skipping {indexers.Count} indexers in {type.ClrFullName} (policy)");

            // Mark them as ViewOnly so they don't emit
            foreach (var indexer in indexers)
            {
                MarkAsViewOnly(indexer);
            }

            return 0;
        }

        // Convert to methods
        var methodName = policy.MethodName; // Default: "Item"

        var synthesizedMethods = new List<MethodSymbol>();

        foreach (var indexer in indexers)
        {
            var method = ConvertIndexerToMethod(ctx, type, indexer, methodName);
            synthesizedMethods.Add(method);

            // Mark original property as ViewOnly
            MarkAsViewOnly(indexer);
        }

        // Add synthesized methods to the type
        var updatedMembers = new TypeMembers
        {
            Methods = type.Members.Methods.Concat(synthesizedMethods).ToList(),
            Properties = type.Members.Properties, // Keeps original indexers but marked ViewOnly
            Fields = type.Members.Fields,
            Events = type.Members.Events,
            Constructors = type.Members.Constructors
        };

        var membersProperty = typeof(TypeSymbol).GetProperty(nameof(TypeSymbol.Members));
        membersProperty!.SetValue(type, updatedMembers);

        ctx.Log($"IndexerPlanner: Converted {indexers.Count} indexers to methods in {type.ClrFullName}");

        return 1; // Converted one group
    }

    private static MethodSymbol ConvertIndexerToMethod(BuildContext ctx, TypeSymbol type, PropertySymbol indexer, string methodName)
    {
        // Create getter method: T Item(TIndex index)
        var typeScope = new TypeScope
        {
            TypeFullName = type.ClrFullName,
            IsStatic = indexer.IsStatic,
            ScopeKey = $"{type.ClrFullName}#{(indexer.IsStatic ? "static" : "instance")}"
        };

        var stableId = new MemberStableId
        {
            AssemblyName = type.StableId.AssemblyName,
            DeclaringClrFullName = type.ClrFullName,
            MemberName = methodName,
            CanonicalSignature = ctx.CanonicalizeMethod(
                methodName,
                indexer.IndexParameters.Select(p => GetTypeFullName(p.Type)).ToList(),
                GetTypeFullName(indexer.PropertyType))
        };

        ctx.Renamer.ReserveMemberName(
            stableId,
            methodName,
            typeScope,
            "IndexSignatureMethodization",
            indexer.IsStatic);

        return new MethodSymbol
        {
            StableId = stableId,
            ClrName = methodName,
            ReturnType = indexer.PropertyType,
            Parameters = indexer.IndexParameters,
            GenericParameters = Array.Empty<GenericParameterSymbol>(),
            IsStatic = indexer.IsStatic,
            IsAbstract = false,
            IsVirtual = indexer.IsVirtual,
            IsOverride = indexer.IsOverride,
            IsSealed = false,
            IsNew = false,
            Visibility = indexer.Visibility,
            Provenance = MemberProvenance.IndexerNormalized,
            EmitScope = EmitScope.ClassSurface,
            Documentation = indexer.Documentation
        };
    }

    private static void MarkAsViewOnly(PropertySymbol property)
    {
        var emitScopeProperty = typeof(PropertySymbol).GetProperty(nameof(PropertySymbol.EmitScope));
        emitScopeProperty!.SetValue(property, EmitScope.ViewOnly);
    }

    private static string GetTypeFullName(Model.Types.TypeReference typeRef)
    {
        return typeRef switch
        {
            Model.Types.NamedTypeReference named => named.FullName,
            Model.Types.NestedTypeReference nested => nested.FullReference.FullName,
            Model.Types.GenericParameterReference gp => gp.Name,
            Model.Types.ArrayTypeReference arr => $"{GetTypeFullName(arr.ElementType)}[]",
            Model.Types.PointerTypeReference ptr => $"{GetTypeFullName(ptr.PointeeType)}*",
            Model.Types.ByRefTypeReference byref => $"{GetTypeFullName(byref.ReferencedType)}&",
            _ => typeRef.ToString() ?? "Unknown"
        };
    }
}

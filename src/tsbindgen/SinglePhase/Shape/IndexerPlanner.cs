using System.Collections.Generic;
using System.Collections.Immutable;
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
/// PURE - returns new SymbolGraph.
/// </summary>
public static class IndexerPlanner
{
    public static SymbolGraph Plan(BuildContext ctx, SymbolGraph graph)
    {
        ctx.Log("IndexerPlanner: Planning indexer representations...");

        var typesWithIndexers = graph.Namespaces
            .SelectMany(ns => ns.Types)
            .Where(t => t.Members.Properties.Any(p => p.IsIndexer))
            .ToList();

        ctx.Log($"IndexerPlanner: Found {typesWithIndexers.Count} types with indexers");

        int totalConverted = 0;
        var updatedGraph = graph;

        foreach (var type in typesWithIndexers)
        {
            bool wasConverted;
            updatedGraph = PlanIndexersForType(ctx, updatedGraph, type, out wasConverted);
            if (wasConverted)
                totalConverted++;
        }

        ctx.Log($"IndexerPlanner: Converted {totalConverted} indexer groups to methods");
        return updatedGraph;
    }

    private static SymbolGraph PlanIndexersForType(BuildContext ctx, SymbolGraph graph, TypeSymbol type, out bool wasConverted)
    {
        wasConverted = false;

        var indexers = type.Members.Properties
            .Where(p => p.IsIndexer)
            .ToList();

        if (indexers.Count == 0)
            return graph;

        var policy = ctx.Policy.Indexers;

        // Decision logic:
        // - Single indexer → keep as property (if policy allows)
        // - Multiple indexers → convert to methods

        if (indexers.Count == 1 && policy.EmitPropertyWhenSingle)
        {
            // Keep as property
            ctx.Log($"IndexerPlanner: Keeping single indexer as property in {type.ClrFullName}");
            return graph;
        }

        if (!policy.EmitMethodsWhenMultiple)
        {
            // Policy says don't convert - remove these indexers entirely
            ctx.Log($"IndexerPlanner: Removing {indexers.Count} indexers from {type.ClrFullName} (policy: no conversion)");

            // Pure transformation - remove indexers
            var graphAfterRemoval = graph.WithUpdatedType(type.ClrFullName, t =>
                t.WithRemovedProperties(p => p.IsIndexer));

            // Verify (diagnostic only)
            if (graphAfterRemoval.TryGetType(type.ClrFullName, out var removedVerify))
            {
                var remaining = removedVerify.Members.Properties.Where(p => p.IsIndexer).ToList();
                if (remaining.Count > 0)
                {
                    ctx.Log($"⚠️ WARNING: {remaining.Count} indexers still remain after removal!");
                    foreach (var r in remaining)
                        ctx.Log($"  - {r.ClrName} [{r.StableId.MemberName}{r.StableId.CanonicalSignature}]");
                }
            }

            return graphAfterRemoval;
        }

        // Convert to methods
        var methodName = policy.MethodName; // Default: "Item"

        var synthesizedMethods = indexers
            .Select(indexer => ConvertIndexerToMethod(ctx, type, indexer, methodName))
            .ToList();

        // Pure transformation - add methods and remove properties
        var updatedGraph = graph.WithUpdatedType(type.ClrFullName, t =>
            t.WithAddedMethods(synthesizedMethods)
             .WithRemovedProperties(p => p.IsIndexer));

        ctx.Log($"IndexerPlanner: Converted {indexers.Count} indexers to {synthesizedMethods.Count} methods in {type.ClrFullName}");

        // Verify (diagnostic only)
        if (updatedGraph.TryGetType(type.ClrFullName, out var verifyType))
        {
            var remaining = verifyType.Members.Properties.Where(p => p.IsIndexer).ToList();
            if (remaining.Count > 0)
            {
                ctx.Log($"⚠️ WARNING: {remaining.Count} indexers still remain after removal!");
                foreach (var r in remaining)
                    ctx.Log($"  - {r.ClrName} [{r.StableId.MemberName}{r.StableId.CanonicalSignature}]");
            }
        }

        wasConverted = true;
        return updatedGraph;
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

        // Get the final TS name from Renamer
        var tsEmitName = ctx.Renamer.GetFinalMemberName(stableId, typeScope, indexer.IsStatic);
        ctx.Log($"IndexerPlanner: Created method {methodName} with TsEmitName={tsEmitName} in {type.ClrFullName}");

        return new MethodSymbol
        {
            StableId = stableId,
            ClrName = methodName,
            TsEmitName = tsEmitName,  // Set TsEmitName from Renamer
            ReturnType = indexer.PropertyType,
            Parameters = indexer.IndexParameters,
            GenericParameters = ImmutableArray<GenericParameterSymbol>.Empty,
            IsStatic = indexer.IsStatic,
            IsAbstract = false,
            IsVirtual = indexer.IsVirtual,
            IsOverride = indexer.IsOverride,
            IsSealed = false,
            IsNew = false,
            Visibility = indexer.Visibility,
            Provenance = MemberProvenance.Synthesized,  // Synthesized from indexer
            EmitScope = EmitScope.ClassSurface,
            Documentation = indexer.Documentation
        };
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

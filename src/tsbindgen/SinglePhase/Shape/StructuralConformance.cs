using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;
using tsbindgen.SinglePhase.Model.Types;
using tsbindgen.SinglePhase.Normalize;

namespace tsbindgen.SinglePhase.Shape;

/// <summary>
/// Analyzes structural conformance for interfaces.
/// Checks if classes/structs can structurally implement their claimed interfaces in TypeScript.
/// Interfaces that can't be structurally implemented are moved to ExplicitViews (As_IInterface properties).
/// </summary>
public static class StructuralConformance
{
    public static void Analyze(BuildContext ctx, SymbolGraph graph)
    {
        ctx.Log("StructuralConformance: Analyzing structural conformance...");

        var classesAndStructs = graph.Namespaces
            .SelectMany(ns => ns.Types)
            .Where(t => t.Kind == TypeKind.Class || t.Kind == TypeKind.Struct)
            .ToList();

        int totalExplicitViews = 0;

        foreach (var type in classesAndStructs)
        {
            var explicitViews = AnalyzeType(ctx, graph, type);
            totalExplicitViews += explicitViews;
        }

        ctx.Log($"StructuralConformance: Created {totalExplicitViews} explicit interface views");
    }

    private static int AnalyzeType(BuildContext ctx, SymbolGraph graph, TypeSymbol type)
    {
        if (type.Interfaces.Length == 0)
            return 0;

        var explicitViews = new List<TypeReference>();

        foreach (var ifaceRef in type.Interfaces)
        {
            var iface = FindInterface(graph, ifaceRef);
            if (iface == null)
                continue; // External interface

            // Check structural conformance
            if (!CanStructurallyImplement(ctx, type, iface))
            {
                ctx.Log($"StructuralConformance: {type.ClrFullName} cannot structurally implement {iface.ClrFullName} - moving to explicit view");
                explicitViews.Add(ifaceRef);
            }
        }

        if (explicitViews.Count == 0)
            return 0;

        // Update type: remove these interfaces from the implements list
        // and store them for explicit view generation
        var remainingInterfaces = type.Interfaces.Except(explicitViews).ToImmutableArray();

        var interfacesProperty = typeof(TypeSymbol).GetProperty(nameof(TypeSymbol.Interfaces));
        interfacesProperty!.SetValue(type, remainingInterfaces);

        // Store explicit views for ViewPlanner to handle
        StoreExplicitViews(type, explicitViews);

        return explicitViews.Count;
    }

    private static bool CanStructurallyImplement(BuildContext ctx, TypeSymbol type, TypeSymbol iface)
    {
        // Build the "representable surface" of the type (excluding ViewOnly members)
        var representableMethods = type.Members.Methods
            .Where(m => m.EmitScope == EmitScope.ClassSurface)
            .ToList();

        var representableProperties = type.Members.Properties
            .Where(p => p.EmitScope == EmitScope.ClassSurface)
            .ToList();

        // Check if all interface requirements are met
        foreach (var requiredMethod in iface.Members.Methods)
        {
            // Use normalized signature for universal matching
            var requiredSig = SignatureNormalization.NormalizeMethod(requiredMethod);

            var exists = representableMethods.Any(m =>
            {
                var mSig = SignatureNormalization.NormalizeMethod(m);
                return mSig == requiredSig;
            });

            if (!exists)
            {
                ctx.Log($"StructuralConformance: {type.ClrFullName} missing method {requiredMethod.ClrName} for {iface.ClrFullName}");
                return false;
            }
        }

        foreach (var requiredProperty in iface.Members.Properties)
        {
            // Use normalized signature for universal matching
            var requiredSig = SignatureNormalization.NormalizeProperty(requiredProperty);

            var exists = representableProperties.Any(p =>
            {
                var pSig = SignatureNormalization.NormalizeProperty(p);
                return pSig == requiredSig;
            });

            if (!exists)
            {
                ctx.Log($"StructuralConformance: {type.ClrFullName} missing property {requiredProperty.ClrName} for {iface.ClrFullName}");
                return false;
            }
        }

        return true; // All requirements met
    }

    private static void StoreExplicitViews(TypeSymbol type, List<TypeReference> explicitViews)
    {
        // Store in a static dictionary for ViewPlanner to access
        if (!_explicitViewsByType.ContainsKey(type.ClrFullName))
        {
            _explicitViewsByType[type.ClrFullName] = new List<TypeReference>();
        }

        _explicitViewsByType[type.ClrFullName].AddRange(explicitViews);
    }

    /// <summary>
    /// Global storage for explicit views, keyed by type full name.
    /// ViewPlanner will consume this.
    /// </summary>
    private static Dictionary<string, List<TypeReference>> _explicitViewsByType = new();

    public static List<TypeReference> GetExplicitViews(string typeFullName)
    {
        if (_explicitViewsByType.TryGetValue(typeFullName, out var views))
            return views;

        return new List<TypeReference>();
    }

    private static TypeSymbol? FindInterface(SymbolGraph graph, TypeReference typeRef)
    {
        var fullName = GetTypeFullName(typeRef);

        return graph.Namespaces
            .SelectMany(ns => ns.Types)
            .FirstOrDefault(t => t.ClrFullName == fullName && t.Kind == TypeKind.Interface);
    }

    private static string GetTypeFullName(TypeReference typeRef)
    {
        return typeRef switch
        {
            NamedTypeReference named => named.FullName,
            NestedTypeReference nested => nested.FullReference.FullName,
            GenericParameterReference gp => gp.Name,
            ArrayTypeReference arr => $"{GetTypeFullName(arr.ElementType)}[]",
            PointerTypeReference ptr => $"{GetTypeFullName(ptr.PointeeType)}*",
            ByRefTypeReference byref => $"{GetTypeFullName(byref.ReferencedType)}&",
            _ => typeRef.ToString() ?? "Unknown"
        };
    }
}

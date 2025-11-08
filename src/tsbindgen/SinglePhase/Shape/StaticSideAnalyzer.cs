using System.Collections.Generic;
using System.Linq;
using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Symbols;

namespace tsbindgen.SinglePhase.Shape;

/// <summary>
/// Analyzes static-side inheritance issues.
/// Detects when static members conflict with instance members from the class hierarchy.
/// TypeScript doesn't allow the static side of a class to extend the static side of the base class,
/// which can cause TS2417 errors.
/// </summary>
public static class StaticSideAnalyzer
{
    public static void Analyze(BuildContext ctx, SymbolGraph graph)
    {
        ctx.Log("StaticSideAnalyzer: Analyzing static-side inheritance...");

        var classes = graph.Namespaces
            .SelectMany(ns => ns.Types)
            .Where(t => t.Kind == TypeKind.Class && t.BaseType != null)
            .ToList();

        int issuesFound = 0;

        foreach (var derivedClass in classes)
        {
            var issues = AnalyzeClass(ctx, graph, derivedClass);
            issuesFound += issues;
        }

        if (issuesFound > 0)
        {
            ctx.Log($"StaticSideAnalyzer: Found {issuesFound} static-side inheritance issues");
            ctx.Log("StaticSideAnalyzer: These can be resolved via renaming or explicit views if needed");
        }
        else
        {
            ctx.Log("StaticSideAnalyzer: No static-side issues detected");
        }
    }

    private static int AnalyzeClass(BuildContext ctx, SymbolGraph graph, TypeSymbol derivedClass)
    {
        var baseClass = FindBaseClass(graph, derivedClass);
        if (baseClass == null)
            return 0;

        // Get static members from both classes
        var derivedStatics = derivedClass.Members.Methods
            .Where(m => m.IsStatic)
            .Concat(derivedClass.Members.Properties.Where(p => p.IsStatic).Select(p => (object)p))
            .Concat(derivedClass.Members.Fields.Where(f => f.IsStatic).Select(f => (object)f))
            .ToList();

        var baseStatics = baseClass.Members.Methods
            .Where(m => m.IsStatic)
            .Concat(baseClass.Members.Properties.Where(p => p.IsStatic).Select(p => (object)p))
            .Concat(baseClass.Members.Fields.Where(f => f.IsStatic).Select(f => (object)f))
            .ToList();

        if (derivedStatics.Count == 0 && baseStatics.Count == 0)
            return 0;

        // Check for conflicts
        var issues = new List<string>();

        // In TypeScript, the static side doesn't automatically inherit from base
        // This can cause issues when:
        // 1. Derived has static members with same names as base but different signatures
        // 2. Derived attempts to override static members (not allowed in TS)

        var derivedStaticNames = GetStaticMemberNames(derivedStatics);
        var baseStaticNames = GetStaticMemberNames(baseStatics);

        var conflicts = derivedStaticNames.Intersect(baseStaticNames).ToList();

        if (conflicts.Count > 0)
        {
            foreach (var conflictName in conflicts)
            {
                var diagnostic = $"Static member '{conflictName}' in {derivedClass.ClrFullName} conflicts with base class {baseClass.ClrFullName}";
                ctx.Diagnostics.Warning(
                    Core.Diagnostics.DiagnosticCodes.StaticSideInheritanceIssue,
                    diagnostic);
                issues.Add(diagnostic);
            }
        }

        return issues.Count;
    }

    private static HashSet<string> GetStaticMemberNames(List<object> members)
    {
        var names = new HashSet<string>();

        foreach (var member in members)
        {
            switch (member)
            {
                case Model.Symbols.MemberSymbols.MethodSymbol m:
                    names.Add(m.ClrName);
                    break;
                case Model.Symbols.MemberSymbols.PropertySymbol p:
                    names.Add(p.ClrName);
                    break;
                case Model.Symbols.MemberSymbols.FieldSymbol f:
                    names.Add(f.ClrName);
                    break;
            }
        }

        return names;
    }

    private static TypeSymbol? FindBaseClass(SymbolGraph graph, TypeSymbol derivedClass)
    {
        if (derivedClass.BaseType == null)
            return null;

        var baseFullName = GetTypeFullName(derivedClass.BaseType);

        // Skip System.Object and System.ValueType
        if (baseFullName == "System.Object" || baseFullName == "System.ValueType")
            return null;

        return graph.Namespaces
            .SelectMany(ns => ns.Types)
            .FirstOrDefault(t => t.ClrFullName == baseFullName && t.Kind == TypeKind.Class);
    }

    private static string GetTypeFullName(Model.Types.TypeReference typeRef)
    {
        return typeRef switch
        {
            Model.Types.NamedTypeReference named => named.FullName,
            Model.Types.NestedTypeReference nested => nested.FullReference.FullName,
            _ => typeRef.ToString() ?? "Unknown"
        };
    }
}

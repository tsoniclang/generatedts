using System.Collections.Generic;
using System.Linq;
using tsbindgen.Model;
using tsbindgen.Model.Symbols;
using tsbindgen.Plan.Validation;

namespace tsbindgen.Plan;

/// <summary>
/// Plans honest TypeScript emission by identifying interfaces that cannot be satisfied.
/// </summary>
public static class HonestEmissionPlanner
{
    public static HonestEmissionPlan PlanHonestEmission(
        BuildContext ctx,
        SymbolGraph graph,
        Dictionary<string, List<string>> conformanceIssuesByType)
    {
        ctx.Log("HonestEmissionPlanner", "Planning honest TypeScript emission for unsatisfiable interfaces...");

        var unsatisfiableByType = new Dictionary<string, List<UnsatisfiableInterface>>();
        int totalUnsatisfiable = 0;

        foreach (var (typeClrName, issues) in conformanceIssuesByType)
        {
            // Parse issues to identify which specific interfaces are unsatisfiable
            var unsatisfiableInterfaces = ExtractUnsatisfiableInterfaces(graph, typeClrName, issues);

            if (unsatisfiableInterfaces.Count > 0)
            {
                unsatisfiableByType[typeClrName] = unsatisfiableInterfaces;
                totalUnsatisfiable += unsatisfiableInterfaces.Count;
            }
        }

        ctx.Log("HonestEmissionPlanner", $"Found {totalUnsatisfiable} unsatisfiable interfaces across {unsatisfiableByType.Count} types");

        // Convert to read-only dictionary
        var readonlyDict = unsatisfiableByType.ToDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<UnsatisfiableInterface>)kvp.Value);

        return new HonestEmissionPlan
        {
            UnsatisfiableInterfaces = readonlyDict,
            TotalUnsatisfiableCount = totalUnsatisfiable
        };
    }

    private static List<UnsatisfiableInterface> ExtractUnsatisfiableInterfaces(
        SymbolGraph graph,
        string typeClrName,
        List<string> issues)
    {
        var unsatisfiableInterfaces = new Dictionary<string, int>(); // Interface name → issue count

        // Parse issue strings to extract interface names
        // Format: "  Missing method Foo(2) from IBar" or "  Method Bar(1) from IBaz has incompatible TS signature"
        foreach (var issue in issues)
        {
            var fromIndex = issue.IndexOf(" from ");
            if (fromIndex >= 0)
            {
                var interfaceName = issue.Substring(fromIndex + 6).Trim();

                if (!unsatisfiableInterfaces.ContainsKey(interfaceName))
                {
                    unsatisfiableInterfaces[interfaceName] = 0;
                }
                unsatisfiableInterfaces[interfaceName]++;
            }
        }

        // Build list of UnsatisfiableInterface records
        var result = new List<UnsatisfiableInterface>();

        foreach (var (interfaceName, issueCount) in unsatisfiableInterfaces)
        {
            // Try to find the full CLR name of the interface
            var interfaceClrName = FindInterfaceClrName(graph, typeClrName, interfaceName);

            if (interfaceClrName != null)
            {
                result.Add(new UnsatisfiableInterface
                {
                    InterfaceClrName = interfaceClrName,
                    Reason = UnsatisfiableReason.MissingOrIncompatibleMembers,
                    IssueCount = issueCount
                });
            }
        }

        return result;
    }

    private static string? FindInterfaceClrName(SymbolGraph graph, string typeClrName, string interfaceShortName)
    {
        // Find the type in the graph
        if (!graph.TryGetType(typeClrName, out var typeSymbol) || typeSymbol == null)
        {
            return null;
        }

        // Find the interface reference that matches the short name
        foreach (var ifaceRef in typeSymbol.Interfaces)
        {
            var ifaceShortNameFromRef = GetShortInterfaceName(ifaceRef);
            if (ifaceShortNameFromRef == interfaceShortName)
            {
                // Return the full CLR name from the type reference
                return GetInterfaceClrFullName(ifaceRef);
            }
        }

        return null;
    }

    private static string GetShortInterfaceName(Model.Types.TypeReference typeRef)
    {
        // Extract simple name from type reference (without generic arity suffix)
        if (typeRef is not Model.Types.NamedTypeReference namedRef)
            return "";

        var clrName = namedRef.FullName;
        var lastDotIndex = clrName.LastIndexOf('.');
        var simpleName = lastDotIndex >= 0 ? clrName.Substring(lastDotIndex + 1) : clrName;

        // Remove generic arity (e.g., "IFoo`1" → "IFoo")
        var backtickIndex = simpleName.IndexOf('`');
        if (backtickIndex >= 0)
        {
            simpleName = simpleName.Substring(0, backtickIndex);
        }

        return simpleName;
    }

    private static string GetInterfaceClrFullName(Model.Types.TypeReference typeRef)
    {
        if (typeRef is Model.Types.NamedTypeReference namedRef)
            return namedRef.FullName;
        return "";
    }
}

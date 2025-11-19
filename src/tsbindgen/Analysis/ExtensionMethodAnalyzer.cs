using System.Collections.Immutable;
using tsbindgen.Model;
using tsbindgen.Model.Symbols;
using tsbindgen.Model.Symbols.MemberSymbols;
using tsbindgen.Model.Types;

namespace tsbindgen.Analysis;

/// <summary>
/// Analyzes the symbol graph to find and group extension methods by target type.
/// Pure functional analyzer - returns ExtensionMethodsPlan without mutating input.
/// </summary>
public static class ExtensionMethodAnalyzer
{
    /// <summary>
    /// Analyze the symbol graph and build a plan for emitting extension method buckets.
    /// Groups all extension methods by their target type (generic definition).
    /// </summary>
    /// <param name="ctx">Build context for logging</param>
    /// <param name="graph">Symbol graph to analyze</param>
    /// <returns>Plan containing all extension method buckets</returns>
    public static ExtensionMethodsPlan Analyze(BuildContext ctx, SymbolGraph graph)
    {
        ctx.Log("ExtensionMethodAnalyzer", "Starting extension method analysis...");

        // Step 1: Collect all extension methods from all types
        var allExtensionMethods = new List<MethodSymbol>();
        foreach (var ns in graph.Namespaces)
        {
            foreach (var type in ns.Types)
            {
                // Extension methods must be in static classes
                if (!type.IsStatic)
                    continue;

                foreach (var method in type.Members.Methods)
                {
                    if (method.IsExtensionMethod)
                    {
                        allExtensionMethods.Add(method);
                    }
                }
            }
        }

        ctx.Log("ExtensionMethodAnalyzer", $"Found {allExtensionMethods.Count} extension methods");

        // Step 2: Group by target type (generic definition)
        var buckets = new Dictionary<ExtensionTargetKey, List<MethodSymbol>>();
        var targetTypeMap = new Dictionary<ExtensionTargetKey, TypeSymbol>();

        foreach (var method in allExtensionMethods)
        {
            if (method.ExtensionTarget == null)
            {
                ctx.Log("ExtensionMethodAnalyzer",
                    $"WARNING: Extension method {method.ClrName} marked as IsExtensionMethod but has null ExtensionTarget");
                continue;
            }

            // Get the target type reference (must be a NamedTypeReference)
            if (method.ExtensionTarget is not NamedTypeReference namedRef)
            {
                ctx.Log("ExtensionMethodAnalyzer",
                    $"WARNING: Extension method {method.ClrName} target is not a named type (kind: {method.ExtensionTarget.Kind})");
                continue;
            }

            // Create key from the type reference (use FullName without type arguments for grouping)
            // Example: System.Collections.Generic.List`1
            var key = new ExtensionTargetKey
            {
                FullName = namedRef.FullName,
                Arity = namedRef.Arity
            };

            // Try to resolve the target type symbol from the graph
            var targetTypeSymbol = FindTypeByClrFullName(graph, namedRef.FullName);
            if (targetTypeSymbol == null)
            {
                ctx.Log("ExtensionMethodAnalyzer",
                    $"WARNING: Cannot find target type '{namedRef.FullName}' in graph for extension method {method.ClrName} - skipping");
                continue;
            }

            // Add to bucket
            if (!buckets.ContainsKey(key))
            {
                buckets[key] = new List<MethodSymbol>();
                targetTypeMap[key] = targetTypeSymbol;
            }

            buckets[key].Add(method);
        }

        ctx.Log("ExtensionMethodAnalyzer", $"Grouped into {buckets.Count} target type buckets");

        // Step 3: Build bucket plans
        var bucketPlans = new List<ExtensionBucketPlan>();
        foreach (var (key, methods) in buckets)
        {
            var targetType = targetTypeMap[key];
            var plan = new ExtensionBucketPlan
            {
                Key = key,
                TargetType = targetType,
                Methods = methods.ToImmutableArray()
            };
            bucketPlans.Add(plan);

            ctx.Log("ExtensionMethodAnalyzer",
                $"  Bucket: {plan.BucketInterfaceName} ({methods.Count} methods for {key.FullName})");
        }

        // Step 4: Return final plan
        var finalPlan = new ExtensionMethodsPlan
        {
            Buckets = bucketPlans.ToImmutableArray()
        };

        ctx.Log("ExtensionMethodAnalyzer",
            $"Extension method analysis complete: {finalPlan.Buckets.Length} buckets, {finalPlan.TotalMethodCount} total methods");

        return finalPlan;
    }

    /// <summary>
    /// Find a TypeSymbol in the graph by its CLR full name.
    /// </summary>
    private static TypeSymbol? FindTypeByClrFullName(SymbolGraph graph, string clrFullName)
    {
        foreach (var ns in graph.Namespaces)
        {
            foreach (var type in ns.Types)
            {
                if (type.ClrFullName == clrFullName)
                {
                    return type;
                }
            }
        }

        return null;
    }
}

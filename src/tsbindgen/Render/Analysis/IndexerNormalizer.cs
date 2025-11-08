using System.Collections.Generic;
using System.Linq;
using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Pass 3: Indexer Normalizer (fixes TS2416).
///
/// When a type has multiple indexers with the same CLR name (e.g., "Item")
/// but different parameter types:
/// 1. Suppress property declarations for that name
/// 2. Emit only method overloads
/// </summary>
public static class IndexerNormalizer
{
    /// <summary>
    /// Applies indexer normalization to resolve TS2416 errors.
    /// </summary>
    public static NamespaceModel Apply(
        NamespaceModel model,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        var updatedTypes = new List<TypeModel>();

        foreach (var type in model.Types)
        {
            var normalized = NormalizeIndexers(type, allModels, ctx);
            updatedTypes.Add(normalized);
        }

        return model with { Types = updatedTypes };
    }

    private static TypeModel NormalizeIndexers(
        TypeModel type,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        // Group methods by name to detect multiple indexer overloads
        var methodGroups = type.Members.Methods
            .GroupBy(m => m.ClrName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Find indexer names with multiple parameter signatures
        var multiParamIndexers = new HashSet<string>();

        foreach (var (methodName, methods) in methodGroups)
        {
            if (methods.Count <= 1)
                continue;

            // Check if this looks like an indexer (typically "Item" or "get_Item")
            if (!IsIndexerName(methodName))
                continue;

            // Get unique parameter type signatures
            var paramSignatures = new HashSet<string>();
            foreach (var method in methods)
            {
                var sig = FormatParameterSignature(method, ctx);
                paramSignatures.Add(sig);
            }

            if (paramSignatures.Count > 1)
            {
                // Multiple different parameter signatures - this is a multi-param indexer
                multiParamIndexers.Add(GetIndexerBaseName(methodName));
            }
        }

        if (multiParamIndexers.Count == 0)
        {
            return type; // No multi-param indexers
        }

        // Remove property declarations for multi-param indexers
        var filteredProperties = type.Members.Properties
            .Where(p => !multiParamIndexers.Contains(p.ClrName))
            .ToList();

        // Convert removed properties to method-only indexers (if not already methods)
        var additionalMethods = new List<MethodModel>();

        foreach (var indexerName in multiParamIndexers)
        {
            var property = type.Members.Properties.FirstOrDefault(p => p.ClrName == indexerName);
            if (property != null)
            {
                // Property exists but should be method-only
                // Check if we already have methods for this
                var hasGetterMethod = type.Members.Methods.Any(m => m.ClrName == indexerName || m.ClrName == $"get_{indexerName}");
                var hasSetterMethod = type.Members.Methods.Any(m => m.ClrName == $"set_{indexerName}");

                if (!hasGetterMethod && !property.IsReadonly)
                {
                    // Create getter method from property
                    var getterMethod = new MethodModel(
                        ClrName: indexerName,
                        IsStatic: property.IsStatic,
                        IsVirtual: false,
                        IsOverride: false,
                        IsAbstract: false,
                        Visibility: property.Visibility,
                        GenericParameters: new List<GenericParameterModel>(),
                        Parameters: new List<ParameterModel>
                        {
                            new ParameterModel(
                                Name: "index",
                                Type: TypeReference.CreateSimple("System", "Int32"),
                                Kind: ParameterKind.In,
                                IsOptional: false,
                                DefaultValue: null,
                                IsParams: false)
                        },
                        ReturnType: property.Type,
                        Binding: property.Binding);

                    additionalMethods.Add(getterMethod);
                }
            }
        }

        var allMethods = type.Members.Methods.Concat(additionalMethods).ToList();

        return type with
        {
            Members = type.Members with
            {
                Properties = filteredProperties,
                Methods = allMethods
            }
        };
    }

    private static bool IsIndexerName(string name)
    {
        return name == "Item" ||
               name == "get_Item" ||
               name == "set_Item" ||
               name.StartsWith("Item_") || // Synthetic indexers
               name.Contains("Indexer"); // Other indexer patterns
    }

    private static string GetIndexerBaseName(string methodName)
    {
        if (methodName.StartsWith("get_"))
            return methodName.Substring(4);
        if (methodName.StartsWith("set_"))
            return methodName.Substring(4);
        return methodName;
    }

    private static string FormatParameterSignature(MethodModel method, AnalysisContext ctx)
    {
        return string.Join(",", method.Parameters.Select(p => FormatTypeReference(p.Type, ctx)));
    }

    private static string FormatTypeReference(TypeReference typeRef, AnalysisContext ctx)
    {
        var name = typeRef.TypeName;
        if (typeRef.GenericArgs.Count > 0)
        {
            var args = string.Join(",", typeRef.GenericArgs.Select(a => FormatTypeReference(a, ctx)));
            name = $"{name}<{args}>";
        }
        return $"{typeRef.Namespace}.{name}";
    }
}

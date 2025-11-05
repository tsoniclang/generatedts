using System.Text.RegularExpressions;

namespace tsbindgen.Snapshot;

/// <summary>
/// Pure functions for aggregating assembly snapshots by namespace.
/// Merges types from multiple assemblies into namespace bundles.
/// </summary>
public static class Aggregate
{
    private static readonly Regex NamespaceRefPattern = new Regex(@"^([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+)", RegexOptions.Compiled);

    /// <summary>
    /// Aggregates assembly snapshots by namespace.
    /// Returns a dictionary of namespace name → namespace bundle.
    /// </summary>
    public static Dictionary<string, NamespaceBundle> ByNamespace(IReadOnlyList<AssemblySnapshot> snapshots)
    {
        var bundles = new Dictionary<string, NamespaceBundle>();

        foreach (var snapshot in snapshots)
        {
            foreach (var ns in snapshot.Namespaces)
            {
                if (!bundles.TryGetValue(ns.ClrName, out var bundle))
                {
                    bundle = new NamespaceBundle(
                        ns.ClrName,
                        new List<TypeSnapshot>(),
                        new Dictionary<string, HashSet<string>>(),
                        new List<Diagnostic>(),
                        new HashSet<string>());

                    bundles[ns.ClrName] = bundle;
                }

                // Merge types
                foreach (var type in ns.Types)
                {
                    bundle.Types.Add(type);

                    // Extract namespace references from type members
                    ExtractNamespaceReferences(type, bundle.Imports, ns.ClrName);
                }

                // Merge imports (assembly → namespaces)
                foreach (var import in ns.Imports)
                {
                    if (!bundle.Imports.TryGetValue(import.Assembly, out var namespaces))
                    {
                        namespaces = new HashSet<string>();
                        bundle.Imports[import.Assembly] = namespaces;
                    }
                    namespaces.Add(import.Namespace);
                }

                // Merge diagnostics
                foreach (var diagnostic in ns.Diagnostics)
                {
                    bundle.Diagnostics.Add(diagnostic);
                }

                // Track source assemblies
                bundle.SourceAssemblies.Add(snapshot.AssemblyName);
            }
        }

        return bundles;
    }

    /// <summary>
    /// Extracts namespace references from type members by parsing type strings.
    /// Type strings look like: "System_Private_CoreLib.System.Collections.Generic.IEnumerable_1"
    /// We need to extract: "System.Collections.Generic"
    /// </summary>
    private static void ExtractNamespaceReferences(TypeSnapshot type, Dictionary<string, HashSet<string>> imports, string currentNamespace)
    {
        // Process all type references in members
        foreach (var method in type.Members.Methods)
        {
            // Return type
            ExtractNamespaceFromTypeString(method.ReturnType.TsType, imports, currentNamespace);

            // Parameters
            foreach (var param in method.Parameters)
            {
                ExtractNamespaceFromTypeString(param.TsType, imports, currentNamespace);
            }
        }

        foreach (var prop in type.Members.Properties)
        {
            ExtractNamespaceFromTypeString(prop.TsType, imports, currentNamespace);
        }

        // Base type and implements
        if (type.BaseType != null)
        {
            ExtractNamespaceFromTypeString(type.BaseType.TsType, imports, currentNamespace);
        }

        foreach (var impl in type.Implements)
        {
            ExtractNamespaceFromTypeString(impl.TsType, imports, currentNamespace);
        }
    }

    private static void ExtractNamespaceFromTypeString(string typeString, Dictionary<string, HashSet<string>> imports, string currentNamespace)
    {
        // Skip generic parameters and primitive types
        if (string.IsNullOrEmpty(typeString) ||
            typeString.Length < 2 ||
            char.IsLower(typeString[0]) ||
            !typeString.Contains('.'))
        {
            return;
        }

        // Match pattern - handles two cases:
        // 1. Cross-assembly: "AssemblyAlias.Namespace.Type" (e.g., "System_Private_CoreLib.System.Collections.Generic.IEnumerable_1")
        // 2. Same assembly: "Namespace.Type" (e.g., "Microsoft.CSharp.RuntimeBinder.Binder")
        var match = NamespaceRefPattern.Match(typeString);
        if (match.Success)
        {
            var fullPath = match.Groups[1].Value;
            var parts = fullPath.Split('.');

            // Determine if this is a cross-assembly reference (has assembly prefix with underscores)
            // Assembly aliases contain underscores: System_Private_CoreLib, System_Linq_Expressions
            // Namespaces use dots: Microsoft.CSharp.RuntimeBinder
            bool hasCrossAssemblyPrefix = parts.Length >= 3 && parts[0].Contains('_');

            string ns;

            if (hasCrossAssemblyPrefix)
            {
                // Cross-assembly: ["AssemblyAlias", "Namespace", "Parts", "Type"]
                // Extract namespace by skipping assembly (first) and type name (last)
                var namespaceParts = parts.Skip(1).Take(parts.Length - 2).ToArray();
                if (namespaceParts.Length == 0) return;
                ns = string.Join(".", namespaceParts);
            }
            else
            {
                // Same assembly: ["Namespace", "Parts", "Type"]
                // Extract namespace by removing type name (last part only)
                if (parts.Length < 2) return;
                var namespaceParts = parts.Take(parts.Length - 1).ToArray();
                ns = string.Join(".", namespaceParts);
            }

            // Don't add self-references
            if (ns != currentNamespace && !string.IsNullOrEmpty(ns))
            {
                // Use a placeholder key for same-assembly references
                var key = hasCrossAssemblyPrefix ? parts[0] : "_SameAssembly";

                if (!imports.TryGetValue(key, out var namespaces))
                {
                    namespaces = new HashSet<string>();
                    imports[key] = namespaces;
                }
                namespaces.Add(ns);
            }
        }
    }
}

/// <summary>
/// A namespace bundle aggregated from multiple assemblies.
/// </summary>
public sealed record NamespaceBundle(
    string ClrName,
    List<TypeSnapshot> Types,
    Dictionary<string, HashSet<string>> Imports, // assembly → namespaces
    List<Diagnostic> Diagnostics,
    HashSet<string> SourceAssemblies);

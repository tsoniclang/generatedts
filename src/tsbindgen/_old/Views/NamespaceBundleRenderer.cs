using System.Text;
using System.Text.Json;
using tsbindgen.Snapshot;
using tsbindgen.Config;

namespace tsbindgen.Views;

/// <summary>
/// Renders namespace bundles to disk.
/// Generates index.d.ts, metadata.json, and bindings.json for each namespace.
/// </summary>
public sealed class NamespaceBundleRenderer
{
    private readonly string _outputDir;
    private readonly GeneratorConfig _config;
    private Dictionary<string, NameBinding> _currentBindings = new();
    private string _currentNamespace = "";

    public NamespaceBundleRenderer(string outputDir, GeneratorConfig config)
    {
        _outputDir = outputDir;
        _config = config;
    }

    /// <summary>
    /// Renders all namespace bundles to namespaces/ directory.
    /// </summary>
    public async Task RenderAllAsync(Dictionary<string, NamespaceBundle> bundles)
    {
        var manifestEntries = new List<NamespaceManifestEntry>();

        foreach (var (nsName, bundle) in bundles)
        {
            var nsDir = Path.Combine(_outputDir, "namespaces", nsName);
            Directory.CreateDirectory(nsDir);

            // Reset bindings for this namespace
            _currentBindings.Clear();

            // Serialize namespace snapshot (before rendering)
            var snapshotPath = Path.Combine(nsDir, "snapshot.json");
            await RenderNamespaceSnapshotAsync(bundle, snapshotPath);

            // Generate index.d.ts
            var dtsPath = Path.Combine(nsDir, "index.d.ts");
            await RenderDeclarationsAsync(bundle, dtsPath);

            // Generate metadata.json
            var metadataPath = Path.Combine(nsDir, "metadata.json");
            await RenderMetadataAsync(bundle, metadataPath);

            // Generate bindings.json (only if transforms were applied)
            if (_currentBindings.Count > 0)
            {
                var bindingsPath = Path.Combine(nsDir, "bindings.json");
                await RenderBindingsAsync(_currentBindings, bindingsPath);
            }

            // Generate index.js (module stub)
            var jsPath = Path.Combine(nsDir, "index.js");
            await RenderModuleStubAsync(bundle, jsPath);

            // Compute namespace alias (apply namespace name transform)
            var nsAlias = NameTransform.Apply(bundle.ClrName, _config.NamespaceNames);

            manifestEntries.Add(new NamespaceManifestEntry(
                nsName,
                nsAlias,
                bundle.Types.Count,
                bundle.SourceAssemblies.ToList(),
                $"{nsName}/snapshot.json"));
        }

        // Write namespaces-manifest.json
        var manifestPath = Path.Combine(_outputDir, "namespaces", "namespaces-manifest.json");
        await WriteManifestAsync(manifestEntries, manifestPath);
    }

    private async Task RenderDeclarationsAsync(NamespaceBundle bundle, string outputPath)
    {
        // Set current namespace for type reference rewriting
        _currentNamespace = bundle.ClrName;

        var sb = new StringBuilder();

        // Add branded type aliases (intrinsics)
        sb.AppendLine("// Branded numeric types");
        sb.AppendLine("type byte = number & { __brand: \"byte\" };");
        sb.AppendLine("type sbyte = number & { __brand: \"sbyte\" };");
        sb.AppendLine("type short = number & { __brand: \"short\" };");
        sb.AppendLine("type ushort = number & { __brand: \"ushort\" };");
        sb.AppendLine("type int = number & { __brand: \"int\" };");
        sb.AppendLine("type uint = number & { __brand: \"uint\" };");
        sb.AppendLine("type long = number & { __brand: \"long\" };");
        sb.AppendLine("type ulong = number & { __brand: \"ulong\" };");
        sb.AppendLine("type float = number & { __brand: \"float\" };");
        sb.AppendLine("type double = number & { __brand: \"double\" };");
        sb.AppendLine("type decimal = number & { __brand: \"decimal\" };");
        sb.AppendLine();

        // Add imports (deduplicate and exclude self-references)
        var referencedNamespaces = bundle.Imports.Values
            .SelectMany(ns => ns)
            .Where(ns => ns != bundle.ClrName)  // Exclude self-references
            .Distinct()
            .OrderBy(ns => ns)
            .ToList();

        if (referencedNamespaces.Any())
        {
            sb.AppendLine("// Cross-namespace dependencies");
            foreach (var ns in referencedNamespaces)
            {
                sb.AppendLine($"import type * as {MakeImportAlias(ns)} from \"../{ns}/index.js\";");
            }
            sb.AppendLine();
        }

        // Export namespace (compute alias from CLR name)
        var nsAlias = NameTransform.Apply(bundle.ClrName, _config.NamespaceNames);
        sb.AppendLine($"export namespace {nsAlias} {{");
        sb.AppendLine();

        // Render types
        foreach (var type in bundle.Types.OrderBy(t => t.ClrName))
        {
            RenderType(sb, type, "    ");
        }

        sb.AppendLine("}");
        sb.AppendLine();

        // Use ESM export syntax - export empty to make it a module
        sb.AppendLine("export {};");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private void RenderType(StringBuilder sb, TypeSnapshot type, string indent)
    {
        switch (type.Kind)
        {
            case TypeKind.Class:
                RenderClass(sb, type, indent);
                break;
            case TypeKind.Interface:
                RenderInterface(sb, type, indent);
                break;
            case TypeKind.Enum:
                RenderEnum(sb, type, indent);
                break;
            case TypeKind.StaticNamespace:
                RenderStaticNamespace(sb, type, indent);
                break;
            case TypeKind.Delegate:
                RenderDelegate(sb, type, indent);
                break;
        }
    }

    private void RenderClass(StringBuilder sb, TypeSnapshot type, string indent)
    {
        // Compute type alias from CLR name
        var typeAlias = NameTransform.Apply(type.ClrName, _config.ClassNames);

        sb.Append($"{indent}");
        if (type.IsStatic) sb.Append("static ");
        if (type.IsAbstract) sb.Append("abstract ");
        sb.Append($"class {typeAlias}");

        if (type.GenericParameters.Any())
        {
            sb.Append("<");
            sb.Append(string.Join(", ", type.GenericParameters.Select(g => g.Name)));
            sb.Append(">");
        }

        if (type.BaseType != null)
        {
            sb.Append($" extends {RewriteTypeReference(type.BaseType.TsType, _currentNamespace)}");
        }

        if (type.Implements.Any())
        {
            sb.Append(" implements ");
            sb.Append(string.Join(", ", type.Implements.Select(i => RewriteTypeReference(i.TsType, _currentNamespace))));
        }

        sb.AppendLine(" {");

        // Render members
        RenderMembers(sb, type.Members, indent + "    ");

        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    private void RenderInterface(StringBuilder sb, TypeSnapshot type, string indent)
    {
        // Compute type alias from CLR name
        var typeAlias = NameTransform.Apply(type.ClrName, _config.InterfaceNames);

        sb.Append($"{indent}interface {typeAlias}");

        if (type.GenericParameters.Any())
        {
            sb.Append("<");
            sb.Append(string.Join(", ", type.GenericParameters.Select(g => g.Name)));
            sb.Append(">");
        }

        if (type.Implements.Any())
        {
            sb.Append(" extends ");
            sb.Append(string.Join(", ", type.Implements.Select(i => RewriteTypeReference(i.TsType, _currentNamespace))));
        }

        sb.AppendLine(" {");

        // Render members
        RenderMembers(sb, type.Members, indent + "    ");

        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    private void RenderEnum(StringBuilder sb, TypeSnapshot type, string indent)
    {
        // Compute type alias from CLR name (enums use class naming convention)
        var typeAlias = NameTransform.Apply(type.ClrName, _config.ClassNames);

        sb.AppendLine($"{indent}enum {typeAlias} {{");

        if (type.EnumMembers != null)
        {
            foreach (var member in type.EnumMembers)
            {
                sb.AppendLine($"{indent}    {member.Name} = {member.Value},");
            }
        }

        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    private void RenderStaticNamespace(StringBuilder sb, TypeSnapshot type, string indent)
    {
        // Compute type alias from CLR name (static namespaces use class naming convention)
        var typeAlias = NameTransform.Apply(type.ClrName, _config.ClassNames);

        sb.AppendLine($"{indent}namespace {typeAlias} {{");

        // Render members without 'static' keyword (namespace members are implicitly static)
        RenderNamespaceMembers(sb, type.Members, indent + "    ");

        sb.AppendLine($"{indent}}}");
        sb.AppendLine();
    }

    private void RenderNamespaceMembers(StringBuilder sb, MemberCollection members, string indent)
    {
        // Properties (as const)
        foreach (var prop in members.Properties)
        {
            // Apply transform and track binding
            var propName = ApplyTransform(
                prop.ClrName,
                prop.Binding.Type + "." + prop.Binding.Member,
                "property",
                _config.PropertyNames,
                _currentBindings);

            sb.Append($"{indent}");
            if (prop.IsReadOnly) sb.Append("const ");
            else sb.Append("let ");
            sb.AppendLine($"{propName}: {RewriteTypeReference(prop.TsType, _currentNamespace)};");
        }

        // Methods (as functions)
        foreach (var method in members.Methods)
        {
            // Apply transform and track binding
            var methodName = ApplyTransform(
                method.ClrName,
                method.Binding.Type + "." + method.Binding.Member,
                "method",
                _config.MethodNames,
                _currentBindings);

            sb.Append($"{indent}function {methodName}");

            if (method.GenericParameters.Any())
            {
                sb.Append("<");
                sb.Append(string.Join(", ", method.GenericParameters.Select(g => g.Name)));
                sb.Append(">");
            }

            sb.Append("(");
            sb.Append(string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {RewriteTypeReference(p.TsType, _currentNamespace)}")));
            sb.Append("): ");
            sb.Append(RewriteTypeReference(method.ReturnType.TsType, _currentNamespace));
            sb.AppendLine(";");
        }
    }

    private void RenderDelegate(StringBuilder sb, TypeSnapshot type, string indent)
    {
        // Compute type alias from CLR name (delegates use class naming convention)
        var typeAlias = NameTransform.Apply(type.ClrName, _config.ClassNames);

        sb.Append($"{indent}type {typeAlias}");

        if (type.GenericParameters.Any())
        {
            sb.Append("<");
            sb.Append(string.Join(", ", type.GenericParameters.Select(g => g.Name)));
            sb.Append(">");
        }

        sb.Append(" = (");

        if (type.DelegateParameters != null)
        {
            sb.Append(string.Join(", ", type.DelegateParameters.Select(p =>
                $"{p.Name}: {RewriteTypeReference(p.TsType, _currentNamespace)}")));
        }

        sb.Append(") => ");
        sb.Append(RewriteTypeReference(type.DelegateReturnType?.TsType ?? "void", _currentNamespace));
        sb.AppendLine(";");
        sb.AppendLine();
    }

    private void RenderMembers(StringBuilder sb, MemberCollection members, string indent)
    {
        // Constructors
        foreach (var ctor in members.Constructors)
        {
            sb.Append($"{indent}constructor(");
            sb.Append(string.Join(", ", ctor.Parameters.Select(p => $"{p.Name}: {RewriteTypeReference(p.TsType, _currentNamespace)}")));
            sb.AppendLine(");");
        }

        // Properties
        foreach (var prop in members.Properties)
        {
            // Apply transform and track binding
            var propName = ApplyTransform(
                prop.ClrName,
                prop.Binding.Type + "." + prop.Binding.Member,
                "property",
                _config.PropertyNames,
                _currentBindings);

            sb.Append($"{indent}");
            if (prop.IsStatic) sb.Append("static ");
            if (prop.IsReadOnly) sb.Append("readonly ");
            sb.AppendLine($"{propName}: {RewriteTypeReference(prop.TsType, _currentNamespace)};");
        }

        // Methods
        foreach (var method in members.Methods)
        {
            // Apply transform and track binding
            var methodName = ApplyTransform(
                method.ClrName,
                method.Binding.Type + "." + method.Binding.Member,
                "method",
                _config.MethodNames,
                _currentBindings);

            sb.Append($"{indent}");
            if (method.IsStatic) sb.Append("static ");
            sb.Append(methodName);

            if (method.GenericParameters.Any())
            {
                sb.Append("<");
                sb.Append(string.Join(", ", method.GenericParameters.Select(g => g.Name)));
                sb.Append(">");
            }

            sb.Append("(");
            sb.Append(string.Join(", ", method.Parameters.Select(p => $"{p.Name}: {RewriteTypeReference(p.TsType, _currentNamespace)}")));
            sb.Append("): ");
            sb.Append(RewriteTypeReference(method.ReturnType.TsType, _currentNamespace));
            sb.AppendLine(";");
        }
    }

    private async Task RenderNamespaceSnapshotAsync(NamespaceBundle bundle, string outputPath)
    {
        // Serialize the complete namespace bundle for debugging and tooling
        var json = JsonSerializer.Serialize(bundle, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        });
        await File.WriteAllTextAsync(outputPath, json);
    }

    private async Task RenderMetadataAsync(NamespaceBundle bundle, string outputPath)
    {
        // TODO: Convert TypeSnapshot → existing metadata format
        // For now, write empty structure
        var metadata = new { };
        var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true
        });
        await File.WriteAllTextAsync(outputPath, json);
    }

    private async Task RenderBindingsAsync(Dictionary<string, NameBinding> bindings, string outputPath)
    {
        var json = JsonSerializer.Serialize(bindings, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(outputPath, json);
    }

    private async Task RenderModuleStubAsync(NamespaceBundle bundle, string outputPath)
    {
        // ESM stub - re-export the namespace as a named export
        var sb = new StringBuilder();
        sb.AppendLine($"// ESM module stub for {bundle.ClrName}");
        sb.AppendLine($"// Re-exports namespace for modern module consumption");
        sb.AppendLine();

        // Import and re-export - but we need an actual runtime implementation
        // For now, create a placeholder that tools can use
        sb.AppendLine($"// This is a type-only module - runtime implementation provided by Tsonic");
        sb.AppendLine($"export {{}};");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private async Task WriteManifestAsync(List<NamespaceManifestEntry> entries, string outputPath)
    {
        // Ensure directory exists
        var directory = Path.GetDirectoryName(outputPath);
        if (directory != null)
        {
            Directory.CreateDirectory(directory);
        }

        var manifest = new { Namespaces = entries };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        await File.WriteAllTextAsync(outputPath, json);
    }

    private string MakeImportAlias(string namespaceName)
    {
        // Convert System.Collections.Generic → System$Collections$Generic ($ separator)
        return namespaceName.Replace(".", "$");
    }

    /// <summary>
    /// Rewrites type references from assembly-prefixed format to import alias format.
    /// Example: "System_Private_CoreLib.System.Collections.Generic.IEnumerable_1<T>"
    ///       → "System$Collections$Generic.IEnumerable_1<T>"
    /// Note: Import alias uses $ separator for readability
    /// </summary>
    private string RewriteTypeReference(string typeString, string currentNamespace)
    {
        if (string.IsNullOrEmpty(typeString) || !typeString.Contains('.'))
        {
            return typeString;
        }

        // Handles two formats:
        // 1. Cross-assembly: "AssemblyAlias.Namespace.Type" (e.g., "System_Private_CoreLib.System.Collections.Generic.List_1")
        // 2. Same assembly: "Namespace.Type" (e.g., "Microsoft.CSharp.RuntimeBinder.Binder")
        var parts = typeString.Split(new[] { '<', '>', '(', ')', ',', ' ', '[', ']' }, StringSplitOptions.RemoveEmptyEntries);

        var result = typeString;
        foreach (var part in parts)
        {
            if (!part.Contains('.') || part.Split('.').Length < 2)
            {
                continue;
            }

            var segments = part.Split('.');

            // Determine if cross-assembly (first segment has underscores like System_Private_CoreLib)
            bool isCrossAssembly = segments.Length >= 3 && segments[0].Contains('_');

            string rewritten;

            if (isCrossAssembly)
            {
                // Cross-assembly: ["AssemblyAlias", "Namespace", "Parts", "Type"]
                // Skip assembly alias, convert namespace to import alias
                var namespaceParts = segments.Skip(1).Take(segments.Length - 2);
                var typeName = segments[segments.Length - 1];

                if (namespaceParts.Any())
                {
                    var importAlias = string.Join("$", namespaceParts);
                    rewritten = $"{importAlias}.{typeName}";
                    result = result.Replace(part, rewritten);
                }
            }
            else
            {
                // Same assembly: ["Namespace", "Parts", "Type"]
                // Check if this is a reference to the CURRENT namespace
                var namespaceParts = segments.Take(segments.Length - 1).ToArray();
                var ns = string.Join(".", namespaceParts);
                var typeName = segments[segments.Length - 1];

                if (ns == currentNamespace)
                {
                    // Same namespace - use unqualified type name
                    rewritten = typeName;
                }
                else
                {
                    // Different namespace in same assembly - use import alias
                    var importAlias = string.Join("$", namespaceParts);
                    rewritten = $"{importAlias}.{typeName}";
                }

                result = result.Replace(part, rewritten);
            }
        }

        return result;
    }

    /// <summary>
    /// Apply name transformation and track binding if transformed.
    /// </summary>
    private string ApplyTransform(
        string clrName,
        string fullName,
        string kind,
        NameTransformOption option,
        Dictionary<string, NameBinding> bindings)
    {
        var alias = NameTransform.Apply(clrName, option);

        // Track binding if transform option is enabled (even if name didn't change)
        // This happens when clrName is already in the target case
        if (option != NameTransformOption.None && alias != clrName)
        {
            bindings[clrName] = new NameBinding
            {
                Kind = kind,
                Name = clrName,
                Alias = alias,
                FullName = fullName
            };
        }

        return alias;
    }
}

/// <summary>
/// Entry in namespace manifest.
/// </summary>
public sealed record NamespaceManifestEntry(
    string Name,
    string Alias,
    int TypeCount,
    IReadOnlyList<string> SourceAssemblies,
    string Snapshot);

/// <summary>
/// Binding entry for name transforms (used in bindings.json).
/// </summary>
public sealed class NameBinding
{
    public string Kind { get; set; } = "";
    public string Name { get; set; } = "";
    public string Alias { get; set; } = "";
    public string FullName { get; set; } = "";
}

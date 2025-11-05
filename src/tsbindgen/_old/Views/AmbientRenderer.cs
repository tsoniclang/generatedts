using System.Text;
using tsbindgen.Config;

namespace tsbindgen.Views;

/// <summary>
/// Renders ambient declarations (global.d.ts).
/// Creates global type augmentation with references to namespace bundles.
/// </summary>
public sealed class AmbientRenderer
{
    private readonly string _outputDir;
    private readonly GeneratorConfig _config;

    public AmbientRenderer(string outputDir, GeneratorConfig config)
    {
        _outputDir = outputDir;
        _config = config;
    }

    /// <summary>
    /// Renders global.d.ts with references to all namespace bundles.
    /// </summary>
    public async Task RenderAsync(Dictionary<string, NamespaceBundle> bundles)
    {
        var ambientDir = Path.Combine(_outputDir, "ambient");
        Directory.CreateDirectory(ambientDir);

        var outputPath = Path.Combine(ambientDir, "global.d.ts");

        var sb = new StringBuilder();

        sb.AppendLine("// Global ambient declarations for Tsonic runtime");
        sb.AppendLine("// This file provides global access to .NET BCL types");
        sb.AppendLine();

        // Add triple-slash references to all namespace bundles
        foreach (var nsName in bundles.Keys.OrderBy(x => x))
        {
            var relativePath = $"../namespaces/{nsName}/index.d.ts";
            sb.AppendLine($"/// <reference path=\"{relativePath}\" />");
        }

        sb.AppendLine();
        sb.AppendLine("declare global {");
        sb.AppendLine("    namespace Tsonic.Runtime {");

        // Re-export all namespaces (compute aliases from CLR names)
        foreach (var (nsName, bundle) in bundles.OrderBy(x => x.Key))
        {
            var nsAlias = NameTransform.Apply(bundle.ClrName, _config.NamespaceNames);
            sb.AppendLine($"        export import {nsAlias} = {nsAlias};");
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("export {};");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }
}

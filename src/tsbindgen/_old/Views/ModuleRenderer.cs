using System.Text;

namespace tsbindgen.Views;

/// <summary>
/// Renders module entry points for package consumption.
/// Creates module-style imports like @tsonic/dotnet/system/linq.
/// </summary>
public sealed class ModuleRenderer
{
    private readonly string _outputDir;

    public ModuleRenderer(string outputDir)
    {
        _outputDir = outputDir;
    }

    /// <summary>
    /// Renders all module entry points.
    /// </summary>
    public async Task RenderAllAsync(Dictionary<string, NamespaceBundle> bundles)
    {
        // Create dotnet modules (namespace-based)
        await RenderDotnetModulesAsync(bundles);

        // TODO: Create runtime modules (mapping-based, e.g., @tsonic/node/fs → System.IO)
        // This requires a separate configuration file defining the mappings
    }

    private async Task RenderDotnetModulesAsync(Dictionary<string, NamespaceBundle> bundles)
    {
        foreach (var (nsName, bundle) in bundles)
        {
            // Convert System.Collections.Generic → dotnet/system/collections/generic
            var modulePath = ConvertNamespaceToModulePath(nsName);
            var moduleDir = Path.Combine(_outputDir, "modules", "dotnet", modulePath);
            Directory.CreateDirectory(moduleDir);

            // Generate index.d.ts
            var dtsPath = Path.Combine(moduleDir, "index.d.ts");
            await RenderModuleDeclarationAsync(bundle, nsName, dtsPath);

            // Generate index.js
            var jsPath = Path.Combine(moduleDir, "index.js");
            await RenderModuleExportAsync(bundle, nsName, jsPath);
        }
    }

    private async Task RenderModuleDeclarationAsync(NamespaceBundle bundle, string nsName, string outputPath)
    {
        var sb = new StringBuilder();

        // Calculate relative path to namespace bundle
        var depth = nsName.Split('.').Length + 2; // +2 for modules/dotnet
        var relativePath = string.Join("/", Enumerable.Repeat("..", depth)) +
                          $"/namespaces/{nsName}/index.d.ts";

        sb.AppendLine($"// Module entry point for {nsName}");
        sb.AppendLine($"/// <reference path=\"{relativePath}\" />");
        sb.AppendLine();
        sb.AppendLine($"export * from \"{relativePath.Replace(".d.ts", ".js")}\";");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private async Task RenderModuleExportAsync(NamespaceBundle bundle, string nsName, string outputPath)
    {
        var sb = new StringBuilder();

        // Calculate relative path to namespace bundle
        var depth = nsName.Split('.').Length + 2; // +2 for modules/dotnet
        var relativePath = string.Join("/", Enumerable.Repeat("..", depth)) +
                          $"/namespaces/{nsName}/index.js";

        sb.AppendLine($"// Module entry point for {nsName}");
        sb.AppendLine($"export * from \"{relativePath}\";");

        await File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    private string ConvertNamespaceToModulePath(string namespaceName)
    {
        // System.Collections.Generic → system/collections/generic
        return namespaceName.ToLowerInvariant().Replace(".", "/");
    }
}

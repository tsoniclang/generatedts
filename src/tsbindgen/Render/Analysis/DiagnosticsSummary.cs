using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Consolidates and summarizes diagnostics.
/// </summary>
public static class DiagnosticsSummary
{
    public static NamespaceModel Apply(NamespaceModel model)
    {
        // Collect all diagnostics from namespace and types
        var allDiagnostics = new List<Diagnostic>(model.Diagnostics);

        foreach (var type in model.Types)
        {
            allDiagnostics.AddRange(type.Diagnostics);
        }

        // Deduplicate by code and message
        var unique = allDiagnostics
            .GroupBy(d => (d.Code, d.Message))
            .Select(g => g.First())
            .OrderBy(d => d.Severity)
            .ThenBy(d => d.Code)
            .ToList();

        return model with { Diagnostics = unique };
    }
}

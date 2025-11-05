using tsbindgen.Snapshot;

namespace tsbindgen.Render;

/// <summary>
/// Namespace model after normalization and analysis.
/// Ready for rendering to TypeScript declarations.
/// </summary>
public sealed record NamespaceModel(
    string ClrName,
    string TsAlias,
    IReadOnlyList<TypeModel> Types,
    IReadOnlyDictionary<string, IReadOnlySet<string>> Imports,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> SourceAssemblies);

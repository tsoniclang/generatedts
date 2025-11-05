namespace tsbindgen.Render;

/// <summary>
/// All artifacts generated for a single namespace.
/// </summary>
public sealed record NamespaceArtifacts(
    string NamespaceName,
    string TsAlias,
    string DtsContent,
    string MetadataContent,
    string? BindingsContent,
    string JsStubContent,
    string SnapshotContent);

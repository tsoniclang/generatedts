using tsbindgen.Config;
using tsbindgen.Snapshot;

namespace tsbindgen.Render;

/// <summary>
/// Type model after normalization and analysis.
/// </summary>
public sealed record TypeModel(
    string ClrName,
    TypeKind Kind,
    bool IsStatic,
    bool IsSealed,
    bool IsAbstract,
    string Visibility,
    IReadOnlyList<GenericParameterModel> GenericParameters,
    TypeReference? BaseType,
    IReadOnlyList<TypeReference> Implements,
    MemberCollectionModel Members,
    BindingInfo Binding,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<HelperDeclaration> Helpers,
    // Enum-specific
    string? UnderlyingType = null,
    IReadOnlyList<EnumMember>? EnumMembers = null,
    // Delegate-specific
    IReadOnlyList<ParameterModel>? DelegateParameters = null,
    TypeReference? DelegateReturnType = null)
{
    /// <summary>
    /// TypeScript alias for analysis and lookups (uses underscore for nesting).
    /// Computed from TypeReference structure - no heuristics.
    /// Example: "Console_Error_1"
    /// </summary>
    public string TsAlias => TsNaming.ForAnalysis(Binding.Type);

    private string? _tsEmitName;

    /// <summary>
    /// TypeScript emit name for .d.ts declarations (uses dollar for nesting).
    /// Computed from TypeReference structure - no heuristics.
    /// Example: "Console$Error_1"
    /// </summary>
    public string TsEmitName => _tsEmitName ??= TsNaming.ForEmit(Binding.Type);
};

/// <summary>
/// Generic parameter with constraints.
/// </summary>
public sealed record GenericParameterModel(
    string Name,
    string TsAlias,
    IReadOnlyList<TypeReference> Constraints,
    Variance Variance);

/// <summary>
/// Collection of all members for a type.
/// </summary>
public sealed record MemberCollectionModel(
    IReadOnlyList<ConstructorModel> Constructors,
    IReadOnlyList<MethodModel> Methods,
    IReadOnlyList<PropertyModel> Properties,
    IReadOnlyList<FieldModel> Fields,
    IReadOnlyList<EventModel> Events);

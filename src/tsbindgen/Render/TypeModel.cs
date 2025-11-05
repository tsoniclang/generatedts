using tsbindgen.Snapshot;

namespace tsbindgen.Render;

/// <summary>
/// Type model after normalization and analysis.
/// </summary>
public sealed record TypeModel(
    string ClrName,
    string TsAlias,
    TypeKind Kind,
    bool IsStatic,
    bool IsSealed,
    bool IsAbstract,
    string Visibility,
    IReadOnlyList<GenericParameterModel> GenericParameters,
    TypeReferenceModel? BaseType,
    IReadOnlyList<TypeReferenceModel> Implements,
    MemberCollectionModel Members,
    BindingInfo Binding,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<HelperDeclaration> Helpers,
    // Enum-specific
    string? UnderlyingType = null,
    IReadOnlyList<EnumMember>? EnumMembers = null,
    // Delegate-specific
    IReadOnlyList<ParameterModel>? DelegateParameters = null,
    TypeReferenceModel? DelegateReturnType = null);

/// <summary>
/// Generic parameter with constraints.
/// </summary>
public sealed record GenericParameterModel(
    string Name,
    string TsAlias,
    IReadOnlyList<string> Constraints,
    Variance Variance);

/// <summary>
/// Type reference with both CLR and TS names.
/// </summary>
public sealed record TypeReferenceModel(
    string ClrType,
    string TsType,
    string? Assembly);

/// <summary>
/// Collection of all members for a type.
/// </summary>
public sealed record MemberCollectionModel(
    IReadOnlyList<ConstructorModel> Constructors,
    IReadOnlyList<MethodModel> Methods,
    IReadOnlyList<PropertyModel> Properties,
    IReadOnlyList<FieldModel> Fields,
    IReadOnlyList<EventModel> Events);

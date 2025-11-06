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
    TypeReference? DelegateReturnType = null);

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

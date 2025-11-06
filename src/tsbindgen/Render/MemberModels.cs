using tsbindgen.Snapshot;

namespace tsbindgen.Render;

/// <summary>
/// Constructor model.
/// </summary>
public sealed record ConstructorModel(
    string Visibility,
    IReadOnlyList<ParameterModel> Parameters);

/// <summary>
/// Method model with both CLR and TS names.
/// </summary>
public sealed record MethodModel(
    string ClrName,
    string TsAlias,
    bool IsStatic,
    bool IsVirtual,
    bool IsOverride,
    bool IsAbstract,
    string Visibility,
    IReadOnlyList<GenericParameterModel> GenericParameters,
    IReadOnlyList<ParameterModel> Parameters,
    TypeReference ReturnType,
    MemberBinding Binding);

/// <summary>
/// Property model with both CLR and TS names.
/// </summary>
public sealed record PropertyModel(
    string ClrName,
    string TsAlias,
    TypeReference Type,
    bool IsReadonly,
    bool IsStatic,
    bool IsVirtual,
    bool IsOverride,
    string Visibility,
    MemberBinding Binding,
    TypeReference? ContractType);  // If not null, property has covariant return type

/// <summary>
/// Field model with both CLR and TS names.
/// </summary>
public sealed record FieldModel(
    string ClrName,
    string TsAlias,
    TypeReference Type,
    bool IsReadonly,
    bool IsStatic,
    string Visibility,
    MemberBinding Binding);

/// <summary>
/// Event model with both CLR and TS names.
/// </summary>
public sealed record EventModel(
    string ClrName,
    string TsAlias,
    TypeReference Type,
    bool IsStatic,
    string Visibility,
    MemberBinding Binding);

/// <summary>
/// Parameter model.
/// </summary>
public sealed record ParameterModel(
    string Name,
    TypeReference Type,
    ParameterKind Kind,
    bool IsOptional,
    string? DefaultValue,
    bool IsParams);

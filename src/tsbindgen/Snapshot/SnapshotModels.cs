using System.Text.Json.Serialization;

namespace tsbindgen.Snapshot;

/// <summary>
/// Root snapshot model for an assembly.
/// Contains complete reflection IR after all transforms.
/// </summary>
public sealed record AssemblySnapshot(
    string AssemblyName,
    string AssemblyPath,
    string Timestamp,
    IReadOnlyList<NamespaceSnapshot> Namespaces);

/// <summary>
/// Snapshot of a single namespace within an assembly.
/// </summary>
public sealed record NamespaceSnapshot(
    string ClrName,
    IReadOnlyList<TypeSnapshot> Types,
    IReadOnlyList<DependencyRef> Imports,
    IReadOnlyList<Diagnostic> Diagnostics);

/// <summary>
/// Snapshot of a type (class, interface, enum, delegate, struct).
/// </summary>
public sealed record TypeSnapshot(
    string ClrName,
    string FullName,
    TypeKind Kind,
    bool IsStatic,
    bool IsSealed,
    bool IsAbstract,
    string Visibility,
    IReadOnlyList<GenericParameter> GenericParameters,
    TypeReference? BaseType,
    IReadOnlyList<TypeReference> Implements,
    MemberCollection Members,
    BindingInfo Binding)
{
    // Enum-specific properties
    public string? UnderlyingType { get; init; }
    public IReadOnlyList<EnumMember>? EnumMembers { get; init; }

    // Delegate-specific properties
    public IReadOnlyList<ParameterSnapshot>? DelegateParameters { get; init; }
    public TypeReference? DelegateReturnType { get; init; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TypeKind
{
    Class,
    Struct,
    Interface,
    Enum,
    Delegate,
    StaticNamespace
}

/// <summary>
/// Collection of all members grouped by kind.
/// </summary>
public sealed record MemberCollection(
    IReadOnlyList<ConstructorSnapshot> Constructors,
    IReadOnlyList<MethodSnapshot> Methods,
    IReadOnlyList<PropertySnapshot> Properties,
    IReadOnlyList<FieldSnapshot> Fields,
    IReadOnlyList<EventSnapshot> Events);

/// <summary>
/// Snapshot of a method.
/// </summary>
public sealed record MethodSnapshot(
    string ClrName,
    bool IsStatic,
    bool IsVirtual,
    bool IsOverride,
    bool IsAbstract,
    string Visibility,
    IReadOnlyList<GenericParameter> GenericParameters,
    IReadOnlyList<ParameterSnapshot> Parameters,
    TypeReference ReturnType,
    MemberBinding Binding);

/// <summary>
/// Snapshot of a property.
/// </summary>
public sealed record PropertySnapshot(
    string ClrName,
    string ClrType,
    string TsType,
    bool IsReadOnly,
    bool IsStatic,
    bool IsVirtual,
    bool IsOverride,
    string Visibility,
    MemberBinding Binding);

/// <summary>
/// Snapshot of a constructor.
/// </summary>
public sealed record ConstructorSnapshot(
    string Visibility,
    IReadOnlyList<ParameterSnapshot> Parameters);

/// <summary>
/// Snapshot of a field.
/// </summary>
public sealed record FieldSnapshot(
    string ClrName,
    string ClrType,
    string TsType,
    bool IsReadOnly,
    bool IsStatic,
    string Visibility,
    MemberBinding Binding);

/// <summary>
/// Snapshot of an event.
/// </summary>
public sealed record EventSnapshot(
    string ClrName,
    string ClrType,
    string TsType,
    bool IsStatic,
    string Visibility,
    MemberBinding Binding);

/// <summary>
/// Type reference with both CLR and TS representations.
/// </summary>
public sealed record TypeReference(
    string ClrType,
    string TsType,
    string? Assembly = null);

/// <summary>
/// Generic parameter with constraints and variance.
/// </summary>
public sealed record GenericParameter(
    string Name,
    IReadOnlyList<string> Constraints,
    Variance Variance);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum Variance
{
    None,
    In,  // Contravariant
    Out  // Covariant
}

/// <summary>
/// Method/constructor parameter.
/// </summary>
public sealed record ParameterSnapshot(
    string Name,
    string ClrType,
    string TsType,
    ParameterKind Kind,
    bool IsOptional,
    string? DefaultValue,
    bool IsParams);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ParameterKind
{
    In,
    Ref,
    Out,
    Params
}

/// <summary>
/// Enum member (name + value).
/// </summary>
public sealed record EnumMember(
    string Name,
    long Value);

/// <summary>
/// Cross-namespace dependency reference.
/// </summary>
public sealed record DependencyRef(
    string Namespace,
    string Assembly);

/// <summary>
/// Diagnostic (warning or error).
/// </summary>
public sealed record Diagnostic(
    string Code,
    DiagnosticSeverity Severity,
    string Message);

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

/// <summary>
/// Binding info for a type.
/// </summary>
public sealed record BindingInfo(
    string Assembly,
    string Type);

/// <summary>
/// Binding info for a member.
/// </summary>
public sealed record MemberBinding(
    string Assembly,
    string Type,
    string Member);

/// <summary>
/// Assembly manifest listing all processed assemblies.
/// </summary>
public sealed record AssemblyManifest(
    IReadOnlyList<AssemblyManifestEntry> Assemblies);

/// <summary>
/// Entry in assembly manifest.
/// </summary>
public sealed record AssemblyManifestEntry(
    string Name,
    string Snapshot,
    int TypeCount,
    int NamespaceCount);

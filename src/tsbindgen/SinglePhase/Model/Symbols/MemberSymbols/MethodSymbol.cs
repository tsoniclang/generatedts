using tsbindgen.Core.Renaming;
using tsbindgen.SinglePhase.Model.Types;

namespace tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;

/// <summary>
/// Represents a method member.
/// </summary>
public sealed class MethodSymbol
{
    /// <summary>
    /// Stable identifier for this method.
    /// </summary>
    public required MemberStableId StableId { get; init; }

    /// <summary>
    /// CLR method name.
    /// </summary>
    public required string ClrName { get; init; }

    /// <summary>
    /// Return type.
    /// </summary>
    public required TypeReference ReturnType { get; init; }

    /// <summary>
    /// Method parameters.
    /// </summary>
    public required IReadOnlyList<ParameterSymbol> Parameters { get; init; }

    /// <summary>
    /// Generic parameters declared by this method (for generic methods).
    /// </summary>
    public required IReadOnlyList<GenericParameterSymbol> GenericParameters { get; init; }

    /// <summary>
    /// Method arity (generic parameter count).
    /// </summary>
    public int Arity => GenericParameters.Count;

    /// <summary>
    /// True if this is a static method.
    /// </summary>
    public required bool IsStatic { get; init; }

    /// <summary>
    /// True if this is abstract.
    /// </summary>
    public bool IsAbstract { get; init; }

    /// <summary>
    /// True if this is virtual.
    /// </summary>
    public bool IsVirtual { get; init; }

    /// <summary>
    /// True if this overrides a base method.
    /// </summary>
    public bool IsOverride { get; init; }

    /// <summary>
    /// True if this is sealed (prevents further overrides).
    /// </summary>
    public bool IsSealed { get; init; }

    /// <summary>
    /// True if this hides a base member with 'new' keyword.
    /// </summary>
    public bool IsNew { get; init; }

    /// <summary>
    /// Visibility (Public, Protected, Internal, etc.).
    /// </summary>
    public required Visibility Visibility { get; init; }

    /// <summary>
    /// Provenance of this method (original, from interface, synthesized, etc.).
    /// </summary>
    public required MemberProvenance Provenance { get; init; }

    /// <summary>
    /// Where this member should be emitted (ClassSurface, StaticSurface, ViewOnly).
    /// Determined during shaping.
    /// </summary>
    public EmitScope EmitScope { get; init; } = EmitScope.ClassSurface;

    /// <summary>
    /// XML documentation comment.
    /// </summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// For interface-sourced members, the interface that contributed this member.
    /// </summary>
    public TypeReference? SourceInterface { get; init; }
}

/// <summary>
/// Represents a method or indexer parameter.
/// </summary>
public sealed class ParameterSymbol
{
    /// <summary>
    /// Parameter name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Parameter type.
    /// </summary>
    public required TypeReference Type { get; init; }

    /// <summary>
    /// True if this is a ref parameter.
    /// </summary>
    public bool IsRef { get; init; }

    /// <summary>
    /// True if this is an out parameter.
    /// </summary>
    public bool IsOut { get; init; }

    /// <summary>
    /// True if this is a params array.
    /// </summary>
    public bool IsParams { get; init; }

    /// <summary>
    /// True if this parameter has a default value.
    /// </summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>
    /// Default value (if HasDefaultValue is true).
    /// </summary>
    public object? DefaultValue { get; init; }
}

public enum Visibility
{
    Public,
    Protected,
    Internal,
    ProtectedInternal,
    PrivateProtected,
    Private
}

public enum MemberProvenance
{
    /// <summary>
    /// Original member declared in this type.
    /// </summary>
    Original,

    /// <summary>
    /// Copied from an implemented interface.
    /// </summary>
    FromInterface,

    /// <summary>
    /// Synthesized by a shaper (e.g., explicit interface implementation).
    /// </summary>
    Synthesized,

    /// <summary>
    /// Added to resolve C# 'new' hiding.
    /// </summary>
    HiddenNew,

    /// <summary>
    /// Added to include base class overload.
    /// </summary>
    BaseOverload,

    /// <summary>
    /// Added to resolve diamond inheritance.
    /// </summary>
    DiamondResolved,

    /// <summary>
    /// Normalized from indexer syntax.
    /// </summary>
    IndexerNormalized
}

public enum EmitScope
{
    /// <summary>
    /// Emit on the main class/interface surface.
    /// </summary>
    ClassSurface,

    /// <summary>
    /// Emit on the static surface (for static classes).
    /// </summary>
    StaticSurface,

    /// <summary>
    /// Only emit in explicit interface views (As_IInterface properties).
    /// </summary>
    ViewOnly
}

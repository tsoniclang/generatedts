using tsbindgen.Core.Renaming;
using tsbindgen.SinglePhase.Model.Types;

namespace tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;

/// <summary>
/// Represents an event member.
/// </summary>
public sealed class EventSymbol
{
    /// <summary>
    /// Stable identifier for this event.
    /// </summary>
    public required MemberStableId StableId { get; init; }

    /// <summary>
    /// CLR event name.
    /// </summary>
    public required string ClrName { get; init; }

    /// <summary>
    /// Event handler type (delegate type).
    /// </summary>
    public required TypeReference EventHandlerType { get; init; }

    /// <summary>
    /// True if this is a static event.
    /// </summary>
    public required bool IsStatic { get; init; }

    /// <summary>
    /// True if this is virtual.
    /// </summary>
    public bool IsVirtual { get; init; }

    /// <summary>
    /// True if this overrides a base event.
    /// </summary>
    public bool IsOverride { get; init; }

    /// <summary>
    /// Visibility.
    /// </summary>
    public required Visibility Visibility { get; init; }

    /// <summary>
    /// Provenance.
    /// </summary>
    public required MemberProvenance Provenance { get; init; }

    /// <summary>
    /// Emit scope.
    /// </summary>
    public EmitScope EmitScope { get; init; } = EmitScope.ClassSurface;

    /// <summary>
    /// Documentation.
    /// </summary>
    public string? Documentation { get; init; }

    /// <summary>
    /// Source interface (for interface-sourced events).
    /// </summary>
    public TypeReference? SourceInterface { get; init; }
}

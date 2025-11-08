using tsbindgen.Core.Renaming;

namespace tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;

/// <summary>
/// Represents a constructor.
/// </summary>
public sealed class ConstructorSymbol
{
    /// <summary>
    /// Stable identifier for this constructor.
    /// </summary>
    public required MemberStableId StableId { get; init; }

    /// <summary>
    /// Constructor parameters.
    /// </summary>
    public required IReadOnlyList<ParameterSymbol> Parameters { get; init; }

    /// <summary>
    /// True if this is a static constructor (type initializer).
    /// </summary>
    public bool IsStatic { get; init; }

    /// <summary>
    /// Visibility.
    /// </summary>
    public required Visibility Visibility { get; init; }

    /// <summary>
    /// Documentation.
    /// </summary>
    public string? Documentation { get; init; }
}

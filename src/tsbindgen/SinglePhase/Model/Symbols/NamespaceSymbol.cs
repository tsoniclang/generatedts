using tsbindgen.Core.Renaming;

namespace tsbindgen.SinglePhase.Model.Symbols;

/// <summary>
/// Represents a namespace containing types.
/// Created during aggregation phase.
/// </summary>
public sealed class NamespaceSymbol
{
    /// <summary>
    /// Namespace name (e.g., "System.Collections.Generic").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// All types in this namespace.
    /// </summary>
    public required IReadOnlyList<TypeSymbol> Types { get; init; }

    /// <summary>
    /// Stable identifier for this namespace.
    /// </summary>
    public required StableId StableId { get; init; }

    /// <summary>
    /// Assembly names contributing to this namespace.
    /// Multiple assemblies can contribute to the same namespace.
    /// </summary>
    public required IReadOnlySet<string> ContributingAssemblies { get; init; }
}

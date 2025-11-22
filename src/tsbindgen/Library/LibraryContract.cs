using System.Collections.Immutable;

namespace tsbindgen.Library;

/// <summary>
/// Represents the contract defined by an existing tsbindgen library package.
/// Loaded from metadata.json and bindings.json files.
/// Used to filter emission to only symbols present in the library.
/// </summary>
public sealed record LibraryContract
{
    /// <summary>
    /// Set of allowed type StableIds (format: "AssemblyName:ClrFullName").
    /// A type is emittable iff its StableId exists in this set.
    /// </summary>
    public required ImmutableHashSet<string> AllowedTypeStableIds { get; init; }

    /// <summary>
    /// Set of allowed member StableIds (format: "AssemblyName:DeclaringType::MemberNameSignature").
    /// A member is emittable iff its StableId exists in this set.
    /// </summary>
    public required ImmutableHashSet<string> AllowedMemberStableIds { get; init; }

    /// <summary>
    /// Set of binding StableIds from bindings.json.
    /// Used to validate that all emitted members have corresponding bindings.
    /// </summary>
    public required ImmutableHashSet<string> AllowedBindingStableIds { get; init; }

    /// <summary>
    /// Mapping from namespace name to set of type StableIds in that namespace.
    /// Used to preserve namespace structure from the library.
    /// </summary>
    public required ImmutableDictionary<string, ImmutableHashSet<string>> NamespaceToTypes { get; init; }

    /// <summary>
    /// Total number of types in the contract.
    /// </summary>
    public int TypeCount => AllowedTypeStableIds.Count;

    /// <summary>
    /// Total number of members in the contract.
    /// </summary>
    public int MemberCount => AllowedMemberStableIds.Count;

    /// <summary>
    /// Total number of namespaces in the contract.
    /// </summary>
    public int NamespaceCount => NamespaceToTypes.Count;
}

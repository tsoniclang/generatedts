using tsbindgen.Model.Symbols;
using tsbindgen.Model.Symbols.MemberSymbols;

namespace tsbindgen.Library;

/// <summary>
/// Filters symbols based on library contract.
/// Used in library mode to emit only symbols present in the library contract.
/// </summary>
public static class LibraryFilter
{
    /// <summary>
    /// Check if a type is allowed by the library contract.
    /// </summary>
    /// <param name="type">Type to check</param>
    /// <param name="contract">Library contract</param>
    /// <returns>True if type is in contract, false otherwise</returns>
    public static bool IsAllowedType(TypeSymbol type, LibraryContract contract)
    {
        var stableId = type.StableId.ToString();
        return contract.AllowedTypeStableIds.Contains(stableId);
    }

    /// <summary>
    /// Check if a method is allowed by the library contract.
    /// </summary>
    public static bool IsAllowedMethod(MethodSymbol method, LibraryContract contract)
    {
        var stableId = method.StableId.ToString();
        return contract.AllowedMemberStableIds.Contains(stableId);
    }

    /// <summary>
    /// Check if a property is allowed by the library contract.
    /// </summary>
    public static bool IsAllowedProperty(PropertySymbol property, LibraryContract contract)
    {
        var stableId = property.StableId.ToString();
        return contract.AllowedMemberStableIds.Contains(stableId);
    }

    /// <summary>
    /// Check if a field is allowed by the library contract.
    /// </summary>
    public static bool IsAllowedField(FieldSymbol field, LibraryContract contract)
    {
        var stableId = field.StableId.ToString();
        return contract.AllowedMemberStableIds.Contains(stableId);
    }

    /// <summary>
    /// Check if an event is allowed by the library contract.
    /// </summary>
    public static bool IsAllowedEvent(EventSymbol evt, LibraryContract contract)
    {
        var stableId = evt.StableId.ToString();
        return contract.AllowedMemberStableIds.Contains(stableId);
    }
}

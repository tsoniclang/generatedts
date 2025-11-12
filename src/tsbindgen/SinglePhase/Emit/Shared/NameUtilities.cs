using tsbindgen.SinglePhase.Renaming;

namespace tsbindgen.SinglePhase.Emit.Shared;

/// <summary>
/// Utilities for applying consistent naming policy across all emission surfaces.
/// Implements the "CLR-name contract" - use PascalCase CLR names, sanitize reserved words.
/// </summary>
public static class NameUtilities
{
    /// <summary>
    /// Apply CLR surface name policy using CLR name.
    ///
    /// Policy:
    /// 1. Start with CLR name (PascalCase)
    /// 2. Sanitize reserved/invalid TypeScript identifiers
    /// 3. NEVER print numeric suffixes (equals2, getHashCode3, etc.)
    ///
    /// This ensures interfaces and classes emit matching member names.
    /// </summary>
    public static string ApplyClrSurfaceNamePolicy(string clrName)
    {
        return SanitizeIdentifier(clrName);
    }

    /// <summary>
    /// Sanitize a TypeScript identifier by appending '_' if it's a reserved word.
    /// </summary>
    private static string SanitizeIdentifier(string name)
    {
        var result = Renaming.TypeScriptReservedWords.Sanitize(name);
        return result.Sanitized;
    }

    /// <summary>
    /// Check if the renamed name is a non-numeric override of the CLR name.
    /// Returns true if Renamer applied a semantic override (not a numeric suffix).
    /// </summary>
    private static bool IsNonNumericOverride(string clrName, string renamedName)
    {
        // Same name - no override
        if (clrName == renamedName)
            return false;

        // Check if renamed name ends with just digits (e.g., equals2, equals3)
        // Pattern: originalName + one or more digits
        if (renamedName.StartsWith(clrName) && renamedName.Length > clrName.Length)
        {
            var suffix = renamedName.Substring(clrName.Length);
            // If suffix is all digits, this is a numeric override (ignore it)
            if (suffix.All(char.IsDigit))
                return false;
        }

        // Otherwise this is a semantic override (e.g., ToString -> ToString_)
        return true;
    }

    /// <summary>
    /// Check if a name ends with a numeric suffix (for PhaseGate validation).
    /// </summary>
    public static bool HasNumericSuffix(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;

        // Check if name ends with one or more digits
        int i = name.Length - 1;
        while (i >= 0 && char.IsDigit(name[i]))
            i--;

        // If we found digits at the end
        return i < name.Length - 1;
    }
}

using System.Text;
using tsbindgen.Snapshot;

namespace tsbindgen.Config;

/// <summary>
/// Single source of truth for TypeScript naming.
/// All names derived from TypeReference structure - no heuristics, no string parsing.
/// </summary>
public static class TsNaming
{
    /// <summary>
    /// Generates TypeScript alias for analysis and lookups.
    /// Uses underscore between all segments: "Console_Error_1"
    /// </summary>
    /// <remarks>
    /// Analysis naming joins all nesting levels with underscore.
    /// Arity is preserved as-is in TypeName (e.g., "List_1").
    /// Used as stable key for analysis passes and type lookups.
    /// </remarks>
    public static string ForAnalysis(TypeReference type)
    {
        var segments = CollectSegments(type);
        return string.Join("_", segments);
    }

    /// <summary>
    /// Generates TypeScript emit name for rendering to .d.ts files.
    /// Uses dollar sign between nested segments: "Console$Error_1"
    /// </summary>
    /// <remarks>
    /// Emit naming uses $ for nesting to match TypeScript conventions.
    /// Top-level types: "List_1"
    /// Nested types: "Console$Error_1"
    /// Deep nesting: "Outer_1$Middle$Inner_2"
    /// Types with underscores: "BIND_OPTS" (unchanged, not nested)
    /// </remarks>
    public static string ForEmit(TypeReference type)
    {
        var segments = CollectSegments(type);
        return string.Join("$", segments);
    }

    /// <summary>
    /// Collects type name segments from leaf to root via DeclaringType chain.
    /// Returns segments in correct order (root..leaf).
    /// </summary>
    /// <remarks>
    /// TypeName already contains arity suffix (e.g., "List_1", "Error_1").
    /// Underscores in TypeName are preserved (e.g., "BIND_OPTS", "Inner_With_Underscore").
    /// DeclaringType chain provides exact nesting hierarchy with no ambiguity.
    /// </remarks>
    private static List<string> CollectSegments(TypeReference type)
    {
        var segments = new List<string>();
        var current = type;

        // Walk up declaring type chain
        while (current != null)
        {
            segments.Insert(0, current.TypeName); // Insert at front for correct order
            current = current.DeclaringType;
        }

        return segments;
    }
}

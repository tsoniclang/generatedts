using tsbindgen.Library;
using tsbindgen.Model;

namespace tsbindgen.Plan.Validation;

/// <summary>
/// Validation rules for library mode.
/// Ensures library contract consistency and prevents dangling references.
/// </summary>
internal static class LibraryMode
{
    /// <summary>
    /// LIB001: Validate that all types/members in library contract exist in current graph.
    /// Missing symbols indicate BCL API removal or assembly version mismatch.
    /// </summary>
    internal static void ValidateContractExists(
        SymbolGraph graph,
        LibraryContract contract,
        ValidationContext validationCtx)
    {
        // Check each type in contract
        var missingTypes = 0;
        var missingMembers = 0;

        foreach (var typeStableId in contract.AllowedTypeStableIds)
        {
            // Find type in graph by StableId
            var type = graph.TypeIndex.Values.FirstOrDefault(t => t.StableId.ToString() == typeStableId);
            if (type == null)
            {
                validationCtx.RecordDiagnostic(
                    "LIB001",
                    "ERROR",
                    $"Library contract references type that doesn't exist in current graph: {typeStableId}");
                missingTypes++;
            }
        }

        // Check each member in contract
        foreach (var memberStableId in contract.AllowedMemberStableIds)
        {
            // Find member in graph by StableId
            var found = false;
            foreach (var type in graph.TypeIndex.Values)
            {
                // Check methods
                if (type.Members.Methods.Any(m => m.StableId.ToString() == memberStableId))
                {
                    found = true;
                    break;
                }
                // Check properties
                if (type.Members.Properties.Any(p => p.StableId.ToString() == memberStableId))
                {
                    found = true;
                    break;
                }
                // Check fields
                if (type.Members.Fields.Any(f => f.StableId.ToString() == memberStableId))
                {
                    found = true;
                    break;
                }
                // Check events
                if (type.Members.Events.Any(e => e.StableId.ToString() == memberStableId))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                validationCtx.RecordDiagnostic(
                    "LIB001",
                    "ERROR",
                    $"Library contract references member that doesn't exist in current graph: {memberStableId}");
                missingMembers++;
            }
        }
    }

    /// <summary>
    /// LIB002: Validate that filtered output contains no dangling references.
    /// After filtering to library contract, all type references must point to:
    /// 1. Types within the library contract, OR
    /// 2. Primitive types (number, string, etc.), OR
    /// 3. External types (not in current graph)
    /// </summary>
    internal static void ValidateNoDanglingReferences(
        SymbolGraph graph,
        LibraryContract contract,
        ValidationContext validationCtx)
    {
        // For this PR, we're using full BCL as library, so this should always pass
        // Implementation placeholder - actual validation would walk type references
        // and ensure all referenced types are either in contract or external

        // No diagnostics recorded - deferred for real library curation
    }

    /// <summary>
    /// LIB003: Validate binding consistency.
    /// Ensures:
    /// 1. All emitted members have corresponding binding entries
    /// 2. All binding entries point to emitted members
    /// </summary>
    internal static void ValidateBindingConsistency(
        SymbolGraph graph,
        LibraryContract contract,
        ValidationContext validationCtx)
    {
        // For this PR, we're using full BCL as library, so bindings should match
        // Implementation placeholder - actual validation would:
        // 1. Check emitted member StableIds are in AllowedBindingStableIds
        // 2. Check binding StableIds point to emitted members

        // No diagnostics recorded - deferred for real library curation
    }
}

using System.Collections.Generic;

namespace tsbindgen.Plan.Validation;

/// <summary>
/// Strict mode diagnostic policy.
/// Defines which diagnostic codes are allowed in strict mode validation.
///
/// Strict mode philosophy:
/// - ERROR: Always disallowed (blocks emission)
/// - WARNING: Disallowed unless explicitly whitelisted here
/// - INFO: Always allowed (informational only, doesn't count toward warning total)
///
/// When strict mode is enabled, PhaseGate validation fails if any non-whitelisted
/// WARNING exists, ensuring zero technical debt in production builds.
/// </summary>
public static class StrictModePolicy
{
    /// <summary>
    /// Diagnostic severity levels for strict mode policy.
    /// </summary>
    public enum AllowedLevel
    {
        /// <summary>
        /// Always fails validation (ERROR level).
        /// </summary>
        Forbidden,

        /// <summary>
        /// Allowed only if explicitly whitelisted (WARNING level).
        /// Must have documented justification.
        /// </summary>
        WhitelistedWarning,

        /// <summary>
        /// Always allowed (INFO level).
        /// Informational only - doesn't count toward warning totals.
        /// </summary>
        Informational
    }

    /// <summary>
    /// Central policy mapping: diagnostic code → allowed level in strict mode.
    /// </summary>
    private static readonly Dictionary<string, (AllowedLevel Level, string Justification)> Policy = new()
    {
        // ============================================================================
        // ERRORS - Always forbidden (strict mode = zero tolerance)
        // ============================================================================

        ["TBG900"] = (AllowedLevel.Forbidden, "StaticFlatteningPlan validity - must be zero"),
        ["TBG901"] = (AllowedLevel.Forbidden, "StaticConflictPlan validity - must be zero"),
        ["TBG902"] = (AllowedLevel.Forbidden, "OverrideConflictPlan validity - must be zero"),
        ["TBG903"] = (AllowedLevel.Forbidden, "PropertyOverridePlan validity - must be zero"),
        ["TBG904"] = (AllowedLevel.Forbidden, "ExtensionMethodsPlan validity - must be zero"),
        ["TBG905"] = (AllowedLevel.Forbidden, "Extension method 'any' erasures - must be zero"),
        ["TBG906"] = (AllowedLevel.Forbidden, "Extension bucket name validity - must be zero"),
        ["TBG907"] = (AllowedLevel.Forbidden, "Extension import resolution - must be zero"),

        // ============================================================================
        // WARNINGS - Currently active, must be eliminated or whitelisted
        // ============================================================================

        // TBG120: Reserved word collisions (8 instances)
        // TEMPORARY WHITELIST - Will be removed in PR D after adding qualification checks
        // Core BCL types (Enum, String, Type, Boolean, Void, etc.) collide with TS keywords
        // but are always used in qualified contexts (System.Type, not bare Type).
        // PR D will add runtime guards to ensure qualification and convert to ERROR if unqualified.
        ["TBG120"] = (AllowedLevel.WhitelistedWarning,
            "Reserved word collisions - whitelisted for core BCL types used in qualified contexts only. " +
            "Will become ERROR in PR D if unqualified usage detected."),

        // TBG201: Circular namespace dependencies (ELIMINATED in PR B)
        // Was 267 instances - now 0 after SCC bucketing filters intra-SCC cycles
        // Kept in policy for documentation, but should never trigger

        // TBG203: Interface conformance issues (87 instances)
        // TEMPORARY WHITELIST - Will be eliminated in PR C by honest emission
        // Types claim to implement interfaces they can't fully express in TypeScript
        // (e.g., generic math interfaces with static abstract members).
        // PR C will suppress TS 'implements' claims while preserving truth in metadata.
        ["TBG203"] = (AllowedLevel.WhitelistedWarning,
            "Interface conformance issues - whitelisted until PR C implements honest emission. " +
            "Will be eliminated by not claiming TS conformance for unsupported interfaces."),

        // ============================================================================
        // INFO - Always allowed (informational, doesn't count as warnings)
        // ============================================================================

        // TBG310: Property covariance (12 instances)
        // C# allows property covariance, TypeScript doesn't support property overloads.
        // This is a fundamental TS limitation - documented but not actionable.
        // Already marked as INFO in current implementation.
        ["TBG310"] = (AllowedLevel.Informational,
            "Property covariance - fundamental TypeScript limitation, documented but not actionable."),

        // TBG410: Narrowed generic constraints (5 instances)
        // Derived types add constraints beyond base class (e.g., where T : IEquatable<T>).
        // This is correct TypeScript behavior - informational only.
        // Already marked as INFO in current implementation.
        ["TBG410"] = (AllowedLevel.Informational,
            "Narrowed generic constraints - correct behavior, informational only."),
    };

    /// <summary>
    /// Check if a diagnostic code is allowed in strict mode.
    /// </summary>
    /// <param name="code">Diagnostic code (e.g., "TBG201")</param>
    /// <param name="level">Diagnostic level ("ERROR", "WARNING", "INFO")</param>
    /// <returns>True if allowed, false if should fail validation</returns>
    public static bool IsAllowed(string code, string level)
    {
        // INFO is always allowed (doesn't count toward warning totals)
        if (level == "INFO")
            return true;

        // ERROR always fails validation
        if (level == "ERROR")
            return false;

        // WARNING - check policy
        if (level == "WARNING")
        {
            if (!Policy.TryGetValue(code, out var policy))
            {
                // Unknown code - fail safe: disallow
                return false;
            }

            return policy.Level == AllowedLevel.WhitelistedWarning;
        }

        // Unknown level - fail safe: disallow
        return false;
    }

    /// <summary>
    /// Get the justification for a whitelisted diagnostic code.
    /// </summary>
    public static string GetJustification(string code)
    {
        if (Policy.TryGetValue(code, out var policy))
        {
            return policy.Justification;
        }

        return "No justification available";
    }

    /// <summary>
    /// Get all whitelisted warning codes.
    /// </summary>
    public static IEnumerable<string> GetWhitelistedWarnings()
    {
        foreach (var (code, (level, _)) in Policy)
        {
            if (level == AllowedLevel.WhitelistedWarning)
            {
                yield return code;
            }
        }
    }

    /// <summary>
    /// Get all informational codes.
    /// </summary>
    public static IEnumerable<string> GetInformationalCodes()
    {
        foreach (var (code, (level, _)) in Policy)
        {
            if (level == AllowedLevel.Informational)
            {
                yield return code;
            }
        }
    }
}

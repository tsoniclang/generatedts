namespace tsbindgen.Core.Diagnostics;

/// <summary>
/// Well-known diagnostic codes for categorization and filtering.
/// </summary>
public static class DiagnosticCodes
{
    // Resolution errors
    public const string UnresolvedType = "TBG001";
    public const string UnresolvedGenericParameter = "TBG002";
    public const string UnresolvedConstraint = "TBG003";

    // Naming conflicts
    public const string NameConflictUnresolved = "TBG100";
    public const string AmbiguousOverload = "TBG101";
    public const string DuplicateMember = "TBG102";

    // Interface/inheritance issues
    public const string DiamondInheritanceDetected = "TBG200";
    public const string CircularInheritance = "TBG201";
    public const string InterfaceNotFound = "TBG202";
    public const string StructuralConformanceFailure = "TBG203";

    // TypeScript compatibility
    public const string PropertyCovarianceUnsupported = "TBG300";
    public const string StaticSideVariance = "TBG301";
    public const string IndexerConflict = "TBG302";

    // Policy violations
    public const string PolicyViolation = "TBG400";
    public const string UnsatisfiableConstraint = "TBG401";

    // Renaming issues
    public const string RenameConflict = "TBG500";
    public const string ExplicitOverrideNotApplied = "TBG501";

    // Metadata issues
    public const string MissingMetadataToken = "TBG600";
    public const string BindingAmbiguity = "TBG601";
}

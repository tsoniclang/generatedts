using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Types;

namespace tsbindgen.SinglePhase.Renaming;

/// <summary>
/// Centralized scope construction for SymbolRenamer.
/// NO MANUAL SCOPE STRINGS - all scopes must be created through these helpers.
///
/// CANONICAL SCOPE FORMATS (authoritative - do not deviate):
/// - Namespace (public):  ns:{NamespaceName}:public
/// - Namespace (internal): ns:{NamespaceName}:internal
/// - Class members:       type:{TypeFullName}#{instance|static}
/// - View members:        view:{TypeStableId}:{InterfaceStableId}#{instance|static}
///
/// USAGE PATTERN:
/// - Reservations: Use BASE scopes (no #instance/#static suffix) - ReserveMemberName adds it
/// - Lookups:      Use SURFACE scopes (with #instance/#static suffix)
///
/// M5 CRITICAL: View members MUST be looked up with ViewSurface(), not ClassSurface().
/// </summary>
public static class ScopeFactory
{
    // ============================================================================
    // NAMESPACE SCOPES (full only - no base/full distinction)
    // ============================================================================

    /// <summary>
    /// Creates namespace scope for public type names.
    /// Format: "ns:{Namespace}:public"
    /// </summary>
    public static NamespaceScope NamespacePublic(string ns)
    {
        return new NamespaceScope
        {
            Namespace = ns,
            IsInternal = false,
            ScopeKey = $"ns:{ns}:public"
        };
    }

    /// <summary>
    /// Creates namespace scope for internal type names.
    /// Format: "ns:{Namespace}:internal"
    ///
    /// Use for: Current internal artifacts (all namespace emissions)
    /// </summary>
    public static NamespaceScope NamespaceInternal(string ns)
    {
        return new NamespaceScope
        {
            Namespace = ns,
            IsInternal = true,
            ScopeKey = $"ns:{ns}:internal"
        };
    }

    // ============================================================================
    // CLASS SCOPES (base for reservations, full for lookups)
    // ============================================================================

    /// <summary>
    /// Creates BASE class scope for member reservations (no side suffix).
    /// Format: "type:{TypeFullName}" (ReserveMemberName will add #instance/#static)
    ///
    /// Use for: ReserveMemberName calls
    /// </summary>
    public static TypeScope ClassBase(TypeSymbol type)
    {
        return new TypeScope
        {
            TypeFullName = type.ClrFullName,
            IsStatic = false,
            ScopeKey = $"type:{type.ClrFullName}"
        };
    }

    /// <summary>
    /// Creates FULL class scope for instance member lookups.
    /// Format: "type:{TypeFullName}#instance"
    ///
    /// Use for: GetFinalMemberName, TryGetDecision calls for instance members
    /// </summary>
    public static TypeScope ClassInstance(TypeSymbol type)
    {
        return new TypeScope
        {
            TypeFullName = type.ClrFullName,
            IsStatic = false,
            ScopeKey = $"type:{type.ClrFullName}#instance"
        };
    }

    /// <summary>
    /// Creates FULL class scope for static member lookups.
    /// Format: "type:{TypeFullName}#static"
    ///
    /// Use for: GetFinalMemberName, TryGetDecision calls for static members
    /// </summary>
    public static TypeScope ClassStatic(TypeSymbol type)
    {
        return new TypeScope
        {
            TypeFullName = type.ClrFullName,
            IsStatic = true,
            ScopeKey = $"type:{type.ClrFullName}#static"
        };
    }

    /// <summary>
    /// Creates FULL class scope based on member's isStatic flag.
    /// Format: "type:{TypeFullName}#instance" or "#static"
    ///
    /// Use for: GetFinalMemberName, TryGetDecision calls when isStatic is dynamic
    /// Preferred over manual ternary: cleaner call-sites
    /// </summary>
    public static TypeScope ClassSurface(TypeSymbol type, bool isStatic)
    {
        return isStatic ? ClassStatic(type) : ClassInstance(type);
    }

    // ============================================================================
    // VIEW SCOPES (base for reservations, full for lookups)
    // ============================================================================

    /// <summary>
    /// Creates BASE view scope for member reservations (no side suffix).
    /// Format: "view:{TypeStableId}:{InterfaceStableId}" (ReserveMemberName will add #instance/#static)
    ///
    /// Use for: ReserveMemberName calls for ViewOnly members
    /// </summary>
    public static TypeScope ViewBase(TypeSymbol type, string interfaceStableId)
    {
        return new TypeScope
        {
            TypeFullName = type.ClrFullName,
            IsStatic = false,
            ScopeKey = $"view:{type.StableId}:{interfaceStableId}"
        };
    }

    /// <summary>
    /// Creates FULL view scope for explicit interface view member lookups.
    /// Format: "view:{TypeStableId}:{InterfaceStableId}#instance" or "#static"
    ///
    /// Use for: GetFinalMemberName, TryGetDecision calls for ViewOnly members
    ///
    /// M5 FIX: This is what emitters were missing - they were using ClassInstance()/ClassStatic()
    /// for view members, causing PG_NAME_004 collisions.
    /// </summary>
    public static TypeScope ViewSurface(TypeSymbol type, string interfaceStableId, bool isStatic)
    {
        return new TypeScope
        {
            TypeFullName = type.ClrFullName,
            IsStatic = isStatic,
            ScopeKey = $"view:{type.StableId}:{interfaceStableId}#{(isStatic ? "static" : "instance")}"
        };
    }

    // ============================================================================
    // UTILITIES
    // ============================================================================

    /// <summary>
    /// Extracts interface StableId from TypeReference (same logic as ViewPlanner).
    /// Returns assembly-qualified identifier for grouping/merging.
    /// </summary>
    public static string GetInterfaceStableId(TypeReference ifaceRef)
    {
        return ifaceRef switch
        {
            NamedTypeReference named => $"{named.AssemblyName}:{named.FullName}",
            NestedTypeReference nested => GetInterfaceStableId(nested.DeclaringType) + "+" + nested.NestedName,
            _ => ifaceRef.ToString() ?? "unknown"
        };
    }
}

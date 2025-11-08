using System.Linq;
using tsbindgen.Core.Renaming;
using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;

namespace tsbindgen.SinglePhase.Normalize;

/// <summary>
/// Reserves all TypeScript names through the central Renamer.
/// Runs after Shape phase, before Plan phase.
/// Computes proper base names (sanitizes `+` → `_`, `` ` `` → `_`),
/// reserves through Renamer, and sets TsEmitName for PhaseGate validation.
/// </summary>
public static class NameReservation
{
    /// <summary>
    /// Reserve all type and member names in the symbol graph.
    /// This is the ONLY place where names are reserved - all other components
    /// must use Renamer.GetFinal*() to retrieve names.
    /// </summary>
    public static void ReserveAllNames(BuildContext ctx, SymbolGraph graph)
    {
        ctx.Log("NameReservation: Reserving all TypeScript names...");

        int typesReserved = 0;
        int membersReserved = 0;
        int skippedCompilerGenerated = 0;

        // Process namespaces in deterministic order
        foreach (var ns in graph.Namespaces.OrderBy(n => n.Name))
        {
            // Create namespace scope
            var nsScope = new NamespaceScope
            {
                ScopeKey = $"ns:{ns.Name}",
                Namespace = ns.Name,
                IsInternal = true // Internal scope (facade scope handled separately)
            };

            // Process types in deterministic order
            foreach (var type in ns.Types.OrderBy(t => t.ClrFullName))
            {
                // Skip compiler-generated types
                if (IsCompilerGenerated(type.ClrName))
                {
                    ctx.Log($"NameReservation: Skipping compiler-generated type {type.ClrFullName}");
                    skippedCompilerGenerated++;
                    continue;
                }

                // Reserve type name
                ReserveTypeName(ctx, type, nsScope);
                typesReserved++;

                // Reserve all member names
                membersReserved += ReserveMemberNames(ctx, type);
            }
        }

        ctx.Log($"NameReservation: Reserved {typesReserved} type names, {membersReserved} member names");
        if (skippedCompilerGenerated > 0)
        {
            ctx.Log($"NameReservation: Skipped {skippedCompilerGenerated} compiler-generated types");
        }
    }

    private static void ReserveTypeName(BuildContext ctx, TypeSymbol type, NamespaceScope nsScope)
    {
        // Compute requested base name with sanitization
        var requested = ComputeTypeRequestedBase(type.ClrName);

        ctx.Renamer.ReserveTypeName(
            stableId: type.StableId,
            requested: requested,
            scope: nsScope,
            reason: "TypeDeclaration",
            decisionSource: "NameReservation");

        // Set TsEmitName for PhaseGate validation and convenience
        type.TsEmitName = ctx.Renamer.GetFinalTypeName(type.StableId, nsScope);
    }

    private static int ReserveMemberNames(BuildContext ctx, TypeSymbol type)
    {
        // Create type scope (base - will be modified per member for static/instance)
        var typeScope = new TypeScope
        {
            ScopeKey = $"type:{type.ClrFullName}",
            TypeFullName = type.ClrFullName,
            IsStatic = false // Will be overridden for static members
        };

        int count = 0;

        // Reserve methods in deterministic order
        foreach (var method in type.Members.Methods.OrderBy(m => m.ClrName))
        {
            ReserveMethodName(ctx, method, typeScope);
            count++;
        }

        // Reserve properties in deterministic order
        foreach (var property in type.Members.Properties.OrderBy(p => p.ClrName))
        {
            ReservePropertyName(ctx, property, typeScope);
            count++;
        }

        // Reserve fields in deterministic order
        foreach (var field in type.Members.Fields.OrderBy(f => f.ClrName))
        {
            ReserveFieldName(ctx, field, typeScope);
            count++;
        }

        // Reserve events in deterministic order
        foreach (var ev in type.Members.Events.OrderBy(e => e.ClrName))
        {
            ReserveEventName(ctx, ev, typeScope);
            count++;
        }

        // Reserve constructors
        foreach (var ctor in type.Members.Constructors)
        {
            ReserveConstructorName(ctx, ctor, typeScope);
            count++;
        }

        return count;
    }

    private static void ReserveMethodName(BuildContext ctx, MethodSymbol method, TypeScope typeScope)
    {
        // Determine reason based on provenance
        var reason = method.Provenance switch
        {
            MemberProvenance.Original => "MethodDeclaration",
            MemberProvenance.FromInterface => "InterfaceMember",
            MemberProvenance.Synthesized => "SynthesizedMember",
            MemberProvenance.HiddenNew => "HiddenNewMember",
            MemberProvenance.BaseOverload => "BaseOverload",
            _ => "Unknown"
        };

        // Compute requested base (handle operators)
        var requested = ComputeMethodBase(method);

        ctx.Renamer.ReserveMemberName(
            stableId: method.StableId,
            requested: requested,
            scope: typeScope,
            reason: reason,
            isStatic: method.IsStatic,
            decisionSource: "NameReservation");

        // Set TsEmitName for PhaseGate validation
        method.TsEmitName = ctx.Renamer.GetFinalMemberName(method.StableId, typeScope, method.IsStatic);
    }

    private static void ReservePropertyName(BuildContext ctx, PropertySymbol property, TypeScope typeScope)
    {
        var reason = property.Provenance switch
        {
            MemberProvenance.Original => property.IsIndexer ? "IndexerProperty" : "PropertyDeclaration",
            MemberProvenance.FromInterface => "InterfaceProperty",
            MemberProvenance.Synthesized => "SynthesizedProperty",
            _ => "Unknown"
        };

        var requested = SanitizeMemberName(property.ClrName);

        ctx.Renamer.ReserveMemberName(
            stableId: property.StableId,
            requested: requested,
            scope: typeScope,
            reason: reason,
            isStatic: property.IsStatic,
            decisionSource: "NameReservation");

        // Set TsEmitName for PhaseGate validation
        property.TsEmitName = ctx.Renamer.GetFinalMemberName(property.StableId, typeScope, property.IsStatic);
    }

    private static void ReserveFieldName(BuildContext ctx, FieldSymbol field, TypeScope typeScope)
    {
        var reason = field.IsConst ? "ConstantField" : "FieldDeclaration";
        var requested = SanitizeMemberName(field.ClrName);

        ctx.Renamer.ReserveMemberName(
            stableId: field.StableId,
            requested: requested,
            scope: typeScope,
            reason: reason,
            isStatic: field.IsStatic,
            decisionSource: "NameReservation");

        // Set TsEmitName for PhaseGate validation
        field.TsEmitName = ctx.Renamer.GetFinalMemberName(field.StableId, typeScope, field.IsStatic);
    }

    private static void ReserveEventName(BuildContext ctx, EventSymbol ev, TypeScope typeScope)
    {
        var requested = SanitizeMemberName(ev.ClrName);

        ctx.Renamer.ReserveMemberName(
            stableId: ev.StableId,
            requested: requested,
            scope: typeScope,
            reason: "EventDeclaration",
            isStatic: ev.IsStatic,
            decisionSource: "NameReservation");

        // Set TsEmitName for PhaseGate validation
        ev.TsEmitName = ctx.Renamer.GetFinalMemberName(ev.StableId, typeScope, ev.IsStatic);
    }

    private static void ReserveConstructorName(BuildContext ctx, ConstructorSymbol ctor, TypeScope typeScope)
    {
        ctx.Renamer.ReserveMemberName(
            stableId: ctor.StableId,
            requested: "constructor", // TypeScript always uses "constructor"
            scope: typeScope,
            reason: "ConstructorDeclaration",
            isStatic: ctor.IsStatic, // static constructors exist
            decisionSource: "NameReservation");

        // Constructors don't have TsEmitName (they're always "constructor" in TypeScript)
    }

    /// <summary>
    /// Compute the requested base name for a type.
    /// Applies syntax transforms only (nested `+` → `_`, generic arity, etc.)
    /// Does NOT apply style/casing - Renamer handles that.
    /// </summary>
    private static string ComputeTypeRequestedBase(string clrName)
    {
        var baseName = clrName;

        // Handle nested types: Outer+Inner → Outer_Inner
        baseName = baseName.Replace('+', '_');

        // Handle generic arity: List`1 → List_1
        baseName = baseName.Replace('`', '_');

        // Remove other invalid TS identifier characters
        baseName = baseName.Replace('<', '_');
        baseName = baseName.Replace('>', '_');
        baseName = baseName.Replace('[', '_');
        baseName = baseName.Replace(']', '_');

        return baseName;
    }

    private static string ComputeMethodBase(MethodSymbol method)
    {
        var name = method.ClrName;

        // Handle operators (map to policy-defined names)
        if (name.StartsWith("op_"))
        {
            return name switch
            {
                "op_Equality" => "equals",
                "op_Inequality" => "notEquals",
                "op_Addition" => "add",
                "op_Subtraction" => "subtract",
                "op_Multiply" => "multiply",
                "op_Division" => "divide",
                "op_Modulus" => "modulus",
                "op_BitwiseAnd" => "bitwiseAnd",
                "op_BitwiseOr" => "bitwiseOr",
                "op_ExclusiveOr" => "bitwiseXor",
                "op_LeftShift" => "leftShift",
                "op_RightShift" => "rightShift",
                "op_UnaryNegation" => "negate",
                "op_UnaryPlus" => "plus",
                "op_LogicalNot" => "not",
                "op_OnesComplement" => "complement",
                "op_Increment" => "increment",
                "op_Decrement" => "decrement",
                "op_True" => "isTrue",
                "op_False" => "isFalse",
                "op_GreaterThan" => "greaterThan",
                "op_LessThan" => "lessThan",
                "op_GreaterThanOrEqual" => "greaterThanOrEqual",
                "op_LessThanOrEqual" => "lessThanOrEqual",
                _ => name.Replace("op_", "operator_")
            };
        }

        // Accessors (get_, set_, add_, remove_) and regular methods use CLR name
        return SanitizeMemberName(name);
    }

    private static string SanitizeMemberName(string name)
    {
        // Remove invalid TS identifier characters
        var sanitized = name.Replace('<', '_');
        sanitized = sanitized.Replace('>', '_');
        sanitized = sanitized.Replace('[', '_');
        sanitized = sanitized.Replace(']', '_');
        sanitized = sanitized.Replace('+', '_');

        return sanitized;
    }

    /// <summary>
    /// Check if a type name indicates compiler-generated code.
    /// Common patterns: <Name>e__FixedBuffer, <>c__DisplayClass, etc.
    /// </summary>
    private static bool IsCompilerGenerated(string clrName)
    {
        return clrName.Contains('<') && (
            clrName.Contains(">e__") ||
            clrName.Contains(">c__") ||
            clrName.Contains(">d__") ||
            clrName.Contains(">f__"));
    }
}

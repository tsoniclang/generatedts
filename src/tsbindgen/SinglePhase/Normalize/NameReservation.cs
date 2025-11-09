using System.Collections.Immutable;
using System.Linq;
using tsbindgen.Core;
using tsbindgen.Core.Renaming;
using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;

namespace tsbindgen.SinglePhase.Normalize;

/// <summary>
/// Reserves all TypeScript names through the central Renamer.
/// Runs after Shape phase, before Plan phase (Phase 3.5).
///
/// Process:
/// 1. Applies syntax transforms (`+` → `_`, `` ` `` → `_`, etc.)
/// 2. Applies reserved word sanitization (adds `_` suffix)
/// 3. Reserves names through Renamer
/// 4. Sets TsEmitName on symbols for PhaseGate validation
///
/// Skips members that already have rename decisions from earlier passes
/// (e.g., HiddenMemberPlanner, IndexerPlanner).
/// </summary>
public static class NameReservation
{
    /// <summary>
    /// Reserve all type and member names in the symbol graph.
    /// This is the ONLY place where names are reserved - all other components
    /// must use Renamer.GetFinal*() to retrieve names.
    /// PURE - reserves names in Renamer and returns new graph with TsEmitName set.
    /// </summary>
    public static SymbolGraph ReserveAllNames(BuildContext ctx, SymbolGraph graph)
    {
        ctx.Log("NameReservation", "Reserving all TypeScript names...");

        int typesReserved = 0;
        int membersReserved = 0;
        int membersSkippedAlreadyRenamed = 0;
        int skippedCompilerGenerated = 0;

        // Phase 1: Reserve all names in Renamer (populates internal dictionaries)
        foreach (var ns in graph.Namespaces.OrderBy(n => n.Name))
        {
            var nsScope = new NamespaceScope
            {
                ScopeKey = $"ns:{ns.Name}",
                Namespace = ns.Name,
                IsInternal = true
            };

            foreach (var type in ns.Types.OrderBy(t => t.ClrFullName))
            {
                if (IsCompilerGenerated(type.ClrName))
                {
                    ctx.Log("NameReservation", $"Skipping compiler-generated type {type.ClrFullName}");
                    skippedCompilerGenerated++;
                    continue;
                }

                // Reserve in Renamer only (don't mutate)
                var requested = ComputeTypeRequestedBase(type.ClrName);
                ctx.Renamer.ReserveTypeName(type.StableId, requested, nsScope, "TypeDeclaration", "NameReservation");
                typesReserved++;

                // Reserve member names
                var (reserved, skipped) = ReserveMemberNamesOnly(ctx, type);
                membersReserved += reserved;
                membersSkippedAlreadyRenamed += skipped;
            }
        }

        ctx.Log("NameReservation", $"Reserved {typesReserved} type names, {membersReserved} member names");
        if (membersSkippedAlreadyRenamed > 0)
        {
            ctx.Log("NameReservation", $"Skipped {membersSkippedAlreadyRenamed} members (already renamed by earlier passes)");
        }
        if (skippedCompilerGenerated > 0)
        {
            ctx.Log("NameReservation", $"Skipped {skippedCompilerGenerated} compiler-generated types");
        }

        // Phase 2: Apply names to graph (pure transformation)
        var updatedGraph = ApplyNamesToGraph(ctx, graph);
        return updatedGraph;
    }

    /// <summary>
    /// Compute the requested base name for a type.
    /// Applies syntax transforms (nested `+` → `_`, generic arity, etc.)
    /// then sanitizes reserved words (adds `_` suffix).
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

        // Apply reserved word sanitization
        var sanitized = TypeScriptReservedWords.Sanitize(baseName);
        return sanitized.Sanitized;
    }

    private static string ComputeMethodBase(MethodSymbol method)
    {
        var name = method.ClrName;

        // Handle operators (map to policy-defined names)
        if (name.StartsWith("op_"))
        {
            var mapped = name switch
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

            // Apply reserved word sanitization to operator names
            var sanitized = TypeScriptReservedWords.Sanitize(mapped);
            return sanitized.Sanitized;
        }

        // Accessors (get_, set_, add_, remove_) and regular methods use CLR name
        return SanitizeMemberName(name);
    }

    private static string SanitizeMemberName(string name)
    {
        // Remove invalid TS identifier characters
        var cleaned = name.Replace('<', '_');
        cleaned = cleaned.Replace('>', '_');
        cleaned = cleaned.Replace('[', '_');
        cleaned = cleaned.Replace(']', '_');
        cleaned = cleaned.Replace('+', '_');

        // Apply reserved word sanitization
        var sanitized = TypeScriptReservedWords.Sanitize(cleaned);
        return sanitized.Sanitized;
    }

    /// <summary>
    /// Check if a type name indicates compiler-generated code.
    /// Compiler-generated types have unspeakable names containing < or >
    /// Examples: "<Module>", "<PrivateImplementationDetails>", "<Name>e__FixedBuffer", "<>c__DisplayClass"
    /// </summary>
    private static bool IsCompilerGenerated(string clrName)
    {
        return clrName.Contains('<') || clrName.Contains('>');
    }

    /// <summary>
    /// Reserve member names without mutating symbols (Phase 1).
    /// Returns (Reserved, Skipped) counts.
    /// Skips members that already have rename decisions from earlier passes.
    /// </summary>
    private static (int Reserved, int Skipped) ReserveMemberNamesOnly(BuildContext ctx, TypeSymbol type)
    {
        var typeScope = new TypeScope
        {
            ScopeKey = $"type:{type.ClrFullName}",
            TypeFullName = type.ClrFullName,
            IsStatic = false
        };

        int reserved = 0;
        int skipped = 0;

        foreach (var method in type.Members.Methods.OrderBy(m => m.ClrName))
        {
            // Check if already renamed by earlier pass (e.g., HiddenMemberPlanner, IndexerPlanner)
            if (ctx.Renamer.TryGetDecision(method.StableId, out var existingDecision))
            {
                skipped++;
                continue;
            }

            var reason = method.Provenance switch
            {
                MemberProvenance.Original => "MethodDeclaration",
                MemberProvenance.FromInterface => "InterfaceMember",
                MemberProvenance.Synthesized => "SynthesizedMember",
                _ => "Unknown"
            };

            var requested = ComputeMethodBase(method);
            ctx.Renamer.ReserveMemberName(method.StableId, requested, typeScope, reason, method.IsStatic, "NameReservation");
            reserved++;
        }

        foreach (var property in type.Members.Properties.OrderBy(p => p.ClrName))
        {
            // Check if already renamed (e.g., IndexerPlanner)
            if (ctx.Renamer.TryGetDecision(property.StableId, out var existingDecision))
            {
                skipped++;
                continue;
            }

            var reason = property.IsIndexer ? "IndexerProperty" : "PropertyDeclaration";
            var requested = SanitizeMemberName(property.ClrName);
            ctx.Renamer.ReserveMemberName(property.StableId, requested, typeScope, reason, property.IsStatic, "NameReservation");
            reserved++;
        }

        foreach (var field in type.Members.Fields.OrderBy(f => f.ClrName))
        {
            // Check if already renamed
            if (ctx.Renamer.TryGetDecision(field.StableId, out var existingDecision))
            {
                skipped++;
                continue;
            }

            var reason = field.IsConst ? "ConstantField" : "FieldDeclaration";
            var requested = SanitizeMemberName(field.ClrName);
            ctx.Renamer.ReserveMemberName(field.StableId, requested, typeScope, reason, field.IsStatic, "NameReservation");
            reserved++;
        }

        foreach (var ev in type.Members.Events.OrderBy(e => e.ClrName))
        {
            // Check if already renamed
            if (ctx.Renamer.TryGetDecision(ev.StableId, out var existingDecision))
            {
                skipped++;
                continue;
            }

            var requested = SanitizeMemberName(ev.ClrName);
            ctx.Renamer.ReserveMemberName(ev.StableId, requested, typeScope, reason: "EventDeclaration", ev.IsStatic, "NameReservation");
            reserved++;
        }

        foreach (var ctor in type.Members.Constructors)
        {
            // Check if already renamed
            if (ctx.Renamer.TryGetDecision(ctor.StableId, out var existingDecision))
            {
                skipped++;
                continue;
            }

            ctx.Renamer.ReserveMemberName(ctor.StableId, "constructor", typeScope, "ConstructorDeclaration", ctor.IsStatic, "NameReservation");
            reserved++;
        }

        return (reserved, skipped);
    }

    /// <summary>
    /// Apply reserved names to graph (Phase 2 - pure transformation).
    /// </summary>
    private static SymbolGraph ApplyNamesToGraph(BuildContext ctx, SymbolGraph graph)
    {
        var updatedNamespaces = graph.Namespaces.Select(ns => ApplyNamesToNamespace(ctx, ns)).ToImmutableArray();
        return graph with { Namespaces = updatedNamespaces };
    }

    private static NamespaceSymbol ApplyNamesToNamespace(BuildContext ctx, NamespaceSymbol ns)
    {
        var nsScope = new NamespaceScope
        {
            ScopeKey = $"ns:{ns.Name}",
            Namespace = ns.Name,
            IsInternal = true
        };

        var updatedTypes = ns.Types.Select(t =>
        {
            if (IsCompilerGenerated(t.ClrName))
                return t;

            return ApplyNamesToType(ctx, t, nsScope);
        }).ToImmutableArray();

        return ns with { Types = updatedTypes };
    }

    private static TypeSymbol ApplyNamesToType(BuildContext ctx, TypeSymbol type, NamespaceScope nsScope)
    {
        var typeScope = new TypeScope
        {
            ScopeKey = $"type:{type.ClrFullName}",
            TypeFullName = type.ClrFullName,
            IsStatic = false
        };

        // Get TsEmitName from Renamer
        var tsEmitName = ctx.Renamer.GetFinalTypeName(type.StableId, nsScope);

        // Update members
        var updatedMembers = ApplyNamesToMembers(ctx, type.Members, typeScope);

        // Return new type with updated names
        return type with
        {
            TsEmitName = tsEmitName,
            Members = updatedMembers
        };
    }

    private static TypeMembers ApplyNamesToMembers(BuildContext ctx, TypeMembers members, TypeScope typeScope)
    {
        var updatedMethods = members.Methods.Select(m =>
        {
            var tsEmitName = ctx.Renamer.GetFinalMemberName(m.StableId, typeScope, m.IsStatic);
            return m with { TsEmitName = tsEmitName };
        }).ToImmutableArray();

        var updatedProperties = members.Properties.Select(p =>
        {
            var tsEmitName = ctx.Renamer.GetFinalMemberName(p.StableId, typeScope, p.IsStatic);
            return p with { TsEmitName = tsEmitName };
        }).ToImmutableArray();

        var updatedFields = members.Fields.Select(f =>
        {
            var tsEmitName = ctx.Renamer.GetFinalMemberName(f.StableId, typeScope, f.IsStatic);
            return f with { TsEmitName = tsEmitName };
        }).ToImmutableArray();

        var updatedEvents = members.Events.Select(e =>
        {
            var tsEmitName = ctx.Renamer.GetFinalMemberName(e.StableId, typeScope, e.IsStatic);
            return e with { TsEmitName = tsEmitName };
        }).ToImmutableArray();

        return members with
        {
            Methods = updatedMethods.ToImmutableArray(),
            Properties = updatedProperties.ToImmutableArray(),
            Fields = updatedFields,
            Events = updatedEvents
        };
    }
}

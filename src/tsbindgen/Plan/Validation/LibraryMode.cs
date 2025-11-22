using tsbindgen.Library;
using tsbindgen.Model;
using tsbindgen.Model.Types;
using tsbindgen.Model.Symbols.MemberSymbols;

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
    /// Library subset must be self-contained - all referenced types must be:
    /// 1. In the library contract (AllowedTypeStableIds), OR
    /// 2. Built-in/primitive types (mapped to TS primitives), OR
    /// 3. External types (not in current graph TypeIndex)
    /// </summary>
    internal static void ValidateNoDanglingReferences(
        SymbolGraph graph,
        LibraryContract contract,
        ValidationContext validationCtx)
    {
        var danglingCount = 0;

        // Walk all emitted types and their members
        foreach (var ns in graph.Namespaces)
        {
            foreach (var type in ns.Types)
            {
                // Check base type
                if (type.BaseType != null)
                {
                    CheckTypeReference(type.BaseType, type.StableId.ToString(), "base type", graph, contract, validationCtx, ref danglingCount);
                }

                // Check interfaces
                foreach (var iface in type.Interfaces)
                {
                    CheckTypeReference(iface, type.StableId.ToString(), "interface", graph, contract, validationCtx, ref danglingCount);
                }

                // Check generic parameters constraints
                foreach (var gp in type.GenericParameters)
                {
                    foreach (var constraint in gp.Constraints)
                    {
                        CheckTypeReference(constraint, type.StableId.ToString(), $"generic constraint on {gp.Name}", graph, contract, validationCtx, ref danglingCount);
                    }
                }

                // Check all members
                foreach (var method in type.Members.Methods)
                {
                    CheckMemberSignature(method, graph, contract, validationCtx, ref danglingCount);
                }

                foreach (var prop in type.Members.Properties)
                {
                    CheckTypeReference(prop.PropertyType, prop.StableId.ToString(), "property type", graph, contract, validationCtx, ref danglingCount);
                }

                foreach (var field in type.Members.Fields)
                {
                    CheckTypeReference(field.FieldType, field.StableId.ToString(), "field type", graph, contract, validationCtx, ref danglingCount);
                }

                foreach (var evt in type.Members.Events)
                {
                    CheckTypeReference(evt.EventHandlerType, evt.StableId.ToString(), "event handler type", graph, contract, validationCtx, ref danglingCount);
                }

                foreach (var ctor in type.Members.Constructors)
                {
                    foreach (var param in ctor.Parameters)
                    {
                        CheckTypeReference(param.Type, ctor.StableId.ToString(), $"constructor parameter {param.Name}", graph, contract, validationCtx, ref danglingCount);
                    }
                }
            }
        }
    }

    private static void CheckMemberSignature(MethodSymbol method, SymbolGraph graph, LibraryContract contract, ValidationContext validationCtx, ref int danglingCount)
    {
        // Check return type
        CheckTypeReference(method.ReturnType, method.StableId.ToString(), "return type", graph, contract, validationCtx, ref danglingCount);

        // Check parameters
        foreach (var param in method.Parameters)
        {
            CheckTypeReference(param.Type, method.StableId.ToString(), $"parameter {param.Name}", graph, contract, validationCtx, ref danglingCount);
        }

        // Check generic parameters constraints
        foreach (var gp in method.GenericParameters)
        {
            foreach (var constraint in gp.Constraints)
            {
                CheckTypeReference(constraint, method.StableId.ToString(), $"generic constraint on {gp.Name}", graph, contract, validationCtx, ref danglingCount);
            }
        }
    }

    private static void CheckTypeReference(
        TypeReference typeRef,
        string referencingMember,
        string context,
        SymbolGraph graph,
        LibraryContract contract,
        ValidationContext validationCtx,
        ref int danglingCount)
    {
        // Walk type reference tree
        var visited = new HashSet<TypeReference>();
        CheckTypeReferenceRecursive(typeRef, referencingMember, context, graph, contract, validationCtx, ref danglingCount, visited);
    }

    private static void CheckTypeReferenceRecursive(
        TypeReference typeRef,
        string referencingMember,
        string context,
        SymbolGraph graph,
        LibraryContract contract,
        ValidationContext validationCtx,
        ref int danglingCount,
        HashSet<TypeReference> visited)
    {
        if (!visited.Add(typeRef))
            return; // Already checked this reference

        switch (typeRef)
        {
            case NamedTypeReference named:
                // Get StableId
                var stableId = $"{named.AssemblyName}:{named.FullName}";

                // Check if it's in contract
                if (!contract.AllowedTypeStableIds.Contains(stableId))
                {
                    // Check if it's in current graph (means we filtered it out)
                    if (graph.TypeIndex.TryGetValue(stableId, out _))
                    {
                        // ERROR: Referenced a type that exists but was filtered out
                        validationCtx.RecordDiagnostic(
                            "LIB002",
                            "ERROR",
                            $"Member {referencingMember} references non-library type {stableId} ({named.FullName}) in {context}");
                        danglingCount++;
                    }
                    // else: External type (not in graph) - allowed
                }

                // Recursively check type arguments
                foreach (var typeArg in named.TypeArguments)
                {
                    CheckTypeReferenceRecursive(typeArg, referencingMember, $"{context} type argument", graph, contract, validationCtx, ref danglingCount, visited);
                }
                break;

            case ArrayTypeReference array:
                CheckTypeReferenceRecursive(array.ElementType, referencingMember, $"{context} array element", graph, contract, validationCtx, ref danglingCount, visited);
                break;

            case PointerTypeReference pointer:
                CheckTypeReferenceRecursive(pointer.PointeeType, referencingMember, $"{context} pointer element", graph, contract, validationCtx, ref danglingCount, visited);
                break;

            case ByRefTypeReference byRef:
                CheckTypeReferenceRecursive(byRef.ReferencedType, referencingMember, $"{context} byref element", graph, contract, validationCtx, ref danglingCount, visited);
                break;

            case GenericParameterReference:
                // Generic parameters don't need validation - they're bound by constraints
                break;

            case PlaceholderTypeReference:
                // Placeholders are handled separately
                break;
        }
    }

    /// <summary>
    /// LIB003: Validate binding consistency.
    /// Ensures emitted surface exactly matches binding surface:
    /// - E = emitted member StableIds
    /// - B = contract binding StableIds
    /// - Must have E == B exactly
    /// </summary>
    internal static void ValidateBindingConsistency(
        SymbolGraph graph,
        LibraryContract contract,
        ValidationContext validationCtx)
    {
        // Collect all emitted member StableIds
        var emittedMemberStableIds = new HashSet<string>();

        foreach (var ns in graph.Namespaces)
        {
            foreach (var type in ns.Types)
            {
                // Add all member StableIds
                foreach (var method in type.Members.Methods)
                {
                    emittedMemberStableIds.Add(method.StableId.ToString());
                }

                foreach (var prop in type.Members.Properties)
                {
                    emittedMemberStableIds.Add(prop.StableId.ToString());
                }

                foreach (var field in type.Members.Fields)
                {
                    emittedMemberStableIds.Add(field.StableId.ToString());
                }

                foreach (var evt in type.Members.Events)
                {
                    emittedMemberStableIds.Add(evt.StableId.ToString());
                }

                foreach (var ctor in type.Members.Constructors)
                {
                    emittedMemberStableIds.Add(ctor.StableId.ToString());
                }
            }
        }

        // Check E ⊆ B (all emitted members have bindings)
        var missingBindings = emittedMemberStableIds.Except(contract.AllowedBindingStableIds).ToList();
        foreach (var stableId in missingBindings.Take(20)) // Limit to first 20
        {
            validationCtx.RecordDiagnostic(
                "LIB003",
                "ERROR",
                $"Emitted member missing binding: {stableId}");
        }

        if (missingBindings.Count > 20)
        {
            validationCtx.RecordDiagnostic(
                "LIB003",
                "ERROR",
                $"... and {missingBindings.Count - 20} more emitted members without bindings");
        }

        // Check B ⊆ E (all bindings point to emitted members)
        var danglingBindings = contract.AllowedBindingStableIds.Except(emittedMemberStableIds).ToList();
        foreach (var stableId in danglingBindings.Take(20)) // Limit to first 20
        {
            validationCtx.RecordDiagnostic(
                "LIB003",
                "ERROR",
                $"Binding references non-emitted member: {stableId}");
        }

        if (danglingBindings.Count > 20)
        {
            validationCtx.RecordDiagnostic(
                "LIB003",
                "ERROR",
                $"... and {danglingBindings.Count - 20} more bindings without emitted members");
        }
    }
}

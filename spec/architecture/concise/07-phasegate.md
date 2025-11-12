# Phase PHASEGATE: Pre-Emission Validation

## Overview

Comprehensive validation gatekeeper. Runs after all transformations, before emission. Enforces 50+ invariants.

**Purpose**: Fail fast on invalid state, prevent malformed output, document invariants
**Input**: `SymbolGraph` + `ImportPlan` + `ConstraintFindings`
**Output**: Diagnostics (ERROR blocks emission)

---

## Validation Categories

### 1. Finalization (PG_FIN_001-009)

**Validates:** Every emitted symbol has final TypeScript name.

**Rules:**
- PG_FIN_001: Type missing TsEmitName
- PG_FIN_002: Member missing TsEmitName (ClassSurface/StaticSurface)
- PG_FIN_003: Member has empty TsEmitName
- PG_FIN_004: ViewOnly member missing TsEmitName
- PG_FIN_005: Member omitted without EmitScope.Omitted
- PG_FIN_006: Indexer not converted to methods
- PG_FIN_007: Generic static member not marked Omitted
- PG_FIN_008: Unresolved generic constraints
- PG_FIN_009: Incomplete rename decision

### 2. Scope Integrity (PG_SCOPE_001-004)

**Validates:** Scope strings are well-formed.

**Rules:**
- PG_SCOPE_001: Invalid scope format
- PG_SCOPE_002: Scope kind mismatch with EmitScope
- PG_SCOPE_003: View scope without SourceInterface
- PG_SCOPE_004: ClassSurface member in view scope

### 3. Name Uniqueness (PG_NAME_001-005)

**Validates:** No duplicate names in same scope.

**Rules:**
- PG_NAME_001: Duplicate type names in namespace
- PG_NAME_002: Duplicate member names on class surface
- PG_NAME_003: Duplicate view member names in view
- PG_NAME_004: Reserved word collision
- PG_NAME_005: Numeric suffix overflow (Name2, Name3, ..., Name1000+)

### 4. View Integrity (PG_INT_001-003)

**Validates:** ViewOnly members belong to views.

**Rules:**
- PG_INT_001: ViewOnly member without SourceInterface
- PG_INT_002: Empty view (no members)
- PG_INT_003: Orphaned view (view exists but no members)

### 5. Import/Export (PG_IMPORT/EXPORT/API_001-002)

**Validates:** Valid imports, no internal leaks.

**Rules:**
- PG_IMPORT_001: Import for non-existent type
- PG_EXPORT_001: Export of internal type from public API
- PG_API_001: Public API exposes internal type
- PG_API_002: Cross-assembly type without import

### 6. Type Resolution (PG_LOAD/TYPEMAP_001)

**Validates:** All types resolved or built-in.

**Rules:**
- PG_LOAD_001: External type reference without TypeIndex entry
- PG_TYPEMAP_001: Unsupported special form (pointer, byref in signature)

### 7. Overload Collision (PG_OL_001-002)

**Validates:** No overload collisions.

**Rules:**
- PG_OL_001: Overloads with same name/arity but different return types
- PG_OL_002: Overload group not unified (should have been merged)

### 8. Constraint Integrity (PG_CNSTR_001-004)

**Validates:** Generic constraints satisfied.

**Rules:**
- PG_CNSTR_001: new() constraint but no parameterless constructor
- PG_CNSTR_002: class constraint but type is struct
- PG_CNSTR_003: struct constraint but type is class
- PG_CNSTR_004: Constraint type not resolved

---

## Diagnostic Severity

- **ERROR** - Blocks emission, BuildResult.Success = false
- **WARNING** - Logged but doesn't block
- **INFO** - Diagnostic information

---

## Validation Modules

### Core.cs
Core validation orchestration. Runs all validator modules.

### Names.cs
Name uniqueness and collision detection.

### Views.cs
ViewOnly member and ExplicitView integrity.

### Scopes.cs
Scope string validation and consistency.

### Types.cs
Type resolution and reference validation.

### ImportExport.cs
Import/export correctness and API surface validation.

### Constraints.cs
Generic constraint satisfaction checking.

### Finalization.cs
Completeness checks (TsEmitName, rename decisions).

### Overloads.cs
Method overload collision detection.

### Context.cs
Validation context and diagnostic recording.

---

## Output Formats

### Console Log
```
=== PHASEGATE VALIDATION ===
Errors: 3
Warnings: 1
Info: 0

[ERROR] PG_FIN_002: Member System.String::Format missing TsEmitName
[ERROR] PG_NAME_002: Duplicate member name 'toString' in type System.Object
[ERROR] PG_INT_001: ViewOnly member without SourceInterface
[WARNING] PG_CNSTR_001: Type Foo doesn't satisfy new() constraint from IBar
```

### .diagnostics.txt
Full details with locations and context.

### validation-summary.json
Structured format for CI comparison:
```json
{
  "errors": 3,
  "warnings": 1,
  "info": 0,
  "diagnostics": [
    {
      "severity": "Error",
      "code": "PG_FIN_002",
      "message": "Member System.String::Format missing TsEmitName",
      "location": "System.String::Format"
    }
  ]
}
```

---

## Integration

```csharp
// In SinglePhaseBuilder.Build()
PhaseGate.Validate(ctx, graph, imports, constraintFindings);

if (ctx.Diagnostics.HasErrors())
{
    // Write diagnostic files
    WriteDiagnostics(ctx.Diagnostics, outputDir);

    return new BuildResult
    {
        Success = false,
        Diagnostics = ctx.Diagnostics.GetAll()
    };
}

// Only proceed to Emit if no errors
EmitPhase.Emit(ctx, plan, outputDir);
```

---

## Summary

**50+ validation rules** organized in 8 categories ensure:
- Every symbol has final name
- No duplicate names in scopes
- ViewOnly members belong to views
- All imports valid
- No internal types leaked to public API
- All type references resolved
- No overload collisions
- Generic constraints satisfied

**Result**: Zero malformed output, documented invariants, safe refactoring

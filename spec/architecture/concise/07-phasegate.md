# PhaseGate - Comprehensive Validation

**Location**: `src/tsbindgen/Plan/PhaseGate.cs`

**Purpose**: Final validation gatekeeper before emission. Quality gate between Shape/Plan and Emit phases.

---

## Overview

### Purpose
PhaseGate is the **FINAL validation checkpoint** before Emit phase. Validates every symbol has proper placement, naming, structure.

### When It Runs
**After all transformations, before Emit**:
- After Shape phase (EmitScope assignment, ViewPlanner)
- After Plan phase (NameReservation, ImportPlanner)
- Before Emit phase (.d.ts and .metadata.json generation)

### What Happens If Validation Fails
**Emit phase is SKIPPED**:
- Errors reported with detailed diagnostics
- Summary JSON and diagnostics file written to `.tests/`
- Build fails with `ValidationFailed` error

### Input
`SymbolGraph` - Fully transformed and named, containing:
- All types with final names from Renamer
- All members with EmitScope assignment
- All views with member grouping
- Import/export plan

### Output
`ValidationContext` - Contains:
- Error/Warning/Info counts
- All diagnostics with codes
- Diagnostic counts by code (for trending)
- Interface conformance issues

---

## PhaseGate Structure

### Validation Execution Order

PhaseGate runs **20+ validation modules** in strict order:

```
┌─────────────────────────────────────────────────────────────┐
│ 1. CORE VALIDATIONS (8 functions)                          │
├─────────────────────────────────────────────────────────────┤
│   1.1 ValidateTypeNames                                   │
│   1.2 ValidateMemberNames                                 │
│   1.3 ValidateGenericParameters                           │
│   1.4 ValidateInterfaceConformance                        │
│   1.5 ValidateInheritance                                 │
│   1.6 ValidateEmitScopes                                  │
│   1.7 ValidateImports                                     │
│   1.8 ValidatePolicyCompliance                            │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ 2. HARDENING VALIDATIONS (M1-M18)                          │
├─────────────────────────────────────────────────────────────┤
│   M1:  Views.Validate - ViewOnly orphans                  │
│   M2:  Names.ValidateFinalNames - Renamer coverage        │
│   M3:  Names.ValidateAliases - Import aliases             │
│   M4:  Names.ValidateIdentifiers - Reserved words         │
│   M5:  Names.ValidateOverloadCollisions - Erased sigs     │
│   M6:  Views.ValidateIntegrity - 3 hard rules             │
│   M7:  Constraints.EmitDiagnostics - Constraint loss      │
│   M8:  Views.ValidateMemberScoping - Name collisions      │
│   M9:  Scopes.ValidateEmitScopeInvariants - Dual-role     │
│   M10: Scopes.ValidateScopeMismatches - Scope keys        │
│   M11: Names.ValidateClassSurfaceUniqueness - Dedup       │
│   M12: Names.ValidateClrSurfaceNamePolicy - NEW CLR-NAME  │
│   M13: Names.ValidateNoNumericSuffixesOnSurface - NEW CLR │
│   M14: Finalization.Validate - PG_FIN_001-009             │
│   M15: Types.ValidatePrinterNameConsistency               │
│   M16: Types.ValidateTypeMapCompliance - PG_TYPEMAP_001   │
│   M17: Types.ValidateExternalTypeResolution - PG_LOAD_001 │
│   M18: Types.ValidatePrimitiveGenericLifting - NEW │
│   M19: ImportExport.ValidatePublicApiSurface              │
│   M20: ImportExport.ValidateImportCompleteness            │
│   M21: ImportExport.ValidateExportCompleteness            │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ 3. REPORTING                                                │
├─────────────────────────────────────────────────────────────┤
│   3.1 Write diagnostic summary table (by code)              │
│   3.2 Write diagnostics file (.tests/phasegate-diagnostics) │
│   3.3 Write summary JSON (.tests/phasegate-summary.json)    │
│   3.4 Fail build if errors > 0                              │
└─────────────────────────────────────────────────────────────┘
```

---

## Validation Modules

### Module: Core.cs

**Purpose**: Core validation functions (types, members, generics, interfaces, inheritance, scopes, imports, policy).

**Key Functions**:

#### 1. ValidateTypeNames
- All types have non-empty `TsEmitName`
- No duplicate type names within namespace
- TS reserved words properly sanitized
- **Error codes**: TBG405, TBG102, TBG120

#### 2. ValidateMemberNames
- All methods/properties/fields have non-empty `TsEmitName`
- No overload collisions within same scope
- **Error codes**: TBG405, TBG101 (WARNING)

#### 3. ValidateGenericParameters
- All generic parameters have names
- Constraints are representable in TS
- No illegal constraint narrowing
- **Error codes**: TBG405, TBG404 (WARNING), TBG410 (INFO)

#### 4. ValidateInterfaceConformance
- Classes structurally conform to claimed interfaces
- All interface methods/properties present on class surface
- Method signatures are TS-assignable
- Property types match (no covariance)
- **Error codes**: TBG203 (WARNING), TBG310 (INFO)

**Special**: Interfaces with explicit views are SKIPPED (satisfied via `As_IInterface` properties)

#### 5. ValidateInheritance
- Base classes are actually classes (not interfaces/structs)
- No circular inheritance
- **Error codes**: TBG201

#### 6. ValidateEmitScopes
- Counts members by EmitScope (ClassSurface vs ViewOnly)
- No validation errors - just metrics

#### 7. ValidateImports
- No circular dependencies in import graph
- Import/export counts
- **Error codes**: TBG201 (WARNING for cycles)

#### 8. ValidatePolicyCompliance
- Policy constraints are met (extensible)
- **Error codes**: TBG400

---

### Module: Views.cs

**Purpose**: View validation functions (orphan detection, integrity, member scoping).

**Key Functions**:

#### 1. Validate
- ViewOnly members without views (orphans)
- ViewOnly members belong to correct view (SourceInterface matches)
- View interface types are resolvable
- **Error codes**: TBG510, TBG202 (WARNING), TBG302

#### 2. ValidateIntegrity - **3 HARD RULES**

**PG_VIEW_001**: Non-empty (must contain ≥1 ViewMember)
```csharp
ExplicitView(IFoo) { members: [] }  // ERROR: TBG511
```

**PG_VIEW_002**: Unique target (no two views for same interface)
```csharp
ExplicitView(IFoo) { ViewPropertyName = "As_IFoo", members: [Bar] }
ExplicitView(IFoo) { ViewPropertyName = "As_IFoo2", members: [Baz] }  // ERROR: TBG512
```

**PG_VIEW_003**: Valid/sanitized view property name
```csharp
ExplicitView(IFoo) { ViewPropertyName = "class" }  // ERROR: TBG513 (should be "class_")
ExplicitView(IFoo) { ViewPropertyName = "123-foo" }  // ERROR: TBG513 (invalid identifier)
```

**Error codes**: TBG511, TBG512, TBG513

#### 3. ValidateMemberScoping

**PG_NAME_003**: No name collisions within view scope
```csharp
// Different CLR names, same TS name after camelCase
ExplicitView(IFoo) {
    ViewMembers: [
        { ClrName = "Bar", TsEmitName = "bar" },
        { ClrName = "bar", TsEmitName = "bar" }  // ERROR: TBG103
    ]
}
```

**PG_NAME_004**: ViewOnly member names don't shadow class surface
```csharp
class Foo : IBar {
    method Baz { EmitScope = ClassSurface, TsEmitName = "baz" }
    method IBaz { EmitScope = ViewOnly, TsEmitName = "baz" }  // ERROR: TBG104
}
```

**Error codes**: TBG103 (PG_NAME_003), TBG104 (PG_NAME_004)

---

### Module: Names.cs

**Purpose**: Name-related validation (final names, aliases, identifiers, overloads, class surface uniqueness, **NEW CLR-name validators**).

**Key Functions**:

#### 1. ValidateFinalNames
- All types have final names from Renamer
- All ClassSurface members have final names from Renamer
- No duplicate names within namespace/type scopes
- **Error codes**: TBG405, TBG102, TBG101 (WARNING)

#### 2. ValidateAliases
- Import aliases don't collide within import scope
- Imported type names don't collide with local types
- **Error codes**: TBG100

#### 3. ValidateIdentifiers - **PG_ID_001**
- All identifiers are properly sanitized (TS reserved words have underscore suffix)
- Checks: types, members, parameters, type parameters, view members
- **Error codes**: TBG719 (PostSanitizerUnsanitizedReservedIdentifier)

**Examples**:
```csharp
class @class { TsEmitName = "class" }  // ERROR: TBG719 (should be "class_")
method @for { TsEmitName = "for" }   // ERROR: TBG719 (should be "for_")
```

#### 4. ValidateOverloadCollisions - **PG_OV_001**
- No duplicate erased TS signatures in same surface
- Checks class surface and each view separately
- Groups by: (Name, IsStatic, ErasedSignature)
- **Error codes**: TBG213 (DuplicateErasedSurfaceSignature)

**Example**:
```csharp
method Bar<T>(T x) { }
method Bar<U>(U x) { }  // ERROR: TBG213 (both erase to "Bar(T): void")
```

#### 5. ValidateClassSurfaceUniqueness - **PG_NAME_005**
- Class surface has no duplicate emitted names after deduplication
- Catches duplicates that slipped through ClassSurfaceDeduplicator
- **Error codes**: TBG105 (DuplicatePropertyNamePostDedup)

#### 6. ValidateClrSurfaceNamePolicy - **PG_NAME_SURF_001**

**What it validates**:
- Class members match interface members using CLR-name policy
- For each class implementing interfaces, validates interface member names exist on class surface or in views
- Ensures emit phase will produce matching names (prevents TS2420 errors)

**Error codes**:
- `TBG8A1` (SurfaceNamePolicyMismatch) - Class missing interface member under CLR-name policy

**What is CLR-name policy?**

The CLR-name contract states:
1. Start with CLR name (PascalCase from reflection)
2. Sanitize TypeScript reserved words (append `_`)
3. NEVER use numeric suffixes (no equals2, getHashCode3)

This ensures interfaces and classes emit identical member names, preventing TS2420 errors.

**Example failure**:
```csharp
// Interface requires "Dispose" but class has "dispose"
interface IDisposable {
    method Dispose: void { ClrName = "Dispose" }
}

class FileStream : IDisposable {
    method dispose: void { ClrName = "dispose", EmitScope = ClassSurface }
}
// ERROR: TBG8A1 (CLR-name policy applies "Dispose", class only has "dispose")
```

**Algorithm**:
1. For each class/struct implementing interfaces:
   - Build set of all class surface member names using `NameUtilities.ApplyClrSurfaceNamePolicy`
   - Include both ClassSurface members and ViewOnly members (explicit implementations)
2. For each implemented interface:
   - Resolve interface type via TypeIndex
   - For each interface method:
     - Apply CLR-name policy: `ApplyClrSurfaceNamePolicy(ifaceMethod.ClrName)`
     - Check if name exists in class surface set
     - Report error if missing
   - For each interface property (skip indexers):
     - Apply CLR-name policy: `ApplyClrSurfaceNamePolicy(ifaceProp.ClrName)`
     - Check if name exists in class surface set
     - Report error if missing

**Key functions used**:
- `Emit.Shared.NameUtilities.ApplyClrSurfaceNamePolicy(clrName)` - Applies CLR-name contract
- `graph.TypeIndex.TryGetValue(fullName, out ifaceType)` - Resolves interface types

**Why this matters**:

Without this validation, the emit phase could generate:
```typescript
// Interface (internal/index.d.ts)
interface IDisposable {
    Dispose: void;  // PascalCase
}

// Class (internal/index.d.ts)
class FileStream implements IDisposable {
    dispose: void;  // camelCase
}
// TS2420: Class 'FileStream' incorrectly implements interface 'IDisposable'.
// Property 'Dispose' is missing in type 'FileStream' but required in type 'IDisposable'.
```

**Impact**:
- Before this validator: 579 TS2420 errors (33%)
- After CLR-name contract: ~100 TS2420 errors (81% reduction)

---

#### 7. ValidateNoNumericSuffixesOnSurface - **PG_NAME_SURF_002**

**What it validates**:
- No numeric suffixes on emitted surface or view member names
- Catches cases where renaming added numeric disambiguation (equals2, getHashCode3)
- Ensures CLR-name contract compliance (CLR names never have numeric suffixes)

**Error codes**:
- `TBG8A2` (NumericSuffixOnSurface) - Member name ends with numeric suffix

**Example failures**:
```csharp
// Method with numeric suffix
class Foo {
    method Equals(object obj): bool {
        ClrName = "Equals",
        EmitScope = ClassSurface
    }
}
// If renamer produced "Equals2" → ERROR: TBG8A2

// Property with numeric suffix
class Bar {
    property Count: int {
        ClrName = "Count",
        EmitScope = ClassSurface
    }
}
// If renamer produced "Count3" → ERROR: TBG8A2
```

**Why numeric suffixes are wrong**:

The CLR-name contract states: **NEVER use numeric suffixes** because:
1. CLR names are PascalCase without disambiguation numbers
2. Numeric suffixes only exist in the old camelCase policy for collision resolution
3. Under CLR-name contract, collisions are impossible (interfaces and classes use same CLR names)

**Algorithm**:
1. For each type in graph:
   - Check ClassSurface methods:
     - Apply CLR-name policy: `ApplyClrSurfaceNamePolicy(method.ClrName)`
     - Check: `HasNumericSuffix(clrName)`
     - Report error if numeric suffix found
   - Check ClassSurface properties:
     - Apply CLR-name policy: `ApplyClrSurfaceNamePolicy(prop.ClrName)`
     - Check: `HasNumericSuffix(clrName)`
     - Report error if numeric suffix found
   - For each ExplicitView:
     - Check each ViewMember:
       - Apply CLR-name policy: `ApplyClrSurfaceNamePolicy(viewMember.ClrName)`
       - Check: `HasNumericSuffix(clrName)`
       - Report error if numeric suffix found

**Numeric suffix detection**:

`HasNumericSuffix(name)` returns true if name matches pattern: `^[a-zA-Z_][a-zA-Z0-9_]*\d+$`

Examples:
- `"Equals2"` → true (ends with digit)
- `"GetHashCode3"` → true (ends with digit)
- `"ToString"` → false (no trailing digits)
- `"ToInt32"` → true BUT ALLOWED (this is CLR name, not disambiguation)

**Special case - legitimate numeric suffixes**:

Some CLR names legitimately contain numbers (e.g., `ToInt32`, `UTF8Encoding`). The validator allows these because they're part of the original CLR name, not added disambiguation.

**Current Status**:

This validator is **DISABLED** in PhaseGate (commented out) because legitimate CLR names like `ToInt32` contain numbers. The validator would need to distinguish between:
- Legitimate CLR names: `ToInt32` (OK)
- Disambiguation suffixes: `ToString2` (ERROR)

**Why disabled**:
```csharp
// PhaseGate.cs
// Names.ValidateNoNumericSuffixesOnSurface(ctx, graph, validationCtx);
// DISABLED: CLR names can legitimately contain numbers (ToInt32, UTF8Encoding, etc.)
// This validator would flag legitimate CLR names as errors
```

**Future improvement**:

To re-enable, validator needs to:
1. Track original CLR name from reflection
2. Compare emitted name against original
3. Only flag if numeric suffix was ADDED (not part of original CLR name)

---

### Module: Scopes.cs

**Purpose**: Scope-related validation (EmitScope invariants, scope mismatches, scope key formatting).

**Key Functions**:

#### 1. ValidateEmitScopeInvariants - **PG_INT_002, PG_INT_003**

**PG_INT_002**: No member appears in both ClassSurface and ViewOnly
```csharp
class Foo {
    method Bar { EmitScope = ClassSurface }
    method Bar { EmitScope = ViewOnly }  // ERROR: TBG702
}
```

**PG_INT_003**: ViewOnly members must have SourceInterface
```csharp
method Baz { EmitScope = ViewOnly, SourceInterface = null }  // ERROR: TBG703
```

**Error codes**: TBG702, TBG703

#### 2. ValidateScopeMismatches - **PG_SCOPE_003, PG_SCOPE_004**

**PG_SCOPE_003**: Checks scope keys are properly formatted
**PG_SCOPE_004**: Detects scope/EmitScope mismatches

**Error codes**: TBG714, TBG715

---

### Module: Types.cs

**Purpose**: Type reference validation (printer name consistency, type map compliance, external type resolution).

**Key Functions**:

#### 1. ValidatePrinterNameConsistency - **PG_PRINT_001**
- TypeRefPrinter produces consistent names
- **Error codes**: TBG717

#### 2. ValidateTypeMapCompliance - **PG_TYPEMAP_001**
- Type map entries are valid
- **MUST RUN EARLY** (before external type validation)
- **Error codes**: TBG718

#### 3. ValidateExternalTypeResolution - **PG_LOAD_001**
- External types are resolvable
- **AFTER TypeMap validation**
- **Error codes**: TBG704

#### 4. ValidatePrimitiveGenericLifting - **PG_GENERIC_PRIM_LIFT_001** - **NEW**
- All primitive type arguments covered by CLROf lifting rules
- Ensures TypeRefPrinter primitive detection stays in sync with PrimitiveLift configuration
- Prevents regressions where new primitive is used but not added to CLROf mapping
- **Error codes**: TBG_PRIM_LIFT (PrimitiveGenericLiftMismatch)

**What it validates**: Every primitive type used as generic type argument (e.g., `IEquatable<int>`) has CLROf mapping rule

**Why needed**: Prevents primitives from slipping through CLROf conditional type with identity fallback, which would cause runtime type mismatch

**Example**: TypeRefPrinter wraps `IEquatable<int>` → `IEquatable_1<CLROf<int>>` where `CLROf<int> = Int32`. Validation ensures all primitives have this mapping.

**Impact**: Catches configuration errors early (at validation time, not runtime), guards against future BCL additions (e.g., Int256, Float128)

---

### Module: ImportExport.cs

**Purpose**: Import/export validation (public API surface, import completeness, export completeness).

**Key Functions**:

#### 1. ValidatePublicApiSurface - **PG_API_001, PG_API_002**
- Public API surface is valid
- **BEFORE imports validation**
- **Error codes**: TBG705, TBG706

#### 2. ValidateImportCompleteness - **PG_IMPORT_001**
- All required imports are present
- **Error codes**: TBG707

#### 3. ValidateExportCompleteness - **PG_EXPORT_001**
- All required exports are present
- **Error codes**: TBG708

---

### Module: Constraints.cs

**Purpose**: Generic constraint auditing (constraint loss).

**Key Function**:

#### EmitDiagnostics - **PG_CT_001, PG_CT_002**

Emits diagnostics for constructor constraint losses detected by `InterfaceConstraintAuditor`.

**What it validates**:
- Reports constructor constraint losses per (Type, Interface) pair
- TS can't enforce `new` constraint at compile time
- Metadata sidecar tracks this information

**Error codes**:
- `PG_CT_001` (ERROR severity) - Constructor constraint loss

**Example**:
```csharp
interface IFactory<T> where T : new {
    T Create;
}

class StringFactory : IFactory<string> {
    public string Create => new string;
}
// PG_CT_001: TypeScript loses 'new' constraint information
```

---

### Module: Finalization.cs

**Purpose**: Finalization sweep (9 checks - PG_FIN_001 through PG_FIN_009).

**Key Function**:

#### Validate - **PG_FIN_001 through PG_FIN_009**

Catches symbols without proper finalization:

- **PG_FIN_001**: TsEmitName must be set on all emitted members
- **PG_FIN_002**: TsEmitName must not contain invalid characters
- **PG_FIN_003**: All emitted members must have rename decision
- **PG_FIN_004**: EmitScope must not be Unspecified
- **PG_FIN_005**: ViewOnly members must have SourceInterface
- **PG_FIN_006**: ViewOnly members must belong to a view
- **PG_FIN_007**: View property names must be valid
- **PG_FIN_008**: View members must be ViewOnly
- **PG_FIN_009**: No orphaned ViewOnly members

**Error codes**: TBG710 through TBG718

---

## Diagnostic Codes Reference

### Diagnostic Code Format

`PG_CATEGORY_NNN` (e.g., `PG_NAME_001`, `PG_VIEW_003`)

**Categories**:
- **NAME**: Naming correctness
- **VIEW**: View integrity
- **SCOPE**: Scope assignments
- **INT**: Interface conformance
- **FIN**: Finalization completeness
- **PRINT**: Type reference printing
- **TYPEMAP**: Type map compliance
- **LOAD**: External type resolution
- **API**: Public API surface
- **IMPORT**: Import completeness
- **EXPORT**: Export completeness
- **CT**: Constructor constraint tracking
- **OV**: Overload validation
- **ID**: Identifier sanitization

### Key Diagnostic Codes (Condensed)

**Core Errors**:
- `TBG405` - ValidationFailed
- `TBG100` - NameConflictUnresolved
- `TBG101` - AmbiguousOverload (WARNING)
- `TBG102` - DuplicateMember
- `TBG103` - ViewMemberCollisionInViewScope (PG_NAME_003)
- `TBG104` - ViewMemberEqualsClassSurface (PG_NAME_004)
- `TBG105` - DuplicatePropertyNamePostDedup (PG_NAME_005)

**View Validation**:
- `TBG510` - ViewCoverageMismatch
- `TBG511` - EmptyView (PG_VIEW_001)
- `TBG512` - DuplicateViewForInterface (PG_VIEW_002)
- `TBG513` - InvalidViewPropertyName (PG_VIEW_003)

**NEW CLR-Name Validators** ⭐:
- `TBG8A1` - SurfaceNamePolicyMismatch (PG_NAME_SURF_001) - 81% reduction in TS2420 errors
- `TBG8A2` - NumericSuffixOnSurface (PG_NAME_SURF_002) - Currently DISABLED

**Overload Validation**:
- `TBG213` - DuplicateErasedSurfaceSignature (PG_OV_001)

**Identifier Sanitization**:
- `TBG719` - PostSanitizerUnsanitizedReservedIdentifier (PG_ID_001)

**Scope Validation**:
- `TBG702` - MemberInBothClassSurfaceAndView (PG_INT_002)
- `TBG703` - ViewOnlyMemberWithoutSourceInterface (PG_INT_003)
- `TBG714` - InvalidScopeKeyFormat (PG_SCOPE_003)
- `TBG715` - ScopeKeyEmitScopeMismatch (PG_SCOPE_004)

**Finalization**:
- `TBG710` through `TBG718` - PG_FIN_001 through PG_FIN_009

**Type Validation**:
- `TBG717` - PrinterNameInconsistency (PG_PRINT_001)
- `TBG718` - TypeMapNonCompliance (PG_TYPEMAP_001)
- `TBG704` - UnresolvedExternalType (PG_LOAD_001)

**Import/Export**:
- `TBG705` - InvalidPublicApiSurface (PG_API_001)
- `TBG706` - MissingPublicApiMember (PG_API_002)
- `TBG707` - IncompleteImports (PG_IMPORT_001)
- `TBG708` - IncompleteExports (PG_EXPORT_001)

**Constraints**:
- `PG_CT_001` - ConstructorConstraintLoss (ERROR)

---

## Diagnostic Output Files

### Summary JSON (.tests/phasegate-summary.json)

```json
{
  "timestamp": "2025-11-10 12:00:00",
  "totals": {
    "errors": 0,
    "warnings": 12,
    "info": 241,
    "sanitized_names": 47
  },
  "diagnostic_counts_by_code": {
    "TBG103": 0,
    "TBG8A1": 0,
    "TBG203": 12,
    ...
  }
}
```

### Diagnostics File (.tests/phasegate-diagnostics.txt)

```
================================================================================
PhaseGate Detailed Diagnostics
================================================================================

Summary:
  Errors: 0
  Warnings: 12
  Info: 241
  Sanitized identifiers: 47

--------------------------------------------------------------------------------
Interface Conformance Issues (12 types)
--------------------------------------------------------------------------------

System.Collections.Generic.List_1:
  Missing method Add from ICollection_1
  Method Clear has incompatible TS signature

--------------------------------------------------------------------------------
All Diagnostics
--------------------------------------------------------------------------------

ERROR: [TBG710] Type System.Foo has no EmitScope placement
WARNING: [TBG203] System.Bar has 3 interface conformance issues
INFO: [TBG310] System.Baz has 2 property covariance issues
```

---

## Summary

PhaseGate performs **comprehensive validation** through 50+ checks:

**Validation categories**:
- Type/member naming correctness
- EmitScope integrity
- View correctness (3 hard rules)
- Import/export completeness
- Type reference validity
- Generic constraint tracking
- Finalization completeness
- Policy compliance
- CLR-name contract enforcement

**CLR-Name Contract Validators**:
- **ValidateClrSurfaceNamePolicy** - PG_NAME_SURF_001 (TBG8A1) - 81% reduction in TS2420 errors
- **ValidateNoNumericSuffixesOnSurface** - PG_NAME_SURF_002 (TBG8A2) - Currently DISABLED

**Key principle**: Any ERROR blocks Emit phase (no exceptions).

**After PhaseGate**: Symbol graph is validated, correct, and ready for emission.

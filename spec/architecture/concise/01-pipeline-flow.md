# Pipeline Flow: Sequential Phase Execution

## Overview

Pipeline executes in **strict sequential order** through 5 main phases. Each phase is **pure** (immutable data) except Phase 5 (Emit) with file I/O side effects.

**Entry Point**: `SinglePhaseBuilder.Build` in `src/tsbindgen/SinglePhase/SinglePhaseBuilder.cs`

## Phase Sequence

```
1. BuildContext.Create
   ↓
2. PHASE 1: LOAD (Reflection)
   ↓
3. PHASE 2: NORMALIZE (Build Indices)
   ↓
4. PHASE 3: SHAPE (16 transformation passes)
   ↓
5. PHASE 3.5: NAME RESERVATION
   ↓
6. PHASE 4: PLAN (Import analysis)
   ↓
7. PHASE 4.5: OVERLOAD UNIFICATION
   ↓
8. PHASE 4.6: INTERFACE CONSTRAINT AUDIT
   ↓
9. PHASE 4.7: PHASEGATE VALIDATION
   ↓
10. PHASE 5: EMIT (if no errors)
```

---

## Phase 1: LOAD

**Purpose**: Reflect over .NET assemblies using MetadataLoadContext

**Input**: `string[]` assembly paths
**Output**: `SymbolGraph` (pure CLR data, no TypeScript concepts)
**Mutability**: Pure (immutable)

**Key Operations**:
1. Create MetadataLoadContext with reference paths
2. Load transitive closure (seed + dependencies)
3. Reflect over types/members via `ReflectionReader`
4. Substitute closed generic interface members
5. Build initial SymbolGraph (namespaces, types, members, type references)

**Files**: `Load/AssemblyLoader.cs`, `Load/ReflectionReader.cs`, `Load/InterfaceMemberSubstitution.cs`

**Data State**: `TsEmitName = null`, `EmitScope` undetermined, interfaces not flattened

---

## Phase 2: NORMALIZE (Build Indices)

**Purpose**: Build lookup tables for efficient resolution

**Input**: `SymbolGraph` (from Phase 1)
**Output**: `SymbolGraph` (with indices populated)
**Mutability**: Pure (immutable)

**Key Operations**:
1. `graph.WithIndices` → NamespaceIndex, TypeIndex
2. Build GlobalInterfaceIndex (interface inheritance)
3. Build InterfaceDeclIndex (interface member declarations)

**Files**: `Model/SymbolGraph.cs`, `Shape/GlobalInterfaceIndex.cs`, `Shape/InterfaceDeclIndex.cs`

---

## Phase 3: SHAPE (16 Transformation Passes)

**Purpose**: Transform CLR semantics → TypeScript semantics

**Input**: `SymbolGraph` (with indices)
**Output**: `SymbolGraph` (TS-ready, `TsEmitName` still null)
**Mutability**: Pure (each pass returns new immutable graph)

**16 Passes (Sequential Order)**:
1. **GlobalInterfaceIndex** - Interface inheritance lookup
2. **InterfaceDeclIndex** - Interface member declarations
3. **StructuralConformance** - Synthesize ViewOnly members
4. **InterfaceInliner** - Flatten interface hierarchies
5. **ExplicitImplSynthesizer** - Explicit impl ViewOnly members
6. **DiamondResolver** - Resolve diamond inheritance
7. **BaseOverloadAdder** - Add base overloads
8. **StaticSideAnalyzer** - Analyze static members
9. **IndexerPlanner** - Mark indexers for omission
10. **HiddenMemberPlanner** - Handle C# 'new' keyword
11. **FinalIndexersPass** - Remove leaked indexers
12. **ClassSurfaceDeduplicator** - Demote duplicates to ViewOnly
13. **ConstraintCloser** - Complete constraint closures
14. **OverloadReturnConflictResolver** - Resolve return conflicts
15. **ViewPlanner** - Plan explicit interface views
16. **MemberDeduplicator** - Final deduplication

**Files**: `Shape/*.cs` (16 files)

**Output State**: `EmitScope` determined (ClassSurface/ViewOnly/Omitted), `TsEmitName = null`

---

## Phase 3.5: NAME RESERVATION

**Purpose**: Assign all TypeScript names via central Renamer

**Input**: `SymbolGraph` (TS-ready but unnamed)
**Output**: `SymbolGraph` (with `TsEmitName` assigned)
**Mutability**: Side effect (Renamer) + pure (returns new graph)

**Key Operations**:
1. For each type: `Renamer.ReserveTypeName`
2. For each member: `Renamer.ReserveMemberName` (correct scope)
3. Apply style transforms, sanitize reserved words
4. Audit completeness (fail fast if missing)
5. `Application.ApplyNamesToGraph` → set `TsEmitName`

**Files**: `Normalize/NameReservation.cs`, `Normalize/Naming/*.cs`

**Scopes**: Namespace, ClassSurface, View (separate naming contexts)

---

## Phase 4: PLAN

**Purpose**: Build import graph, plan emission order

**Input**: `SymbolGraph` (fully named)
**Output**: `EmissionPlan` (graph + imports + order)
**Mutability**: Pure (immutable)

**Key Operations**:
1. Build `ImportGraph` (cross-namespace dependencies)
2. `ImportPlanner.PlanImports` (imports, exports, aliases)
3. `EmitOrderPlanner.PlanOrder` (stable order)

**Files**: `Plan/ImportGraph.cs`, `Plan/ImportPlanner.cs`, `Plan/EmitOrderPlanner.cs`

---

## Phase 4.5: OVERLOAD UNIFICATION

**Purpose**: Merge method overloads into single declaration

**Input**: `SymbolGraph`
**Output**: `SymbolGraph` (overloads unified)
**Mutability**: Pure (immutable)

**Files**: `Plan/OverloadUnifier.cs`

---

## Phase 4.6: INTERFACE CONSTRAINT AUDIT

**Purpose**: Audit constructor constraints per (Type, Interface) pair

**Input**: `SymbolGraph`
**Output**: `ConstraintFindings`
**Mutability**: Pure (immutable)

**Files**: `Plan/InterfaceConstraintAuditor.cs`

---

## Phase 4.7: PHASEGATE VALIDATION

**Purpose**: Validate entire pipeline before emission

**Input**: `SymbolGraph`, `ImportPlan`, `ConstraintFindings`
**Output**: Side effect (record diagnostics in `BuildContext.Diagnostics`)
**Mutability**: Side effect only

**Key Operations**:
- Run 50+ validation checks
- Record ERROR/WARNING/INFO diagnostics
- Fail fast if ERROR-level diagnostics found

**Files**: `Plan/PhaseGate.cs`, `Plan/Validation/*.cs`

**Critical Rule**: Any ERROR blocks Phase 5 (Emit)

---

## Phase 5: EMIT

**Purpose**: Generate all output files

**Input**: `EmissionPlan` (validated)
**Output**: Side effects (file I/O)
**Mutability**: Side effects only

**Key Operations**:
1. `SupportTypesEmit` → `_support/types.d.ts`
2. For each namespace (in emission order):
   - `InternalIndexEmitter` → `<ns>/internal/index.d.ts`
   - `FacadeEmitter` → `<ns>/index.d.ts`
   - `MetadataEmitter` → `<ns>/metadata.json`
   - `BindingEmitter` → `<ns>/bindings.json`
   - `ModuleStubEmitter` → `<ns>/index.js`

**Files**: `Emit/SupportTypesEmitter.cs`, `Emit/InternalIndexEmitter.cs`, `Emit/FacadeEmitter.cs`, `Emit/MetadataEmitter.cs`, `Emit/BindingEmitter.cs`, `Emit/ModuleStubEmitter.cs`

**Critical Rule**: Only executes if `ctx.Diagnostics.HasErrors == false`

---

## Data Transformations Table

| Phase | Input | Output | Mutability | Key Transformations |
|-------|-------|--------|------------|---------------------|
| **1. LOAD** | `string[]` | `SymbolGraph` | Pure | Reflection → SymbolGraph |
| **2. NORMALIZE** | `SymbolGraph` | `SymbolGraph` | Pure | Build indices |
| **3. SHAPE** | `SymbolGraph` | `SymbolGraph` | Pure | 16 passes: flatten, synthesize, determine EmitScope |
| **3.5. NAME** | `SymbolGraph` | `SymbolGraph` | Side effect + pure | Reserve names, set TsEmitName |
| **4. PLAN** | `SymbolGraph` | `EmissionPlan` | Pure | Import graph, emission order |
| **4.5. OVERLOAD** | `SymbolGraph` | `SymbolGraph` | Pure | Merge overloads |
| **4.6. CONSTRAINT** | `SymbolGraph` | `ConstraintFindings` | Pure | Audit constraints |
| **4.7. PHASEGATE** | All | Diagnostics | Side effect | 50+ validation checks |
| **5. EMIT** | `EmissionPlan` | Files | Side effect | Generate .d.ts, .json, .js |

---

## Critical Sequencing Rules

### Shape Pass Dependencies
- **StructuralConformance BEFORE InterfaceInliner**: Needs original hierarchy
- **InterfaceInliner BEFORE ExplicitImplSynthesizer**: Needs flattened interfaces
- **IndexerPlanner BEFORE FinalIndexersPass**: Mark before remove
- **ViewPlanner BEFORE MemberDeduplicator**: Plan before final dedup

### Name Reservation Timing
**MUST** occur:
- **AFTER** all Shape passes (EmitScope determined first)
- **BEFORE** Plan phase (PhaseGate needs TsEmitName)

### PhaseGate Position
**MUST** occur:
- **AFTER** all transformations + naming
- **BEFORE** Emit phase

### Emit Phase Gating
**ONLY** executes if `ctx.Diagnostics.HasErrors == false`

---

## Immutability Pattern

Every phase (except Emit) follows this pattern:

```csharp
public static TOutput PhaseFunction(BuildContext ctx, TInput input)
{
    // input is immutable - read only
    var transformed = ApplyTransformation(input);
    return transformed; // new immutable output
}
```

**Benefits**: No hidden mutations, safe parallelization (future), easy debugging, clear data flow

---

## Summary

**Pipeline**: Deterministic, pure functional transformation (except Emit I/O)

**Flow**: Load → Normalize → Shape → Name → Plan → Validate → Emit

**Guarantees**: Immutability, predictability, fail-fast validation, 100% data integrity

# Pipeline Flow: Sequential Phase Execution

## Overview

Pipeline executes in **strict sequential order** through 5 main phases. Each phase is **pure** (returns new immutable data) except Emit which has file I/O side effects.

**Entry Point**: `SinglePhaseBuilder.Build()`

## Sequential Execution

```
1. BuildContext.Create()
2. PHASE 1: LOAD
3. PHASE 2: NORMALIZE (Build Indices)
4. PHASE 3: SHAPE (18 transformation passes)
5. PHASE 3.5: NAME RESERVATION
6. PHASE 4: PLAN
7. PHASE 4.5: OVERLOAD UNIFICATION
8. PHASE 4.6: INTERFACE CONSTRAINT AUDIT
9. PHASE 4.7: PHASEGATE VALIDATION
10. PHASE 5: EMIT (if no errors)
```

---

## PHASE 1: LOAD

**Purpose**: Reflect over .NET assemblies to build initial SymbolGraph

**Input**: `string[]` assemblyPaths
**Output**: `SymbolGraph` (pure CLR, no TypeScript concepts)
**Mutability**: Pure (immutable)

**Operations**:
1. Create `MetadataLoadContext` with reference paths
2. Load transitive closure (seed + dependencies)
3. Reflect via `ReflectionReader.ReadAssemblies()`
4. Substitute closed generic interface members
5. Build SymbolGraph:
   - Namespaces, types, members
   - Type references (base, interfaces, generic args)

**Files**: `Load/AssemblyLoader.cs`, `Load/ReflectionReader.cs`, `Load/InterfaceMemberSubstitution.cs`, `Load/DeclaringAssemblyResolver.cs`

**State**: `TsEmitName` null, `EmitScope` undetermined, interfaces not flattened

---

## PHASE 2: NORMALIZE

**Purpose**: Build lookup tables for cross-reference resolution

**Input**: `SymbolGraph` (from Phase 1)
**Output**: `SymbolGraph` (with indices)
**Mutability**: Pure (immutable)

**Operations**:
1. `graph.WithIndices()`:
   - `NamespaceIndex`: namespace name → NamespaceSymbol
   - `TypeIndex`: CLR full name → TypeSymbol
2. Build `GlobalInterfaceIndex` (interface inheritance)
3. Build `InterfaceDeclIndex` (member declarations)

**Files**: `Model/SymbolGraph.cs`, `Shape/GlobalInterfaceIndex.cs`, `Shape/InterfaceDeclIndex.cs`

---

## PHASE 3: SHAPE (18 Passes)

**Purpose**: Transform CLR → TypeScript semantics

**Input**: `SymbolGraph` (indexed)
**Output**: `SymbolGraph` (TS-ready, unnamed)
**Mutability**: Pure (each pass returns new graph)

**Transformations**: Interface flattening, explicit impl synthesis, diamond resolution, overload handling, deduplication, EmitScope determination

### Shape Passes (Sequential Order)

1. **GlobalInterfaceIndex.Build()** - Interface inheritance lookup
2. **InterfaceDeclIndex.Build()** - Member declaration lookup
3. **InterfaceInliner.Inline()** - Flatten interface hierarchies (copy inherited members)
4. **InternalInterfaceFilter.Filter()** - Remove internal BCL interfaces
5. **StructuralConformance.Analyze()** - Synthesize ViewOnly for non-conforming interfaces
6. **ExplicitImplSynthesizer.Synthesize()** - Synthesize ViewOnly for explicit impls
7. **InterfaceResolver.Resolve()** - Resolve declaring interface for members
8. **DiamondResolver.Resolve()** - Resolve diamond inheritance conflicts
9. **BaseOverloadAdder.AddOverloads()** - Add base class overloads
10. **OverloadReturnConflictResolver.Resolve()** - Resolve return type conflicts
11. **MemberDeduplicator.Deduplicate()** - Remove duplicates
12. **ViewPlanner.Plan()** - Plan explicit interface views
13. **ClassSurfaceDeduplicator.Deduplicate()** - Demote duplicates to ViewOnly
14. **HiddenMemberPlanner.Plan()** - Handle C# 'new' keyword
15. **IndexerPlanner.Plan()** - Mark indexers Omitted
16. **FinalIndexersPass.Run()** - Remove leaked indexers
17. **StaticSideAnalyzer.Analyze()** - Analyze static members
18. **ConstraintCloser.Close()** - Complete generic constraints

**Files**: `Shape/*.cs` (18 files)

**State After**: `EmitScope` determined, transformations complete, `TsEmitName` still null

---

## PHASE 3.5: NAME RESERVATION

**Purpose**: Assign all TypeScript names via SymbolRenamer

**Input**: `SymbolGraph` (TS-ready but unnamed)
**Output**: `SymbolGraph` (fully named)
**Mutability**: Side effect (populates Renamer) + pure (returns new graph)

**Operations**:
1. For each type: Apply transforms, reserve via `Renamer.ReserveTypeName()`
2. For each member:
   - Skip already renamed (by earlier passes)
   - Apply syntax transforms (`` ` `` → `_`, `+` → `_`)
   - Sanitize reserved words (add `_` suffix)
   - Reserve via `Renamer.ReserveMemberName()` with correct scope
3. Audit completeness (fail if any emitted member lacks decision)
4. Apply names to graph (`Application.ApplyNamesToGraph()`)

**Files**: `Normalize/NameReservation.cs`, `Normalize/Naming/*.cs`

**Scopes**:
- Type names: `ScopeFactory.Namespace(ns, NamespaceArea.Internal)`
- Class surface: `ScopeFactory.ClassSurface(type, isStatic)`
- View members: `ScopeFactory.View(type, interfaceStableId)`

**State After**: All symbols have `TsEmitName` assigned

---

## PHASE 4: PLAN

**Purpose**: Build import graph, plan emission order

**Input**: `SymbolGraph` (fully named)
**Output**: `EmissionPlan` (SymbolGraph + ImportPlan + EmitOrder)
**Mutability**: Pure (immutable)

**Operations**:
1. Build `ImportGraph` (cross-namespace dependencies)
2. Plan imports/exports via `ImportPlanner.PlanImports()`
3. Determine stable order via `EmitOrderPlanner.PlanOrder()`

**Files**: `Plan/ImportGraph.cs`, `Plan/ImportPlanner.cs`, `Plan/EmitOrderPlanner.cs`

---

## PHASE 4.5: OVERLOAD UNIFICATION

**Purpose**: Merge method overloads into single declaration

**Input**: `SymbolGraph`
**Output**: `SymbolGraph` (overloads unified)
**Mutability**: Pure (immutable)

**Operations**: Group by name/scope, merge compatible returns, preserve distinct signatures

**Files**: `Plan/OverloadUnifier.cs`

---

## PHASE 4.6: INTERFACE CONSTRAINT AUDIT

**Purpose**: Audit constructor constraints per (Type, Interface) pair

**Input**: `SymbolGraph`
**Output**: `ConstraintFindings`
**Mutability**: Pure (immutable)

**Operations**: Check each (Type, Interface) for constructor constraints, record findings

**Files**: `Plan/InterfaceConstraintAuditor.cs`

---

## PHASE 4.7: PHASEGATE VALIDATION

**Purpose**: Validate pipeline output before emission

**Input**: `SymbolGraph` + `ImportPlan` + `ConstraintFindings`
**Output**: Side effect (records diagnostics)
**Mutability**: Side effect only

**Operations**: Run 50+ validation checks, record ERROR/WARNING/INFO diagnostics

**Files**: `Plan/PhaseGate.cs`, `Plan/Validation/*.cs`

**Critical**: Any ERROR blocks Phase 5, Build returns `Success = false`

---

## PHASE 5: EMIT

**Purpose**: Generate output files

**Input**: `EmissionPlan` (validated)
**Output**: File I/O (*.d.ts, *.json, *.js)
**Mutability**: Side effects only

**Operations**:
1. Emit `_support/types.d.ts` (marker types)
2. For each namespace (in emission order):
   - `<ns>/internal/index.d.ts` (internal declarations)
   - `<ns>/index.d.ts` (public facade)
   - `<ns>/metadata.json` (CLR info)
   - `<ns>/bindings.json` (CLR → TS name mappings)
   - `<ns>/index.js` (ES module stub)

**Files**: `Emit/SupportTypesEmitter.cs`, `Emit/InternalIndexEmitter.cs`, `Emit/FacadeEmitter.cs`, `Emit/MetadataEmitter.cs`, `Emit/BindingEmitter.cs`, `Emit/ModuleStubEmitter.cs`

**Critical**: Only executes if `ctx.Diagnostics.HasErrors() == false`

---

## Data Transformations

| Phase | Input | Output | Mutability | Key Transformations |
|-------|-------|--------|------------|---------------------|
| **1. LOAD** | `string[]` | `SymbolGraph` | Pure | Reflection → SymbolGraph |
| **2. NORMALIZE** | `SymbolGraph` | `SymbolGraph` | Pure | Build indices |
| **3. SHAPE** | `SymbolGraph` | `SymbolGraph` | Pure | 18 passes: flatten, synthesize, deduplicate |
| **3.5. NAME RESERVATION** | `SymbolGraph` | `SymbolGraph` | Side effect + pure | Reserve names, set TsEmitName |
| **4. PLAN** | `SymbolGraph` | `EmissionPlan` | Pure | Import graph, emission order |
| **4.5. OVERLOAD UNIFICATION** | `SymbolGraph` | `SymbolGraph` | Pure | Merge overloads |
| **4.6. CONSTRAINT AUDIT** | `SymbolGraph` | `ConstraintFindings` | Pure | Audit constraints |
| **4.7. PHASEGATE** | `SymbolGraph` + `ImportPlan` + `ConstraintFindings` | Side effect | Side effect | 50+ validation checks |
| **5. EMIT** | `EmissionPlan` | File I/O | Side effects | Generate files |

---

## Critical Sequencing Rules

### Shape Pass Dependencies

**MUST execute in order**:
- **InterfaceInliner BEFORE InternalInterfaceFilter**: Filter needs flattened list
- **InternalInterfaceFilter BEFORE StructuralConformance**: Don't synthesize for internal interfaces
- **StructuralConformance BEFORE ExplicitImplSynthesizer**: Conformance needs original hierarchy
- **InterfaceInliner BEFORE ExplicitImplSynthesizer**: Synthesis needs flattened interfaces
- **IndexerPlanner BEFORE FinalIndexersPass**: Mark before removing
- **ClassSurfaceDeduplicator BEFORE ConstraintCloser**: Dedup may affect constraints
- **OverloadReturnConflictResolver BEFORE ViewPlanner**: Conflicts resolved before planning
- **ViewPlanner BEFORE MemberDeduplicator**: Views planned before final dedup

### Name Reservation Timing

**MUST occur**:
- **AFTER** all Shape passes (EmitScope determined)
- **BEFORE** Plan phase (PhaseGate needs TsEmitName)

### PhaseGate Position

**MUST occur**:
- **AFTER** all transformations
- **AFTER** names assigned
- **BEFORE** Emit

### Emit Gating

**ONLY executes if**: `ctx.Diagnostics.HasErrors() == false`

If errors exist: Build returns `Success = false` immediately

---

## Immutability Pattern

Every phase (except Emit) follows:

```csharp
public static TOutput PhaseFunction(BuildContext ctx, TInput input)
{
    // input is immutable - read only
    var transformed = ApplyTransformation(input);
    return transformed;  // New immutable output
}
```

**Example**:
```csharp
public static SymbolGraph Inline(BuildContext ctx, SymbolGraph graph)
{
    // graph never modified
    var newNamespaces = graph.Namespaces
        .Select(ns => ns with { Types = FlattenTypes(ns.Types) })
        .ToImmutableArray();

    return graph with { Namespaces = newNamespaces };  // New graph
}
```

**Benefits**: No hidden mutations, safe parallelization (future), easy debugging, clear data flow

---

## Summary

**Flow**: BuildContext → Load (Reflection) → Normalize (Indices) → Shape (18 transforms) → Name Reservation (SymbolRenamer) → Plan (Imports + Order) → Overload Unification → Constraint Audit → PhaseGate (Validation) → Emit (Files)

**Principles**: Sequential execution, immutable data, pure functions, side effects isolated to Renamer and Emit

**Validation**: PhaseGate enforces 50+ invariants before any files generated

**Result**: Type-safe TypeScript declarations with 100% data integrity

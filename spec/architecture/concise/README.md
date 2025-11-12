# SinglePhase Pipeline Architecture - Concise Documentation

Compressed architecture documentation for the tsbindgen SinglePhase pipeline. ~70% shorter than main docs while preserving essential information.

## Documentation Index

### Core Architecture
- **[00-overview.md](00-overview.md)** - System overview, principles, BuildContext services
- **[01-pipeline-flow.md](01-pipeline-flow.md)** - Phase sequence, data flow, immutability

### Pipeline Phases (Execution Order)
- **[02-phase-load.md](02-phase-load.md)** - Reflection, assembly loading, type references
- **[03-phase-model.md](03-phase-model.md)** - Data structures (SymbolGraph, TypeSymbol, MemberSymbols)
- **[04-phase-shape.md](04-phase-shape.md)** - 18 transformation passes (CLR → TypeScript semantics)
- **[05-phase-normalize.md](05-phase-normalize.md)** - Name reservation, overload unification
- **[06-phase-plan.md](06-phase-plan.md)** - Import planning, emission order, cross-assembly resolution
- **[07-phasegate.md](07-phasegate.md)** - 50+ validation rules, 43 diagnostic codes
- **[08-phase-emit.md](08-phase-emit.md)** - File generation (6 output types)

### Infrastructure
- **[09-renaming.md](09-renaming.md)** - SymbolRenamer, dual-scope naming, collision resolution
- **[10-call-graphs.md](10-call-graphs.md)** - Call chains, execution flow

## Reading Guide

**New developers:**
1. 00-overview.md - Big picture
2. 01-pipeline-flow.md - Phase connections
3. 03-phase-model.md - Data structures

**Understanding transformations:**
- 04-phase-shape.md - CLR → TypeScript semantic mapping

**Understanding naming:**
- 09-renaming.md - Dual-scope algorithm
- 05-phase-normalize.md - Name reservation flow

**Understanding validation:**
- 07-phasegate.md - All validation rules

## Key Concepts

### StableId
Assembly-qualified identifiers:
- **TypeStableId**: `"Assembly:Namespace.Type\`Arity"`
- **MemberStableId**: `"Assembly:Type::Member(Signature):Return"`

### EmitScope
Placement decisions:
- `ClassSurface` - On class directly
- `ViewOnly` - In As_IInterface view
- `Omitted` - Not emitted

### Dual-Scope Naming
Separate scopes:
- Class surface (instance/static)
- View surface (per interface)

### ViewPlanner
Explicit interface implementations:
- Synthesizes `As_IInterface` properties
- ViewOnly members with `$view` suffix if collision

## Coverage

✅ 76 source files
✅ All public/private methods
✅ All 50+ validation rules
✅ All 43 diagnostic codes
✅ All 18 transformation passes
✅ Complete call chains
✅ All data structures

## Phase Navigation

- Phase 1 (Load): [02-phase-load.md](02-phase-load.md)
- Phase 2 (Model): [03-phase-model.md](03-phase-model.md)
- Phase 3 (Shape): [04-phase-shape.md](04-phase-shape.md)
- Phase 3.5 (Normalize): [05-phase-normalize.md](05-phase-normalize.md)
- Phase 4 (Plan): [06-phase-plan.md](06-phase-plan.md)
- Phase 4.7 (PhaseGate): [07-phasegate.md](07-phasegate.md)
- Phase 5 (Emit): [08-phase-emit.md](08-phase-emit.md)

---

**Note**: For full detail, see main architecture docs in `spec/architecture/`. These concise docs focus on essential algorithms and flows.

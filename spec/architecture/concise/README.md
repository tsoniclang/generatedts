# SinglePhase Pipeline Architecture Documentation (Concise)

**Condensed architectural documentation for the tsbindgen SinglePhase pipeline** (~35% size of full docs).

## Documentation Index

### Core Architecture
- **[00-overview.md](00-overview.md)** - System overview, principles, BuildContext, Tsonic compiler context
- **[01-pipeline-flow.md](01-pipeline-flow.md)** - Phase sequence, data transformations

### Pipeline Phases (Execution Order)
- **[02-phase-load.md](02-phase-load.md)** - Phase 1: Reflection and assembly loading
- **[03-phase-model.md](03-phase-model.md)** - Data structures (SymbolGraph, TypeSymbol, StableId)
- **[04-phase-shape.md](04-phase-shape.md)** - Phase 3: 16 transformation passes
- **[05-phase-normalize.md](05-phase-normalize.md)** - Phase 3.5: Name reservation
- **[06-phase-plan.md](06-phase-plan.md)** - Phase 4: Import planning
- **[07-phasegate.md](07-phasegate.md)** - Phase 4.7: Validation (50+ rules, 40+ diagnostic codes)
- **[08-phase-emit.md](08-phase-emit.md)** - Phase 5: File generation (.d.ts, .json, .js)

### Infrastructure
- **[09-renaming.md](09-renaming.md)** - Renaming system (SymbolRenamer, dual-scope naming)
- **[10-call-graphs.md](10-call-graphs.md)** - Call chains and execution flow

## Reading Guide

**For New Developers**: Start with 00-overview.md → 01-pipeline-flow.md → 10-call-graphs.md

**For Validation**: 07-phasegate.md (every validation rule + diagnostic code)

**For Naming**: 09-renaming.md (dual-scope algorithm, collision resolution)

**For Transformations**: 04-phase-shape.md (16 passes, CLR → TypeScript)

**For Code Generation**: 08-phase-emit.md (output formats, printers)

## Key Concepts

- **StableId**: Assembly-qualified identifiers (type/member identity)
- **EmitScope**: Placement decisions (ClassSurface, ViewOnly, Omitted)
- **Dual-Scope Naming**: Separate scopes for class vs view members
- **ViewPlanner**: Explicit interface implementation via `As_IInterface` properties
- **PhaseGate**: Final validation (50+ rules, blocks emission on errors)

## Coverage

- ✅ All 76 files in SinglePhase/
- ✅ All public methods with signatures
- ✅ All validation rules and diagnostic codes
- ✅ All transformation passes
- ✅ Complete call chains
- ✅ Key algorithms and data structures

## Statistics

- 76 source files
- 12 architecture documents
- 50+ validation rules
- 40+ diagnostic codes
- 16 transformation passes
- 6 output file types

## Concise vs Full

| Document | Full | Concise | Reduction |
|----------|------|---------|-----------|
| All docs | ~21,500 lines | ~7,000 lines | 65% |

**Note**: Concise docs preserve all critical information but remove verbose examples, extended explanations, and duplicate content.

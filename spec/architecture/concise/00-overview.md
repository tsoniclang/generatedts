# SinglePhase Pipeline Architecture

## 1. System Overview

### What is tsbindgen?

**tsbindgen** transforms .NET assemblies into TypeScript declaration files with metadata sidecars, enabling the **Tsonic compiler** to import and use .NET BCL types with full IDE support and type safety.

### The Tsonic Compiler

**Tsonic** is a TypeScript-to-C# compiler targeting NativeAOT executables:

- Enables writing .NET applications using TypeScript syntax
- Direct .NET interop: `import { File } from "System.IO"`
- Compiles to single-file native executables via .NET CLI
- Full TypeScript type checking against .NET APIs

**Example**:
```typescript
import { File } from "System.IO";
import { Console } from "System";

const lines = File.ReadAllLines("data.txt");
Console.WriteLine(`Read ${lines.length} lines`);
```

### How tsbindgen Enables Tsonic

tsbindgen generates three companion artifacts per assembly:

1. **Type declarations** (`*.d.ts`) - For IDE IntelliSense and type checking
2. **Metadata sidecars** (`*.metadata.json`) - CLR semantics (virtual/override, struct, ref)
3. **Binding manifests** (`*.bindings.json`) - Maps TS names to CLR names

### Purpose

Bridge TypeScript and .NET:

- **Input**: .NET assembly DLLs
- **Process**: Reflect, analyze, resolve conflicts, validate, emit
- **Output**: TypeScript `.d.ts` + JSON metadata

Enables:
- Reference .NET types with IntelliSense
- Use `List<T>`, `Dictionary<K,V>`, `File`, `Console` from TypeScript
- Type-safe TypeScript→C# boundary
- Correct C# code generation via metadata

---

## 2. Architectural Principles

### Single-Pass Processing

Pipeline processes each assembly **once** through sequential phases. No iteration. Each phase transforms symbol graph immutably.

### Immutable Data Structures

All data structures are **immutable records** (`SymbolGraph`, `TypeSymbol`, `MemberSymbol`). Transformations return new instances via `with` expressions. Enables:
- Pure functional transformations
- Safe parallelization
- Precise change tracking

### Pure Functions

Transformation logic in **static classes** with **pure functions**. No instance state, no side effects. Input → Process → Output.

### Centralized State (BuildContext)

Shared services:
- **Policy**: Configuration (naming, filters)
- **SymbolRenamer**: Centralized naming authority
- **DiagnosticBag**: Error/warning collection
- **Interner**: String deduplication
- **Logger**: Progress reporting

### StableId-Based Identity

Every symbol has **StableId** (assembly-qualified identifier):
- **Type**: `"System.Private.CoreLib:System.Decimal"`
- **Member**: `"System.Private.CoreLib:System.Decimal::ToString(IFormatProvider):String"`

Immutable, unique, stable across runs. Used for rename decisions, cross-assembly refs, binding.

### Scope-Based Naming

TypeScript names reserved in **scopes** for uniqueness:
- **Namespace**: `ns:System.Collections.Generic:internal`
- **Class Instance**: `type:System.Decimal#instance`
- **View**: `view:CoreLib:System.Decimal:CoreLib:IConvertible#instance`

Enables class member `ToString()` and view member `ToString()` to coexist.

---

## 3. Pipeline Phases

```
Input: .NET Assembly DLLs
  ↓
[Phase 1: Load] - System.Reflection → SymbolGraph (pure CLR data)
  ↓
[Phase 2: Normalize] - Build indices (NamespaceIndex, TypeIndex)
  ↓
[Phase 3: Shape] - 16 transformations (interface inlining, view planning, etc.)
  ↓
[Phase 3.5: Name Reservation] - Reserve all names via SymbolRenamer
  ↓
[Phase 4: Plan] - Import analysis + PhaseGate validation (50+ rules)
  ↓
[Phase 5: Emit] - Generate .d.ts, .metadata.json, .bindings.json
  ↓
Output: TypeScript declarations + metadata
```

### Phase 1: Load (Reflection)

- **AssemblyLoader**: Transitive closure loading
- **ReflectionReader**: System.Reflection → SymbolGraph
- **TypeReferenceFactory**: Build TypeReference objects
- **InterfaceMemberSubstitution**: Substitute closed generic interface members

**Output**: SymbolGraph (pure CLR data, no TypeScript concepts)

### Phase 2: Normalize (Build Indices)

- Build NamespaceIndex, TypeIndex for fast lookups
- Canonical signature normalization

**Output**: SymbolGraph with indices

### Phase 3: Shape (Transformations)

16 sequential passes handling .NET/TypeScript impedance mismatches:

1. **GlobalInterfaceIndex** - Index all interfaces
2. **InterfaceDeclIndex** - Index interface members
3. **StructuralConformance** - Analyze structural conformance (mark ViewOnly)
4. **InterfaceInliner** - Flatten interface hierarchy
5. **ExplicitImplSynthesizer** - Synthesize explicit implementations
6. **DiamondResolver** - Resolve diamond inheritance
7. **BaseOverloadAdder** - Add base overloads
8. **StaticSideAnalyzer** - Analyze static members
9. **IndexerPlanner** - Mark indexers as Omitted
10. **HiddenMemberPlanner** - Handle C# 'new' keyword
11. **FinalIndexersPass** - Remove leaked indexers
12. **ClassSurfaceDeduplicator** - Deduplicate class surface
13. **ConstraintCloser** - Resolve generic constraints
14. **OverloadReturnConflictResolver** - Resolve return type conflicts
15. **ViewPlanner** - Plan explicit interface views
16. **MemberDeduplicator** - Final deduplication

**Output**: SymbolGraph shaped for TypeScript

### Phase 3.5: Name Reservation

- **NameReservation**: Reserve all type/member names via SymbolRenamer
- Apply naming policy (PascalCase, camelCase, etc.)
- Resolve conflicts via numeric suffixes
- Sanitize reserved words (class → class_)

**Output**: SymbolGraph + RenameDecisions in Renamer

### Phase 4: Plan (Import Analysis + Validation)

- **ImportGraph**: Build dependency graph
- **ImportPlanner**: Determine imports/aliases
- **EmitOrderPlanner**: Stable emission order
- **OverloadUnifier**: Merge overload variants
- **InterfaceConstraintAuditor**: Audit constructor constraints
- **PhaseGate**: Validate 50+ invariants before emission

**Output**: EmissionPlan (graph + imports + order)

### Phase 5: Emit (File Generation)

- **SupportTypesEmitter**: `_support/types.d.ts`
- **InternalIndexEmitter**: `internal/index.d.ts` per namespace
- **FacadeEmitter**: `index.d.ts` per namespace
- **MetadataEmitter**: `metadata.json` per namespace
- **BindingEmitter**: `bindings.json` per namespace
- **ModuleStubEmitter**: `index.js` stubs per namespace

**Output**: Files written to output directory

---

## 4. Key Concepts

### StableId: Immutable Symbol Identity

**Format**:
- Type: `{AssemblyName}:{ClrFullName}`
- Member: `{AssemblyName}:{DeclaringType}::{MemberName}{Signature}`

**Properties**: Immutable, unique, stable across runs, semantic (not metadata token)

**Usage**: Rename decision keys, cross-assembly refs, binding metadata, duplicate detection

### EmitScope: Where Members Are Emitted

```csharp
enum EmitScope {
    ClassSurface,   // On class body
    StaticSurface,  // Static section
    ViewOnly,       // As_IInterface view property
    Omitted         // Not emitted (tracked in metadata)
}
```

**ClassSurface**: Default for public members
**ViewOnly**: Explicit interface implementations (structural conformance failed)
**Omitted**: Indexers, generic static members, internal/private (tracked in metadata)

### ViewPlanner: Explicit Interface Implementation Support

TypeScript lacks explicit interface implementations. **ViewPlanner** generates view properties:

```typescript
class Decimal {
    ToString(): string;  // ClassSurface

    As_IConvertible: {   // ViewOnly members
        ToBoolean(provider: IFormatProvider | null): boolean;
        ToInt32(provider: IFormatProvider | null): int;
    };
}
```

**Process**: StructuralConformance marks ViewOnly → ViewPlanner groups by interface → Emit view properties

### Scope-Based Naming

Separate scopes enable name reuse:

```typescript
class Decimal {
    ToString(): string;  // Scope: type:Decimal#instance
    As_IConvertible: {
        ToString(): string;  // Scope: view:Decimal:IConvertible#instance
    };
    static Parse(s: string): Decimal;  // Scope: type:Decimal#static
}
```

**Scopes**: Namespace, Class Instance, Class Static, View Instance, View Static

### PhaseGate: Pre-Emission Validation

Validates 50+ invariants after transformations, before emission:

**Categories**:
- Finalization (every symbol has final name)
- Scope integrity (well-formed scopes)
- Name uniqueness (no duplicates in scope)
- View integrity (ViewOnly belongs to view)
- Import/Export (valid imports, no circular deps)
- Type resolution (all types resolvable)
- Overload collision (no arity conflicts)
- Constraint integrity (satisfiable constraints)

**Severity**: ERROR blocks emission, WARNING logs only

---

## 5. Directory Structure

```
SinglePhase/
├── SinglePhaseBuilder.cs        # Main orchestrator
├── BuildContext.cs              # Shared services
├── Load/                        # Phase 1: Reflection
├── Model/                       # Immutable data structures
├── Normalize/                   # Phase 2: Indices
├── Shape/                       # Phase 3: Transformations
├── Renaming/                    # Phase 3.5: Naming service
├── Plan/                        # Phase 4: Import planning
└── Emit/                        # Phase 5: File generation
```

---

## 6. Build Context Services

### Policy (Configuration)

- Type/member name transforms
- Emission filters (internal types, doc comments)
- Branded primitives (int vs number)
- Import style (ES6 vs namespace)

### SymbolRenamer (Naming Service)

Central naming authority:
- Reserve names in scopes
- Apply style transforms
- Resolve conflicts (numeric suffixes)
- Sanitize reserved words
- Track rename decisions

### DiagnosticBag (Error Collection)

Collects errors/warnings/info throughout pipeline. Blocks emission if errors exist.

### Interner (String Deduplication)

Reduces memory via string interning. Typical savings: 30-40%.

### Logger (Progress Reporting)

Optional logging for debugging. Categories: Build, Load, Shape, ViewPlanner, PhaseGate, Emit.

---

## 7. Current Validation Metrics (jumanji9)

### TypeScript Validation Results

Full BCL generation (4,047 types, 130 namespaces, 50,720 members):

**Total TypeScript errors**: **198** (down from 1,471, **-86.5%** reduction)

**Error breakdown**:
- **0 syntax errors** (TS1xxx) - All output is valid TypeScript ✓
- **198 semantic errors** (TS2xxx) - Known .NET/TS impedance

**Semantic error categories**:
- 146 TS2416 (73.7%) - Property covariance (C# allows, TS doesn't)
- 25 TS2417 (12.6%) - Override mismatches (C# virtual/override != TS structural)
- 24 TS2344 (12.1%) - Generic constraints (F-bounded types like `INumber<TSelf>`)
- 2 TS2315 (1.0%) - Array/System.Array shadowing
- 1 TS2440 (0.5%) - Abstract member edge case

**Errors eliminated in recent work**:
- TS2304: 212 → **0** (100%) - "Cannot find name" errors
- TS2420: 579 → **0** (100%) - "Class incorrectly implements interface" errors
- TS2552: 5 → **0** (100%) - "Cannot find name" (different context)
- TS2416: 794 → 146 (81.6% reduction) - Property assignability

**Key fixes**:
- Non-public interface filtering (eliminated TS2420 cascade)
- Cross-namespace qualification (facades, view interfaces, nested types)
- Free type variable detection and demotion
- CLROf<T> primitive lifting for generic constraints
- Recursive nested type analysis in import graph

**Data integrity**: 100% - All reflected types accounted for in emission (verified via completeness checks)

---

## Summary

**SinglePhase pipeline** is a deterministic, pure functional transformation:

1. **Load**: Reflection → SymbolGraph
2. **Normalize**: Build indices
3. **Shape**: 16 transformations
4. **Name Reservation**: Reserve all names
5. **Plan**: Import analysis + validation
6. **Emit**: Generate files

**Core Principles**: Immutability, purity, centralization, stable identity, scoping, validation

**Result**: 100% data integrity, zero data loss, type-safe TypeScript declarations for entire .NET BCL (4,047 types, 130 namespaces, **198 known impedance errors**).

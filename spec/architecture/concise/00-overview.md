# SinglePhase Pipeline Architecture

## System Overview

**tsbindgen** transforms .NET assemblies into TypeScript declaration files with metadata sidecars via System.Reflection.

- **Input**: .NET assembly DLL files
- **Process**: Reflect → analyze → resolve conflicts → plan → emit
- **Output**: TypeScript `.d.ts`, JSON metadata, binding mappings

Enables Tsonic compiler (TypeScript→C# transpiler) to:
- Type-check TypeScript against .NET APIs
- Generate correct C# with proper overload resolution
- Respect CLR semantics (virtual, static, ref/out, constraints)
- Handle explicit interface implementations

**Scale**: 130 BCL namespaces, 4,000+ types, 100% data integrity.

---

## Core Principles

### Single-Pass Processing

Pipeline processes each assembly **once** through sequential phases. No iteration. Each phase returns new immutable SymbolGraph.

### Immutable Data

All structures are **immutable records**:
- `SymbolGraph` → `NamespaceSymbol[]` → `TypeSymbol[]` → `MemberSymbol[]`
- Transformations use `with` expressions
- No mutation - pure functional

### Static Classes with Pure Functions

All logic in **static classes** with **pure functions**:
- No instance state
- No side effects (except I/O)
- Input → Process → Output

```csharp
public static class InterfaceInliner
{
    public static SymbolGraph Inline(BuildContext ctx, SymbolGraph graph)
    {
        // Returns new graph
    }
}
```

### Centralized Services (BuildContext)

All shared state in `BuildContext`:
- **Policy**: Configuration
- **SymbolRenamer**: Central naming authority
- **DiagnosticBag**: Error collection
- **Interner**: String deduplication
- **Logger**: Progress reporting

Created once, passed to all phases.

---

## Pipeline Phases

```
┌─────────────────────────────────────────┐
│  PHASE 1: Load (Reflection)             │
│  - AssemblyLoader (transitive closure)  │
│  - ReflectionReader (System.Reflection) │
│  - InterfaceMemberSubstitution          │
│  Output: SymbolGraph (pure CLR)         │
└─────────────┬───────────────────────────┘
              │
┌─────────────▼───────────────────────────┐
│  PHASE 2: Normalize (Build Indices)     │
│  - SymbolGraph.WithIndices()            │
│  - NamespaceIndex, TypeIndex            │
│  Output: SymbolGraph (with indices)     │
└─────────────┬───────────────────────────┘
              │
┌─────────────▼───────────────────────────┐
│  PHASE 3: Shape (16 Transformations)    │
│  1. GlobalInterfaceIndex                │
│  2. InterfaceDeclIndex                  │
│  3. InterfaceInliner (flatten)          │
│  4. InternalInterfaceFilter             │
│  5. StructuralConformance               │
│  6. ExplicitImplSynthesizer             │
│  7. InterfaceResolver                   │
│  8. DiamondResolver                     │
│  9. BaseOverloadAdder                   │
│  10. OverloadReturnConflictResolver     │
│  11. MemberDeduplicator                 │
│  12. ViewPlanner                        │
│  13. ClassSurfaceDeduplicator           │
│  14. HiddenMemberPlanner                │
│  15. IndexerPlanner                     │
│  16. FinalIndexersPass                  │
│  17. StaticSideAnalyzer                 │
│  18. ConstraintCloser                   │
│  Output: SymbolGraph (TS-ready)         │
└─────────────┬───────────────────────────┘
              │
┌─────────────▼───────────────────────────┐
│  PHASE 3.5: Name Reservation            │
│  - NameReservation.ReserveAllNames      │
│  - Reserve types, members in scopes     │
│  - Apply naming policy, resolve         │
│  Output: SymbolGraph + Rename Decisions │
└─────────────┬───────────────────────────┘
              │
┌─────────────▼───────────────────────────┐
│  PHASE 4: Plan (Imports/Validation)     │
│  - ImportGraph (analyze dependencies)   │
│  - ImportPlanner (plan imports/aliases) │
│  - EmitOrderPlanner (stable order)      │
│  - OverloadUnifier (merge overloads)    │
│  - InterfaceConstraintAuditor           │
│  - PhaseGate (50+ validation rules)     │
│  Output: EmissionPlan (validated)       │
└─────────────┬───────────────────────────┘
              │
┌─────────────▼───────────────────────────┐
│  PHASE 5: Emit (File Generation)        │
│  - SupportTypesEmit (_support/types)    │
│  - InternalIndexEmitter (internal/)     │
│  - FacadeEmitter (facade/)              │
│  - MetadataEmitter (metadata.json)      │
│  - BindingEmitter (bindings.json)       │
│  - ModuleStubEmitter (index.js)         │
│  Output: Files on disk                  │
└─────────────┬───────────────────────────┘
              │
         BuildResult
```

---

## Key Concepts

### StableId: Immutable Identity

**StableId** = assembly-qualified identifier **before name transformations**. Permanent key for rename decisions and CLR bindings.

**Format**:
- **Type**: `{AssemblyName}:{ClrFullName}`
  - Example: `"System.Private.CoreLib:System.Collections.Generic.List\`1"`
- **Member**: `{AssemblyName}:{DeclaringType}::{MemberName}{CanonicalSignature}`
  - Example: `"System.Private.CoreLib:System.Decimal::ToString(IFormatProvider):String"`

**Properties**:
- Immutable, unique, stable, semantic (not metadata token)
- Used for rename keys, cross-assembly refs, bindings, deduplication

**Equality**: Same assembly + declaring type + member name + canonical signature. Metadata token excluded.

### EmitScope: Placement Decisions

Controls **where** member is emitted:

```csharp
public enum EmitScope
{
    ClassSurface,  // Direct on class/interface
    StaticSurface, // Static section
    ViewOnly,      // As_IInterface view property
    Omitted        // Not emitted (tracked in metadata)
}
```

**ClassSurface**: Default for public members
```typescript
class Decimal { ToString(): string; }
```

**ViewOnly**: Explicit interface implementations
```typescript
class Decimal {
    As_IConvertible: { ToBoolean(): boolean; };
}
```

**Omitted**: Intentionally not emitted
- Indexers (TS limitation)
- Generic static members (TS limitation)
- Tracked in `metadata.json`

**Decision Process**:
1. **StructuralConformance** marks ViewOnly if structural impl fails
2. **IndexerPlanner** marks indexers Omitted
3. **ClassSurfaceDeduplicator** demotes duplicates to ViewOnly
4. **PhaseGate** validates EmitScope consistency

### ViewPlanner: Explicit Interface Implementation

TypeScript lacks explicit interface implementations. C# has them:

```csharp
// C# - explicit impl
class Decimal : IConvertible
{
    public override string ToString() => "...";               // Implicit
    bool IConvertible.ToBoolean(IFormatProvider? p) => ...;   // Explicit - ONLY via cast
}
```

**ViewPlanner solution**: Generate **view properties**

```typescript
class Decimal {
    ToString(): string;  // ClassSurface

    As_IConvertible: {   // View property
        ToBoolean(provider: IFormatProvider | null): boolean;
        ToInt32(provider: IFormatProvider | null): int;
    };
}
```

**How it works**:
1. **StructuralConformance** marks ViewOnly when structural impl fails
2. **ViewPlanner** groups ViewOnly by source interface
3. Creates `ExplicitView` with view property name + member list
4. **FacadeEmitter** emits view properties

### Scope-Based Naming

TypeScript naming differs from C#. Need separate scopes:

**Problem**: Multiple members with same name in different contexts

```csharp
// C# - different contexts
class Decimal : IConvertible, IFormattable
{
    string ToString() => "1.0";
    string IConvertible.ToString(IFormatProvider p) => "1.0";
    string IFormattable.ToString(string fmt, IFormatProvider p) => "1.0";
}
```

**Solution**: Separate scopes for each context

```typescript
class Decimal {
    ToString(): string;  // Scope: "type:System.Decimal#instance"
    As_IConvertible: {
        ToString(): string;  // Scope: "view:CoreLib:Decimal:CoreLib:IConvertible#instance"
    };
    As_IFormattable: {
        ToString(): string;  // Scope: "view:CoreLib:Decimal:CoreLib:IFormattable#instance"
    };
}
```

**Scope Formats**:
- **Namespace**: `ns:System.Collections.Generic:internal`
- **Class Instance**: `type:System.Decimal#instance`
- **Class Static**: `type:System.Decimal#static`
- **View Instance**: `view:CoreLib:System.Decimal:CoreLib:IConvertible#instance`
- **View Static**: `view:CoreLib:System.Decimal:CoreLib:IConvertible#static`

**Benefits**: Independent naming per context, no artificial suffixes, preserves original names.

### PhaseGate: Pre-Emission Validation

Comprehensive validation layer with 50+ invariants. Runs **after transformations**, **before emission**.

**Purpose**: Fail fast, prevent malformed output, document invariants, enable safe refactoring.

**Validation Categories**:
1. **Finalization** (PG_FIN_001-009) - Every symbol has final TS name
2. **Scope Integrity** (PG_SCOPE_001-004) - Well-formed scopes
3. **Name Uniqueness** (PG_NAME_001-005) - No duplicates in scope
4. **View Integrity** (PG_INT_001-003) - ViewOnly members belong to views
5. **Import/Export** (PG_IMPORT/EXPORT/API_001-002) - Valid imports, no internal leaks
6. **Type Resolution** (PG_LOAD/TYPEMAP_001) - All types resolved
7. **Overload Collision** (PG_OL_001-002) - No collisions
8. **Constraint Integrity** (PG_CNSTR_001-004) - Constraints satisfied

**Severity**: ERROR blocks emission, WARNING logged, INFO diagnostic.

**Output**: Console log, `.diagnostics.txt`, `validation-summary.json`

**Integration**:
```csharp
PhaseGate.Validate(ctx, graph, imports, constraintFindings);
if (ctx.Diagnostics.HasErrors()) {
    return new BuildResult { Success = false };
}
```

---

## Directory Structure

```
SinglePhase/
├── SinglePhaseBuilder.cs        # Main orchestrator
├── BuildContext.cs              # Services container
│
├── Load/                        # Phase 1: Reflection
│   ├── AssemblyLoader.cs
│   ├── ReflectionReader.cs
│   ├── TypeReferenceFactory.cs
│   ├── InterfaceMemberSubstitution.cs
│   └── DeclaringAssemblyResolver.cs
│
├── Model/                       # Immutable data
│   ├── SymbolGraph.cs
│   ├── Symbols/ (Namespace, Type, Member)
│   ├── Types/ (TypeReference variants)
│   └── AssemblyKey.cs
│
├── Normalize/                   # Phase 2: Indices
│   ├── SignatureNormalization.cs
│   ├── OverloadUnifier.cs
│   └── NameReservation.cs
│
├── Shape/                       # Phase 3: Transformations (18 passes)
│   ├── GlobalInterfaceIndex.cs
│   ├── InterfaceDeclIndex.cs
│   ├── InterfaceInliner.cs
│   ├── InternalInterfaceFilter.cs
│   ├── StructuralConformance.cs
│   ├── ExplicitImplSynthesizer.cs
│   ├── InterfaceResolver.cs
│   ├── DiamondResolver.cs
│   ├── BaseOverloadAdder.cs
│   ├── OverloadReturnConflictResolver.cs
│   ├── MemberDeduplicator.cs
│   ├── ViewPlanner.cs
│   ├── ClassSurfaceDeduplicator.cs
│   ├── HiddenMemberPlanner.cs
│   ├── IndexerPlanner.cs
│   ├── FinalIndexersPass.cs
│   ├── StaticSideAnalyzer.cs
│   └── ConstraintCloser.cs
│
├── Renaming/                    # Phase 3.5: Naming
│   ├── SymbolRenamer.cs
│   ├── StableId.cs
│   ├── RenameScope.cs
│   ├── ScopeFactory.cs
│   ├── RenameDecision.cs
│   ├── NameReservationTable.cs
│   └── TypeScriptReservedWords.cs
│
├── Plan/                        # Phase 4: Planning + Validation
│   ├── ImportGraph.cs
│   ├── ImportPlanner.cs
│   ├── EmitOrderPlanner.cs
│   ├── InterfaceConstraintAuditor.cs
│   ├── PhaseGate.cs
│   ├── TsAssignability.cs
│   ├── TsErase.cs
│   ├── PathPlanner.cs
│   └── Validation/ (Core, Names, Views, Scopes, Types, etc.)
│
└── Emit/                        # Phase 5: File generation
    ├── SupportTypesEmitter.cs
    ├── InternalIndexEmitter.cs
    ├── FacadeEmitter.cs
    ├── MetadataEmitter.cs
    ├── BindingEmitter.cs
    ├── ModuleStubEmitter.cs
    ├── Printers/ (Class, Interface, Enum, Method, Property, etc.)
    ├── TypeRefPrinter.cs
    └── TypeNameResolver.cs
```

**Design**: Load (pure CLR) → Model (shared data) → Normalize (indices) → Shape (16 transforms) → Renaming (central naming) → Plan (imports + validation) → Emit (string generation)

---

## BuildContext Services

### Policy (Configuration)

```csharp
public sealed class GenerationPolicy
{
    // Naming
    public NameTransformStrategy TypeNameTransform { get; init; }
    public NameTransformStrategy MemberNameTransform { get; init; }
    public Dictionary<string, string> ExplicitMap { get; init; }

    // Filters
    public bool IncludeInternalTypes { get; init; }
    public bool EmitDocumentation { get; init; }
    public bool UseBrandedPrimitives { get; init; }

    // Import style
    public ImportStyle ImportStyle { get; init; }
}
```

### SymbolRenamer (Central Naming Authority)

**Responsibilities**:
1. Reserve names in scopes
2. Apply style transforms (PascalCase, camelCase)
3. Resolve conflicts via numeric suffixes
4. Sanitize reserved words (class → class_)
5. Track rename decisions with provenance

**Key Methods**:
```csharp
void ReserveTypeName(StableId id, string requested, NamespaceScope scope, string reason);
void ReserveMemberName(StableId id, string requested, RenameScope scope, string reason, bool isStatic);
string GetFinalTypeName(TypeSymbol type, NamespaceArea area);
string GetFinalMemberName(StableId id, RenameScope scope);
```

**Scope Separation**: Class surface vs view surfaces - independent naming.

**Decision Recording**:
```csharp
public sealed record RenameDecision
{
    public StableId Id { get; init; }
    public string Requested { get; init; }
    public string Final { get; init; }
    public string From { get; init; }
    public string Reason { get; init; }
    public string Strategy { get; init; }  // "NumericSuffix", "Sanitize"
    public string ScopeKey { get; init; }
}
```

Emitted to `bindings.json` for runtime binding.

### DiagnosticBag (Error Collection)

```csharp
void Error(string code, string message);
void Warning(string code, string message);
void Info(string code, string message);
bool HasErrors();
IReadOnlyList<Diagnostic> GetAll();
```

**Diagnostic**: Severity (Error/Warning/Info), Code ("PG_FIN_003"), Message, Location.

### Interner (String Deduplication)

```csharp
string Intern(string value);
```

Reduces memory by 30-40% for large BCL assemblies.

### Logger (Progress Reporting)

```csharp
void Log(string category, string message);
```

**Categories**: "Build", "Load", "Shape", "ViewPlanner", "PhaseGate", "Emit"

**Filtering**:
```csharp
var ctx = BuildContext.Create(policy, logger, verboseLogging: true);
// OR
var logCategories = new HashSet<string> { "PhaseGate", "ViewPlanner" };
var ctx = BuildContext.Create(policy, logger, verboseLogging: false, logCategories);
```

---

## Summary

**SinglePhase Pipeline**: Deterministic pure functional transformation from .NET → TypeScript.

**Flow**: Load (Reflection → SymbolGraph) → Normalize (Indices) → Shape (16 transforms) → Name Reservation (SymbolRenamer) → Plan (Imports + PhaseGate) → Emit (TypeScript files + metadata)

**Principles**: Immutability, purity, centralization, StableId identity, scope-based naming, validation gates.

**Result**: 100% data integrity, zero data loss, type-safe TypeScript for entire .NET BCL.

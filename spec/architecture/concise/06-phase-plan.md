# Phase PLAN: Import Planning and Cross-Assembly Resolution

## Overview

Analyzes dependencies, plans imports/exports, determines emission order, audits constraints, validates via PhaseGate.

**Input**: `SymbolGraph` (fully named)
**Output**: `EmissionPlan` (SymbolGraph + ImportPlan + EmitOrder)
**Sub-phases**: ImportGraph → ImportPlanner → EmitOrderPlanner → OverloadUnifier → InterfaceConstraintAuditor → PhaseGate

---

## ImportGraph.cs

### Purpose
Builds dependency graph showing which namespaces use types from other namespaces.

### Method: Build(BuildContext, SymbolGraph) → ImportGraph

**Algorithm:**
1. For each namespace:
   - For each type:
     - Scan base class, interfaces, members (return types, parameters)
     - Extract foreign type references
     - Record: `(currentNamespace, foreignNamespace, foreignType)`
2. Build graph:
   - Nodes: namespaces
   - Edges: dependencies
3. Return `ImportGraph` with dependency map

### Method: GetOpenGenericClrKey(NamedTypeReference) → string

**Purpose:** Construct open generic CLR key from NamedTypeReference.

**Algorithm:**
1. Start with `ref.Name`
2. If `ref.TypeArguments.Count > 0`:
   - Append backtick + arity: `List\`1`
3. Prepend namespace: `System.Collections.Generic.List\`1`
4. Return CLR key (NO assembly-qualified type args)

**Critical:** Returns OPEN generic form (e.g., `List\`1`), not closed (`List\`1[[System.String]]`). Prevents assembly-qualified garbage in imports.

**Related:** TypeReferenceFactory.CreateNamed() uses same logic.

### Method: ClrFullNameToNamespace(string clrFullName) → string

**Purpose:** Extract namespace from CLR full name.

**Algorithm:**
1. Find last `.` in `clrFullName`
2. Substring before `.` is namespace
3. Handle nested types (contains `+`): still use last `.`
4. Return namespace string

**Example:** `System.Collections.Generic.List\`1` → `System.Collections.Generic`

---

## ImportPlanner.cs

### Purpose
Plans which types to import, which to export, which to alias.

### Method: PlanImports(BuildContext, SymbolGraph, ImportGraph) → ImportPlan

**Algorithm:**
1. **Determine Exports**:
   - For each namespace:
     - All public types are exported
     - Internal types not exported (unless policy override)

2. **Determine Imports**:
   - For each namespace:
     - Get dependencies from ImportGraph
     - For each foreign namespace:
       - Add import for all used types
       - Determine import path (relative vs absolute)

3. **Determine Aliases**:
   - If type name collision across namespaces → create alias
   - Alias format: `{TypeName}_{NamespaceSuffix}`

4. **Unresolved Types (Cross-Assembly)**:
   - Collect types used but not in SymbolGraph
   - Call `DeclaringAssemblyResolver.ResolveBatch()` to find declaring assemblies
   - Group by assembly
   - Log for future ambient stub generation

5. Return `ImportPlan`

### Method: GetTypeScriptNameForExternalType(string clrFullName, SymbolGraph) → string

**Purpose:** Compute TypeScript name for external (cross-assembly) types not in current graph.

**Algorithm:**
1. Parse CLR full name → namespace + simple name
2. Apply same naming transforms as internal types:
   - Strip backtick arity: `List\`1` → `List_1`
   - Apply policy transforms (PascalCase, etc.)
   - Sanitize reserved words
3. Return TS name

**Used by:** External type reference emission (future ambient stub generation)

---

## EmitOrderPlanner.cs

### Purpose
Determines stable emission order for namespaces to ensure deterministic output.

### Method: PlanOrder(BuildContext, SymbolGraph, ImportGraph) → List<string>

**Algorithm:**
1. Build dependency graph (namespace → dependencies)
2. Topological sort:
   - DFS from roots
   - Emit dependencies before dependents
   - Break cycles by alphabetical order
3. Within same level: alphabetical sort
4. Return ordered namespace list

**Determinism**: Same input always produces same output (critical for reproducibility).

---

## OverloadUnifier.cs

### Purpose
Unifies method overloads that TypeScript cannot distinguish (already covered in Phase Normalize).

### Method: UnifyOverloads(BuildContext, SymbolGraph) → SymbolGraph

**Algorithm:**
1. For each type:
   - Group methods by (Name, EmitScope, IsStatic)
   - For each group:
     - Build TS signatures (erase ref/out/constraints)
     - Group by TS signature
     - If multiple CLR sigs → same TS sig: keep first, mark rest `Omitted`
2. Return new graph

---

## InterfaceConstraintAuditor.cs

### Purpose
Audits constructor constraints for (Type, Interface) pairs.

### Method: Audit(BuildContext, SymbolGraph) → ConstraintFindings

**Algorithm:**
1. For each type:
   - For each interface:
     - Check if interface has `new()` constraint
     - Check if type has parameterless constructor
     - If constraint not satisfied: record finding
2. Return `ConstraintFindings`

**Used by:** PhaseGate validation (PG_CNSTR_* checks)

---

## TsAssignability.cs

### Purpose
TypeScript assignability rules (for structural conformance checking).

### Method: IsMethodAssignable(MethodSig class, MethodSig iface) → bool

**Algorithm:**
1. Compare parameter counts
2. For each parameter:
   - Check contravariant assignability (interface param assignable TO class param)
3. Check return type (covariant): class return assignable TO interface return
4. Return true if all checks pass

**TS Rule:** Method on class is assignable to interface method if:
- Same arity
- Class params are contravariant to interface params
- Class return is covariant to interface return

### Method: IsPropertyAssignable(PropertySig class, PropertySig iface) → bool

**Algorithm:**
1. Check type assignability (class type assignable TO interface type)
2. If interface is readonly: class can be readwrite or readonly
3. If interface is readwrite: class MUST be readwrite
4. Return true if all checks pass

---

## TsErase.cs

### Purpose
Erase CLR-specific info to simulate TypeScript signatures (for assignability checks).

### Method: EraseToTsSignature(MethodSymbol) → TsMethodSig

**Algorithm:**
1. Strip ref/out from parameters
2. Strip generic constraints
3. Keep only: name, parameter types, return type
4. Return erased signature

**Used by:** TsAssignability for structural conformance

---

## PathPlanner.cs

### Purpose
Plans file paths for emitted namespaces.

### Method: PlanPaths(Policy, List<string> namespaces) → Dictionary<string, PathInfo>

**Algorithm:**
1. For each namespace:
   - Compute directory path: replace `.` with `/`
   - Compute file names:
     - `{ns}/internal/index.d.ts`
     - `{ns}/index.d.ts` (facade)
     - `{ns}/metadata.json`
     - `{ns}/bindings.json`
     - `{ns}/index.js`
2. Return path map

---

## PhaseGate.cs

### Purpose
Comprehensive pre-emission validation (detailed in 07-phasegate.md).

### Method: Validate(BuildContext, SymbolGraph, ImportPlan, ConstraintFindings)

**Validation Categories:**
1. Finalization (PG_FIN_* ) - TsEmitName set
2. Scopes (PG_SCOPE_*) - Well-formed scopes
3. Names (PG_NAME_*) - Uniqueness
4. Views (PG_INT_*) - View integrity
5. Imports/Exports (PG_IMPORT_*, PG_EXPORT_*, PG_API_*) - Valid imports
6. Types (PG_LOAD_*, PG_TYPEMAP_*) - Type resolution
7. Overloads (PG_OL_*) - No collisions
8. Constraints (PG_CNSTR_*) - Constraint satisfaction

**Total:** 50+ validation rules

**Output:** Diagnostics in `ctx.Diagnostics` (ERROR blocks emission)

---

## DeclaringAssemblyResolver.cs

### Purpose
Resolves CLR type full names → declaring assembly names (for cross-assembly dependencies).

### Method: ResolveBatch(IReadOnlySet<string> clrKeys) → Dictionary<string, string>

**Algorithm:**
1. For each CLR key:
   - Create placeholder `AssemblyName` with key as name
   - Call `LoadContext.LoadFromAssemblyName()`
   - MetadataLoadContext resolves via PathAssemblyResolver
   - Extract `assembly.GetName().Name` as declaring assembly
2. Return map: `clrKey → assemblyName`

**Used by:** ImportPlanner for unresolved external types

### Method: GroupByAssembly(Dictionary<string, string>) → Dictionary<string, List<string>>

**Purpose:** Groups types by their declaring assembly (for diagnostics).

**Algorithm:**
1. Invert map: `assemblyName → List<typeClrKeys>`
2. Return grouped map

---

## Integration Flow

```
SymbolGraph (fully named)
  ↓
ImportGraph.Build()
  → Scan all type references
  → Build dependency graph
  ↓
ImportPlanner.PlanImports()
  → Determine exports (public types)
  → Determine imports (foreign types)
  → Determine aliases (name collisions)
  → Resolve unresolved types (cross-assembly)
    → DeclaringAssemblyResolver.ResolveBatch()
  ↓
EmitOrderPlanner.PlanOrder()
  → Topological sort (dependencies first)
  → Alphabetical within level
  ↓
OverloadUnifier.UnifyOverloads()
  → Erase to TS signatures
  → Merge indistinguishable overloads
  ↓
InterfaceConstraintAuditor.Audit()
  → Check new() constraints
  → Record findings
  ↓
PhaseGate.Validate()
  → 50+ validation checks
  → Record ERROR/WARNING/INFO
  → Block emission if errors
  ↓
EmissionPlan (if no errors)
```

---

## Key Algorithms

### Open Generic CLR Key Construction

**Problem:** Need stable keys for generic types without assembly-qualified type args.

**Solution:**
1. Use OPEN generic form: `List\`1` (not `List\`1[[System.String]]`)
2. Format: `{Namespace}.{SimpleName}\`{Arity}`
3. Example: `System.Collections.Generic.List\`1`

**Benefits:**
- Stable across different type instantiations
- No assembly-qualified garbage
- Matches TypeReferenceFactory.CreateNamed() logic

### Topological Sort for Emission Order

**Problem:** Must emit dependencies before dependents.

**Solution:**
1. Build directed graph: namespace → dependencies
2. DFS from roots (nodes with no dependencies)
3. Emit when all dependencies emitted
4. Break cycles: alphabetical order
5. Within same level: alphabetical sort

**Benefits:** Deterministic, dependency-safe

### Cross-Assembly Resolution

**Problem:** Types used but not in current SymbolGraph (external assemblies).

**Solution:**
1. Collect unresolved CLR keys
2. Call `DeclaringAssemblyResolver.ResolveBatch()`
3. MetadataLoadContext resolves to declaring assemblies
4. Group by assembly
5. Log for future ambient stub generation

**Benefits:** Enables partial BCL generation, identifies external dependencies

---

## Summary

**Plan phase responsibilities:**
1. Build import dependency graph
2. Plan imports, exports, aliases
3. Resolve cross-assembly type references
4. Determine stable emission order
5. Unify indistinguishable overloads
6. Audit interface constructor constraints
7. Validate everything via PhaseGate (50+ rules)

**Output:** EmissionPlan (if validation passes) or diagnostics (if errors)

**Key design decisions:**
- Open generic CLR keys (no assembly-qualified args)
- Topological sort for deterministic order
- Cross-assembly resolution via MetadataLoadContext
- Comprehensive validation before emission
- DeclaringAssemblyResolver enables future type-forwarding

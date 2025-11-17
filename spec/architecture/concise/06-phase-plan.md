# Phase 6: Plan - Import Planning and Final Validation

## Overview

**Plan phase** is final preparation before emission. Builds cross-namespace dependency graph, validates entire symbol graph, plans imports/emission order, enforces all invariants via PhaseGate.

**Key Responsibilities**:
- Build import dependency graph between namespaces
- Detect foreign type references, plan imports
- Compute relative import paths for TS modules
- Determine topological emission order
- Audit interface constraint losses
- Validate 50+ correctness rules (PhaseGate)

**Input**: Fully-shaped `SymbolGraph` from Shape phase
**Output**: `ImportPlan`, `EmitOrder`, validation reports

---

## File: ImportPlanner.cs

### Purpose
Plans import statements and aliasing for TS declarations. Generates import/export statements based on dependency graph, handles namespace-to-module mapping with collision resolution.

### Method: `PlanImports(BuildContext, SymbolGraph, ImportGraphData) -> ImportPlan`

**Algorithm**:
1. Create empty `ImportPlan` with three dictionaries:
   - `NamespaceImports` - namespace → list of import statements
   - `NamespaceExports` - namespace → list of export statements
   - `ImportAliases` - namespace → alias dictionary
2. For each namespace:
   - Call `PlanNamespaceImports` to analyze dependencies
   - Call `PlanNamespaceExports` to catalog public types
3. Return complete `ImportPlan`

### Method: `PlanNamespaceImports`

**Foreign type detection algorithm**:
1. **Get dependencies**: Look up namespace in `importGraph.NamespaceDependencies`
2. **For each target namespace**:
   - Filter `CrossNamespaceReferences` where source is current, target is dependency
   - Extract CLR full names for referenced types
   - Sort by CLR name (deterministic)
3. **Determine import path**: Call `PathPlanner.GetSpecifier(sourceNs, targetNs)` → relative path like `"../System.Collections/internal/index"`
4. **Check name collisions**:
   - Get TS emit name from Renamer
   - Call `DetermineAlias` to check if alias needed
   - Create `TypeImport(TypeName, Alias)`
5. **Build import statement**:
   - Group all imports from same target
   - Create `ImportStatement(ImportPath, TargetNamespace, TypeImports)`
   - Add to `plan.NamespaceImports[ns.Name]`

**Alias assignment**: Needed when:
- Name collision detected (same TS name from different namespace)
- Policy requires it (`ctx.Policy.Modules.AlwaysAliasImports == true`)

Alias format: `{TypeName}_{TargetNamespaceShortName}`

### Method: `PlanNamespaceExports`

**Algorithm**:
1. Iterate all types in namespace
2. Filter to public types only
3. For each public type:
   - Get final TS name via `ctx.Renamer.GetFinalTypeName`
   - Determine export kind based on type kind
   - Create `ExportStatement(ExportName, ExportKind)`
4. Add to `plan.NamespaceExports[ns.Name]`

**Export kind mapping**:
- `Class` → `ExportKind.Class`
- `Interface` → `ExportKind.Interface`
- `Struct` → `ExportKind.Interface` (structs emit as TS interfaces)
- `Enum` → `ExportKind.Enum`
- `Delegate` → `ExportKind.Type` (delegates emit as type aliases)

### Method: `GetTypeScriptNameForExternalType(string clrFullName) -> string`

**Purpose**: Convert CLR full name to TS emit name for external types (types from other namespaces not in local graph).

**Algorithm**:
1. Extract simple name from full CLR name: `"System.Collections.Generic.IEnumerable`1"` → `"IEnumerable`1"`
2. Sanitize backtick to underscore: `"IEnumerable`1"` → `"IEnumerable_1"`
3. Handle nested types (replace `+` with `$`): `"Dictionary_2+Enumerator"` → `"Dictionary_2$Enumerator"`
4. Check TS reserved words: `TypeScriptReservedWords.Sanitize`

**Examples**:
- `"System.Collections.Generic.IEnumerable`1"` → `"IEnumerable_1"`
- `"System.Collections.Generic.Dictionary`2+Enumerator"` → `"Dictionary_2$Enumerator"`
- `"System.Type"` → `"Type_"`
- `"System.Func`3"` → `"Func_3"`

**Critical for**: Cross-namespace generic type imports

---

## File: ImportGraph.cs

### Purpose
Builds cross-namespace dependency graph for import planning. Analyzes type references throughout symbol graph to determine which namespaces need imports from which others.

### Method: `Build(BuildContext, SymbolGraph) -> ImportGraphData`

**Algorithm**:
1. Create empty `ImportGraphData`:
   - `NamespaceDependencies` - maps namespace → set of dependent namespaces
   - `NamespaceTypeIndex` - maps namespace → set of type full names (legacy set-based)
   - `ClrFullNameToNamespace` - O(1) lookup: CLR name → namespace
   - `CrossNamespaceReferences` - list of all foreign type references
   - `UnresolvedClrKeys` - types not found in graph
   - `UnresolvedToAssembly` - unresolved type → assembly mapping
2. **Build namespace type index**: Call `BuildNamespaceTypeIndex` to catalog all public types (creates TWO lookups: set-based and map-based)
3. **Analyze dependencies**: For each namespace, call `AnalyzeNamespaceDependencies` - recursively scans all type references, tracks unresolved types
4. Return complete `ImportGraphData`

### Method: `BuildNamespaceTypeIndex`

**Algorithm**:
1. For each namespace:
   - Get all public types
   - Extract CLR full names (backtick form: `"IEnumerable`1"`)
   - Add to BOTH indexes:
     - `NamespaceTypeIndex[ns.Name].Add(type.ClrFullName)` (set-based, legacy)
     - `ClrFullNameToNamespace[type.ClrFullName] = ns.Name` (map-based, O(1) lookup)

**Dual indexing**:
- **Set-based**: Legacy, used for set operations
- **Map-based**: O(1) hash lookup for `FindNamespaceForType` (critical for BCL with 4,000+ types)

### Method: `AnalyzeNamespaceDependencies` - **UPDATED**

**Comprehensive scanning algorithm**:

For each **public type**:
1. **TS2304 FIX**: Call `AnalyzeTypeAndNestedRecursively` to analyze type AND all public nested types recursively
   - Previously only analyzed top-level types
   - Now processes nested types (e.g., `ImmutableArray<T>.Builder`)
   - Ensures nested type members scanned for cross-namespace dependencies

**Result**:
- `dependencies` set contains all foreign namespace names
- `CrossNamespaceReferences` has detailed reference records

### Method: `AnalyzeTypeAndNestedRecursively` - **NEW**

**Recursive type analysis** (including nested types):

For given type:
1. **Base class analysis**:
   - Call `CollectTypeReferences(type.BaseType)`
   - Recursively finds ALL referenced types (including generic arguments)
   - For each foreign type, add to dependencies, create `CrossNamespaceReference`
   - Reference kind: `ReferenceKind.BaseClass`

2. **Interface analysis**:
   - For each interface, call `CollectTypeReferences`
   - Reference kind: `ReferenceKind.Interface`

3. **Generic constraint analysis**:
   - For each type generic parameter with constraints
   - Call `CollectTypeReferences` on each constraint
   - Reference kind: `ReferenceKind.GenericConstraint`

4. **Member analysis**:
   - Call `AnalyzeMemberDependencies` to scan all members
   - Analyzes methods, properties, fields, events, constructors

5. **Nested type recursion**:
   - For each **public** nested type: call `AnalyzeTypeAndNestedRecursively` recursively
   - Ensures deeply nested types fully analyzed (e.g., `Outer<T>.Middle.Inner`)

**Why needed**: Nested type members can reference cross-namespace types. Without recursive analysis, imports were missing (e.g., `ImmutableArray<T>.Builder.AddRange(IEnumerable<T>)` needs `System.Collections.Generic` import).

**Impact**: Fixed TS2304 errors from nested types referencing cross-namespace types

### Method: `CollectTypeReferences`

**Recursive type tree traversal** - finds ALL foreign types.

**Algorithm by TypeReference kind**:

1. **NamedTypeReference**:
   - Find namespace: `FindNamespaceForType`
   - Get open generic CLR key: `GetOpenGenericClrKey(named)`
   - **INVARIANT GUARD**: If `clrKey.Contains('[')` or `clrKey.Contains(',')` → ERROR (assembly-qualified key detected)
   - Add to collected: `collected.Add((clrKey, ns))`
   - **Track unresolved**: If `ns == null`, add to `UnresolvedClrKeys`
   - **Recurse into type arguments**: For `List<Dictionary<K, V>>`, recursively processes Dictionary, K, V

2. **NestedTypeReference**:
   - Find namespace: `FindNamespaceForType`
   - Get open generic CLR key from full reference
   - **INVARIANT GUARD**: Same check as NamedTypeReference
   - Add to collected, track unresolved
   - **Recurse into type arguments** of nested type

3. **ArrayTypeReference**: Recurse into element type

4. **PointerTypeReference / ByRefTypeReference**: Recurse into pointee/referenced type (TS doesn't have pointers/refs, erase to underlying)

5. **GenericParameterReference**: Skip (generic parameters are local, don't need imports)

**Why recursive**: Generic arguments can be complex (`Dictionary<string, List<MyClass>>` needs Dictionary, List, AND MyClass)

### Method: `FindNamespaceForType(SymbolGraph, ImportGraphData, TypeReference) -> string?`

**Namespace lookup**:
1. Get normalized CLR lookup key: `GetClrLookupKey(typeRef)` (returns null for generic parameters, placeholders)
2. **O(1) dictionary lookup**:
   ```csharp
   if (graphData.ClrFullNameToNamespace.TryGetValue(clrKey, out var ns))
       return ns;
   ```
3. Return null if not found (external type)

**Why null is valid**: Type might be from external assembly, built-in TS type, type-forwarded, or different assembly version

### Method: `GetOpenGenericClrKey(NamedTypeReference) -> string` - **UPDATED**

**Purpose**: Construct open generic CLR key from NamedTypeReference. Ensures generic types use open form (`List`1`) not constructed form with assembly-qualified type arguments.

**Algorithm**:
0. **TS2304 FIX - Nested type special handling**:
   - If `FullName.Contains('+')`: Nested type detected
   - Use `FullName` directly (already has correct CLR format with `+` separator)
   - Strip assembly qualification if present
   - Return (skip reconstruction)
   - **Why**: For nested types, `Name` is just child part (e.g., `"Builder"`), reconstruction would give wrong result

1. Extract components: `ns`, `name`, `arity`
2. Validate inputs (defensive): If namespace/name empty, fallback to `FullName`
3. **Strip assembly qualification from name**: If `name.Contains(',')`, truncate at comma
4. **Non-generic path**: If `arity == 0`, return `"{ns}.{name}"`
5. **Generic path**:
   - Strip backtick from name if present: `name.Substring(0, name.IndexOf('`'))`
   - Reconstruct with backtick arity: `"{ns}.{nameWithoutArity}`{arity}"`

**Examples**:
- `IEnumerable<T>` → `System.Collections.Generic.IEnumerable`1`
- `Dictionary<K,V>` → `System.Collections.Generic.Dictionary`2`
- `Exception` → `System.Exception`
- ****: `ImmutableArray\`1+Builder` → `System.Collections.Immutable.ImmutableArray\`1+Builder` (nested type)

**Why critical**: Without proper key construction, lookups fail → no import → TS2304 error

**Impact**: Fixed nested type import lookups - prevented TS2304 errors from nested types

---

## File: EmitOrderPlanner.cs

### Purpose
Plans stable, deterministic emission order for all symbols. Ensures reproducible .d.ts files using `Renamer.GetFinalTypeName` for sorting.

### Method: `PlanOrder(SymbolGraph) -> EmitOrder`

**Algorithm**:
1. Create empty list of `NamespaceEmitOrder`
2. **Sort namespaces** alphabetically
3. For each namespace:
   - Call `OrderTypes` to sort types
   - Create `NamespaceEmitOrder(Namespace, OrderedTypes)`
4. Return `EmitOrder` with ordered namespaces

### Method: `OrderTypes(IReadOnlyList<TypeSymbol>) -> List<TypeEmitOrder>`

**Stable deterministic sorting**:

**Primary sort keys (in order)**:
1. **Kind sort order** (`GetKindSortOrder`):
   - Enums first (0), Delegates (1), Interfaces (2), Structs (3), Classes (4), Static namespaces last (5)
2. **Final TS name** from `ctx.Renamer.GetFinalTypeName(type)` (uses finalized, post-collision name)
3. **Arity** (generic parameter count)

**For each type**:
- Recursively order nested types: `OrderTypes(type.NestedTypes)`
- Order members: `OrderMembers(type)`
- Create `TypeEmitOrder(Type, OrderedMembers, OrderedNestedTypes)`

**Why this ordering**:
- Forward reference safe: Enums/delegates can be used before defined
- Interfaces before structs/classes: Common TS pattern
- Alphabetical by final name: Predictable, git-friendly

### Method: `OrderMembers(TypeSymbol) -> MemberEmitOrder`

**Member category ordering**:
1. Constructors
2. Fields
3. Properties
4. Events
5. Methods

**Within each category, sort by**:
1. **IsStatic**: Instance first, then static
2. **Final TS member name** via `ctx.Renamer.GetFinalMemberName` (must compute proper `EmitScope` for renaming context)
3. **Arity** (for methods): Method-level generic parameter count
4. **Canonical signature** (for overloads): From `StableId.CanonicalSignature`

**Filtering**: Only include `EmitScope == ClassSurface` or `StaticSurface` (excludes view-only members)

---

## File: PathPlanner.cs

### Purpose
Plans module specifiers for TS imports. Generates relative paths based on source/target namespaces and emission area.

### Method: `GetSpecifier(string sourceNamespace, string targetNamespace) -> string`

**Relative path computation**:

**Input**: Source and target namespace names (empty for root)

**Output**: Relative module specifier

**Path generation rules**:
1. **Determine if root**:
   - Root uses `_root` directory name
2. **Compute target path**:
   - If target is root: `targetDir = "_root"`, `targetFile = "index"`
   - If target is named: `targetDir = targetNamespace`, `targetFile = "internal/index"`
3. **Compute relative path**:
   - Source is root → Root: `./_root/index`
   - Source is root → Non-root: `./{targetNamespace}/internal/index`
   - Source is non-root → Root: `../_root/index`
   - Source is non-root → Non-root: `../{targetNamespace}/internal/index"`

**Why always `internal/index`**:
- Public API (`index.d.ts`) re-exports from internal
- Imports need full type definitions
- Consistent import paths

---

## File: InterfaceConstraintAuditor.cs

### Purpose
Audits constructor constraint loss per (Type, Interface) pair. Detects when TS loses C# `new` constraint information. Prevents duplicate diagnostics for view members by auditing at interface implementation level.

### Method: `Audit(BuildContext, SymbolGraph) -> InterfaceConstraintFindings`

**Algorithm**:
1. Create findings builder
2. For each namespace → type:
   - Skip if no interfaces
   - For each interface reference:
     - Resolve interface TypeSymbol
     - Check constraints: `CheckInterfaceConstraints`
     - If finding detected, add to builder
3. Return `InterfaceConstraintFindings`

**Why (Type, Interface) pairs**: Same interface implemented by multiple types → separate findings. Same type implementing multiple interfaces → separate findings. Prevents finding duplication when multiple view members exist.

### Method: `CheckInterfaceConstraints`

**Constraint loss detection**:
1. **Skip if interface has no generic parameters**
2. **For each generic parameter**:
   - Check `SpecialConstraints` flags for `DefaultConstructor` bit
   - If `(gp.SpecialConstraints & GenericParameterConstraints.DefaultConstructor) != 0`:
     - **Constructor constraint loss detected**
3. **Create finding**:
   ```csharp
   new InterfaceConstraintFinding {
       ImplementingTypeStableId = implementingType.StableId,
       InterfaceStableId = interfaceType.StableId,
       LossKind = ConstraintLossKind.ConstructorConstraintLoss,
       GenericParameterName = gp.Name,
       ...
   }
   ```

**What is constructor constraint loss**:
- C# `new` constraint guarantees type has parameterless constructor
- TS has no equivalent
- Information lost in TS declarations
- Must be tracked separately for runtime binding
- PhaseGate emits PG_CT_001 diagnostic

---

## File: TsAssignability.cs

### Purpose
TS assignability checking for erased type shapes. Implements simplified TS structural typing rules to validate interface implementations satisfy contracts in emitted TS world.

### Method: `IsAssignable(TsTypeShape source, TsTypeShape target) -> bool`

**TS structural typing rules**:
1. **Exact match**: If `source.Equals(target)` → true
2. **Unknown type**: Conservative (assume compatible)
3. **Type parameter compatibility**: Match by name (`T` compatible with `T`)
4. **Array covariance**: TS arrays are readonly in model → covariant (`string[]` assignable to `object[]`)
5. **Generic application**: Base generic must match, type arguments checked pairwise (invariant)
6. **Named type widening**: Call `IsWideningConversion`

### Method: `IsWideningConversion(string sourceFullName, string targetFullName) -> bool`

**Known widening conversions**:
1. **Same type**: `sourceFullName == targetFullName` → true
2. **Numeric type widening**: All numeric types widen to each other (all map to `number` brand)
3. **Everything widens to Object**: `targetFullName == "System.Object"` → true
4. **ValueType widens to Object**: `sourceFullName == "System.ValueType" && targetFullName == "System.Object"` → true

**What this enables**: Validates covariant return types, checks overridden methods satisfy base contracts, detects breaking changes

### Method: `IsMethodAssignable(TsMethodSignature source, TsMethodSignature target) -> bool`

**Method signature compatibility**:
1. **Name match**: `source.Name != target.Name` → false
2. **Arity match**: Generic parameter count must match
3. **Parameter count match**
4. **Return type covariance**: `IsAssignable(source.ReturnType, target.ReturnType)` (source return can be subtype)
5. **Parameter type checking**: Invariant for safety (real TS uses contravariance, but we're stricter)

---

## File: TsErase.cs

### Purpose
Erases CLR-specific details to produce TS-level signatures. Used for assignability checking in PhaseGate. Strips C# concepts (ref/out, pointers) that don't exist in TS.

### Method: `EraseMember(MethodSymbol) -> TsMethodSignature`

**Method erasure**:
1. Take final TS name: `method.TsEmitName`
2. Take arity: `method.Arity`
3. **Erase each parameter type**: Map parameters → `EraseType(p.Type)` (removes ref/out modifiers)
4. **Erase return type**: `EraseType(method.ReturnType)`

**Example**:
```csharp
// C#: public ref int GetValue(ref string s, out int x)
// After erasure:
TsMethodSignature(
    Name: "GetValue", Arity: 0,
    Parameters: [Named("System.String"), Named("System.Int32")],
    ReturnType: Named("System.Int32")
)
```

### Method: `EraseType(TypeReference) -> TsTypeShape`

**Type erasure by reference kind**:
1. **NamedTypeReference** (with type arguments): `GenericApplication(Named(fullName), erased type args)` (recursively erase args)
2. **NamedTypeReference** (simple): `Named(fullName)`
3. **NestedTypeReference**: `Named(nested.FullReference.FullName)`
4. **GenericParameterReference**: `TypeParameter(name)`
5. **ArrayTypeReference**: `Array(EraseType(elementType))` (recursively erase)
6. **PointerTypeReference**: `EraseType(pointeeType)` (erase pointer, TS doesn't have pointers)
7. **ByRefTypeReference**: `EraseType(referencedType)` (erase ref/out, TS doesn't have ref params)
8. **Fallback**: `Unknown(description)`

---

## File: PhaseGate.cs Overview

### Purpose
Validates symbol graph before emission. Comprehensive validation checks and policy enforcement. Quality gate between Shape/Plan and Emit phases.

### Method: `Validate(BuildContext, SymbolGraph, ImportPlan, InterfaceConstraintFindings)`

**Validation orchestration**:
1. **Create ValidationContext**: Track errors, warnings, diagnostics
2. **Run core validation checks** (delegated to `Validation.Core`):
   - ValidateTypeNames, ValidateMemberNames, ValidateGenericParameters
   - ValidateInterfaceConformance, ValidateInheritance
   - ValidateEmitScopes, ValidateImports, ValidatePolicyCompliance
3. **Run PhaseGate Hardening checks** (50+ rules):
   - **M1**: Identifier sanitization (PG_NAME_001/002)
   - **M2**: Overload collision detection (PG_NAME_006)
   - **M3**: View integrity validation (PG_VIEW_001/002/003)
   - **M4**: Constructor constraint loss (PG_CT_001/002)
   - **M5**: Scoping and naming (PG_NAME_003/004/005, PG_INT_002/003, PG_SCOPE_003/004)
   - **M6**: Finalization sweep (PG_FIN_001 through PG_FIN_009)
   - **M7**: Type reference validation (PG_PRINT_001, PG_TYPEMAP_001, PG_LOAD_001)
   - **M8**: Public API surface (PG_API_001/002)
   - **M9**: Import completeness (PG_IMPORT_001)
   - **M10**: Export completeness (PG_EXPORT_001)
4. **Report results**: Log error/warning/info counts, diagnostic summary table
5. **Handle errors**: If error count > 0, emit ValidationFailed diagnostic
6. **Write diagnostic files**: Full detailed report + machine-readable summary JSON

**Why this order matters**:
- TypeMap validation before external type checks (foundational)
- API surface validation before import checks (more fundamental)
- View integrity before member scoping (views must exist first)
- Finalization last (catches anything missed)

### Validation Module Structure

PhaseGate delegates to specialized validation modules in `Validation/` directory:

- **Core.cs** - Core validation (8 categories)
- **Names.cs** - Name collision, sanitization, uniqueness (5 checks)
- **Views.cs** - View integrity, member scoping (4 checks)
- **Scopes.cs** - EmitScope validation (3 checks)
- **Types.cs** - Type reference validation (3 checks)
- **ImportExport.cs** - Import/export completeness (3 checks)
- **Constraints.cs** - Generic constraint auditing (2 diagnostics)
- **Finalization.cs** - Finalization sweep (9 checks)
- **Context.cs** - Diagnostic tracking and reporting
- **Shared.cs** - Shared validation utilities

**Total**: 50+ distinct checks covering naming, scopes, views, types, imports, exports, conformance, constraints, finalization, policy compliance

**Diagnostic codes**: `PG_CATEGORY_NNN` format (e.g., `PG_NAME_001`, `PG_VIEW_003`)

**Full PhaseGate validation details documented separately in `07-phasegate.md`.**

---

## Summary

**Plan phase** performs final validation and preparation before emission:

1. **ImportGraph** builds complete namespace dependency graph
2. **ImportPlanner** generates TS import statements with aliases
3. **EmitOrderPlanner** creates deterministic emission order
4. **PathPlanner** computes relative module paths
5. **InterfaceConstraintAuditor** detects constraint losses
6. **TsErase/TsAssignability** validate TS compatibility
7. **PhaseGate** enforces 50+ correctness rules

**PhaseGate validation categories**:
- Type/member naming correctness
- EmitScope integrity
- View correctness (3 hard rules)
- Import/export completeness
- Type reference validity
- Generic constraint tracking
- Finalization completeness
- Policy compliance

**After Plan phase**:
- Symbol graph validated and correct
- Import dependencies resolved
- Emission order determined
- All PhaseGate invariants hold
- Ready for code emission

**Next phase**: Emit (generate `.d.ts`, `.metadata.json`, `.bindings.json`)

# 10. Call Graphs - Complete Call Chains

## Overview

**What are call graphs?**
- Complete chain of function calls from CLI entry through file emission
- Shows execution flow through entire pipeline
- Documents which functions call which other functions
- Exact function signatures and file locations

**Why document call graphs?**
- Essential for understanding execution flow
- Critical for debugging (trace call origins)
- Enables impact analysis (what breaks if I change this?)
- Guides new developers through codebase

---

## Entry Point Call Chain

```
User executes CLI command
  ↓
Program.Main(string[] args)
  Location: src/tsbindgen/Cli/Program.cs
  ↓
GenerateCommand.SetHandler(async context => ...)
  Location: src/tsbindgen/Cli/GenerateCommand.cs:114
  ↓
GenerateCommand.ExecuteAsync(...)
  Location: GenerateCommand.cs:153
  ↓
GenerateCommand.ExecuteNewPipelineAsync(...)
  Location: GenerateCommand.cs:440
  ↓
SinglePhaseBuilder.Build(assemblyPaths, outDir, policy, logger, verbose, logCategories)
  Location: src/tsbindgen/SinglePhase/SinglePhaseBuilder.cs:27
  ↓
[Five-Phase Pipeline Starts]
  [Phase 1: Load]
  [Phase 2: Normalize]
  [Phase 3: Shape]
  [Phase 3.5: Name Reservation]
  [Phase 4: Plan]
  [Phase 4.5-4.7: Validation]
  [Phase 5: Emit]
```

---

## Phase 1: Load Call Graph

Complete chain for assembly loading and reflection:

```
SinglePhaseBuilder.Build
  ↓
LoadPhase(ctx, assemblyPaths)
  Location: SinglePhaseBuilder.cs:118
  ↓
AssemblyLoader.LoadClosure(seedPaths, refPaths, strictVersions)
  Location: src/tsbindgen/SinglePhase/Load/AssemblyLoader.cs:113
  ↓
  ├─→ BuildCandidateMap(refPaths)
  │   Location: AssemblyLoader.cs:167
  │   Returns: Dictionary<AssemblyKey, List<string>>
  │   Scans reference directories for all .dll files
  │
  ├─→ ResolveClosure(seedPaths, candidateMap, strictVersions)
  │   Location: AssemblyLoader.cs:207
  │   BFS traversal to find all transitive dependencies
  │   Uses System.Reflection.PortableExecutable.PEReader
  │
  ├─→ ValidateAssemblyIdentity(resolvedPaths, strictVersions)
  │   Location: AssemblyLoader.cs:295
  │   Check PG_LOAD_002 (mixed PKT), PG_LOAD_003 (version drift)
  │
  ├─→ FindCoreLibrary(resolvedPaths)
  │   Location: AssemblyLoader.cs:354
  │   Returns: Path to System.Private.CoreLib.dll
  │
  └─→ new MetadataLoadContext(resolver, "System.Private.CoreLib")
      System.Reflection infrastructure
      Returns: MetadataLoadContext with all assemblies loaded
  ↓
ReflectionReader.ReadAssemblies(loadContext, allAssemblyPaths)
  Location: src/tsbindgen/SinglePhase/Load/ReflectionReader.cs:28
  ↓
  For each type in assembly.GetTypes:
    └─→ ReadType(type)
        Location: ReflectionReader.cs:102
        ↓
        ├─→ DetermineTypeKind(type)
        │   Returns: TypeKind enum (Class, Interface, Enum, etc.)
        │
        ├─→ ComputeAccessibility(type)
        │   Returns: Accessibility enum (Public, Internal)
        │
        ├─→ TypeReferenceFactory.CreateGenericParameterSymbol(param)
        │   For each generic parameter
        │
        ├─→ TypeReferenceFactory.Create(type.BaseType)
        │   Create TypeReference for base type
        │
        ├─→ TypeReferenceFactory.Create(iface) for each interface
        │
        ├─→ ReadMembers(type)
        │   Location: ReflectionReader.cs:189
        │   ↓
        │   ├─→ ReadMethod(method, type) for each method
        │   │   Location: ReflectionReader.cs:264
        │   │   ↓
        │   │   ├─→ CreateMethodSignature(method)
        │   │   │   ctx.CanonicalizeMethod(name, paramTypes, returnType)
        │   │   │
        │   │   ├─→ ReadParameter(param) for each parameter
        │   │   │   TypeScriptReservedWords.SanitizeParameterName(name)
        │   │   │   TypeReferenceFactory.Create(param.ParameterType)
        │   │   │
        │   │   └─→ IsMethodOverride(method)
        │   │       Check MethodAttributes flags (NewSlot)
        │   │
        │   ├─→ ReadProperty(property, type) for each property
        │   ├─→ ReadField(field, type) for each field
        │   ├─→ ReadEvent(evt, type) for each event
        │   └─→ ReadConstructor(ctor, type) for each constructor
        │
        └─→ For each nested type: ReadType(nestedType) (recursive)
  ↓
Returns: SymbolGraph with Namespaces → Types → Members
  All members have EmitScope = EmitScope.ClassSurface (initial state)
  ↓
InterfaceMemberSubstitution.SubstituteClosedInterfaces(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Load/InterfaceMemberSubstitutor.cs:20
  ↓
  For each closed generic interface (e.g., IComparable<int>):
    └─→ BuildSubstitutionMap(ifaceSymbol, closedInterfaceRef)
        Creates map: T → int for IComparable<int>
        Used later by StructuralConformance, ViewPlanner
```

**Key System.Reflection Functions:**
- `Assembly.GetTypes`, `Type.GetMethods`, `Type.GetProperties`
- `Type.GetFields`, `Type.GetEvents`, `Type.GetConstructors`
- `Type.GetInterfaces`, `MethodInfo.GetParameters`

---

## Phase 2: Normalize Call Graph

Index building for O(1) lookups:

```
SinglePhaseBuilder.Build
  ↓
graph = graph.WithIndices
  Location: src/tsbindgen/SinglePhase/Model/SymbolGraph.cs
  ↓
  Builds TypeIndex: CLR full name → TypeSymbol
  Builds NamespaceIndex: namespace name → NamespaceSymbol
  ↓
  Returns: new SymbolGraph with indices populated
```

---

## Phase 3: Shape Call Graph

14 transformation passes:

```
SinglePhaseBuilder.Build
  ↓
ShapePhase(ctx, graph)
  Location: SinglePhaseBuilder.cs:165
  ↓
┌─────────────────────────────────────────────────────┐
│ Pass 1: Build Interface Indices (BEFORE flattening) │
└─────────────────────────────────────────────────────┘
  ↓
GlobalInterfaceIndex.Build(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/GlobalInterfaceIndex.cs:22
  ↓
  For each interface:
    └─→ ComputeMethodSignatures(ctx, iface)
        ctx.CanonicalizeMethod(name, paramTypes, returnType)
  ↓
  Populates: _globalIndex[interfaceFullName] = InterfaceInfo
  ↓
InterfaceDeclIndex.Build(ctx, graph)
  Location: GlobalInterfaceIndex.cs:149
  ↓
  For each interface:
    └─→ CollectInheritedSignatures(iface)
        Walk base interfaces, collect inherited signatures
        Compute declared-only (exclude inherited)
  ↓
┌──────────────────────────────────────────────────────────┐
│ Pass 2: Structural Conformance (synthesizes ViewOnly)   │
└──────────────────────────────────────────────────────────┘
  ↓
graph = StructuralConformance.Analyze(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/StructuralConformance.cs
  ↓
  For each class implementing interfaces:
    If class method doesn't match interface signature:
      └─→ Create ViewOnly synthetic method
          Set EmitScope = ViewOnly
          Set SourceInterface = interface CLR name
  ↓
┌────────────────────────────────────────────────────┐
│ Pass 3: Interface Inlining (flatten interfaces)   │
└────────────────────────────────────────────────────┘
  ↓
graph = InterfaceInliner.Inline(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/InterfaceInliner.cs
  ↓
  For each class implementing interfaces:
    For each interface method not on class:
      └─→ Create inlined method
          Set EmitScope = ClassSurface
          Set Provenance = InterfaceInlining
  ↓
┌────────────────────────────────────────────────────────────┐
│ Pass 4: Explicit Interface Implementation Synthesis       │
└────────────────────────────────────────────────────────────┘
  ↓
graph = ExplicitImplSynthesizer.Synthesize(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/ExplicitImplSynthesizer.cs
  ↓
  For each method/property with '.' in name:
    └─→ Parse qualified name (e.g., "System.IDisposable.Dispose")
        Create ViewOnly member with SourceInterface
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 5: Diamond Inheritance Resolution          │
└──────────────────────────────────────────────────┘
  ↓
graph = DiamondResolver.Resolve(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/DiamondResolver.cs
  ↓
  Pick single implementation for ambiguous members
  Emit PG_INT_005 diagnostic if conflict
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 6: Base Overload Addition                  │
└──────────────────────────────────────────────────┘
  ↓
graph = BaseOverloadAdder.AddOverloads(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/BaseOverloadAdder.cs
  ↓
  Walk inheritance chain, add base overloads if needed for TypeScript
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 7: Static-Side Analysis                    │
└──────────────────────────────────────────────────┘
  ↓
StaticSideAnalyzer.Analyze(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/StaticSideAnalyzer.cs
  ↓
  Check for static/instance name collisions
  Emit PG_NAME_002 if collision
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 8: Indexer Planning                        │
└──────────────────────────────────────────────────┘
  ↓
graph = IndexerPlanner.Plan(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/IndexerPlanner.cs
  ↓
  For each property with IndexParameters.Count > 0:
    └─→ Set EmitScope = Omit
        Reserve name through ctx.Renamer
        Track in metadata.json
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 9: Hidden Member Planning (C# 'new')       │
└──────────────────────────────────────────────────┘
  ↓
HiddenMemberPlanner.Plan(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/HiddenMemberPlanner.cs
  ↓
  For each member hiding base member:
    └─→ Reserve renamed name with disambiguation suffix
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 10: Final Indexers Pass                    │
└──────────────────────────────────────────────────┘
  ↓
graph = FinalIndexersPass.Run(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/FinalIndexersPass.cs
  ↓
  Verify no indexer has EmitScope = ClassSurface
  Emit PG_EMIT_001 if leak found
  ↓
┌──────────────────────────────────────────────────────────┐
│ Pass 10.5: Class Surface Deduplication (M5)             │
└──────────────────────────────────────────────────────────┘
  ↓
graph = ClassSurfaceDeduplicator.Deduplicate(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/ClassSurfaceDeduplicator.cs
  ↓
  Group ClassSurface members by TsEmitName:
    If duplicates: pick winner (prefer Original)
    Demote losers to Omit
    Emit PG_DEDUP_001
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 11: Constraint Closure                     │
└──────────────────────────────────────────────────┘
  ↓
graph = ConstraintCloser.Close(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/ConstraintCloser.cs
  ↓
  Compute transitive closure of constraints
  Example: T : IComparable<U>, U : IList<V> → T : IList<V>
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 12: Return-Type Conflict Resolution        │
└──────────────────────────────────────────────────┘
  ↓
graph = OverloadReturnConflictResolver.Resolve(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/OverloadReturnConflictResolver.cs
  ↓
  For method groups with different return types:
    └─→ Add disambiguation suffix
        Emit PG_OVERLOAD_001
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 13: View Planning (explicit interface views)│
└──────────────────────────────────────────────────┘
  ↓
graph = ViewPlanner.Plan(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/ViewPlanner.cs
  ↓
  For each ViewOnly member:
    Verify SourceInterface is set
    Decide if member goes in view
  ↓
┌──────────────────────────────────────────────────┐
│ Pass 14: Final Member Deduplication              │
└──────────────────────────────────────────────────┘
  ↓
graph = MemberDeduplicator.Deduplicate(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Shape/MemberDeduplicator.cs
  ↓
  Group members by StableId, remove duplicates
  Emit PG_DEDUP_002
```

**Key Observation:**
- Shape passes are PURE - each returns new SymbolGraph
- Renamer is MUTATED - accumulates rename decisions

---

## Phase 3.5: Name Reservation Call Graph

Central naming phase:

```
SinglePhaseBuilder.Build
  ↓
graph = NameReservation.ReserveAllNames(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Normalize/NameReservation.cs:32
  ↓
┌────────────────────────────────────────────────┐
│ Step 1: Reserve Type Names                    │
└────────────────────────────────────────────────┘
  ↓
  For each type:
    └─→ Shared.ComputeTypeRequestedBase(type.ClrName)
        Location: src/tsbindgen/SinglePhase/Normalize/Naming/Shared.cs
        ↓
        Transforms:
          - Remove backtick and arity (List`1 → List)
          - Replace + with _ (Outer+Inner → Outer_Inner)
          - Sanitize reserved words
        ↓
        ctx.Renamer.ReserveTypeName(stableId, requested, scope, context, source)
        Location: src/tsbindgen/SinglePhase/Renaming/SymbolRenamer.cs
        ↓
        Check if name available, add suffix if conflict
        Store in _typeDecisions[stableId][scope] = RenameDecision
  ↓
┌────────────────────────────────────────────────┐
│ Step 2: Reserve Class Surface Member Names    │
└────────────────────────────────────────────────┘
  ↓
  Reservation.ReserveMemberNamesOnly(ctx, type)
  Location: src/tsbindgen/SinglePhase/Normalize/Naming/Reservation.cs
  ↓
  For each member where EmitScope == ClassSurface:
    ↓
    If already has decision: Skip
    ↓
    Otherwise:
      ├─→ Shared.ComputeMemberRequestedBase(member.ClrName)
      │   Sanitize reserved words, apply camelCase if enabled
      │
      ├─→ ScopeFactory.ClassSurface(type, member.IsStatic)
      │   Location: src/tsbindgen/SinglePhase/Normalize/Naming/ScopeFactory.cs
      │   Creates: namespace/internal/TypeName/instance or static
      │
      └─→ ctx.Renamer.ReserveMemberName(stableId, requested, scope, context, source)
          Check if name available, add suffix if conflict
          Store in _memberDecisions[stableId][scope] = RenameDecision
  ↓
┌────────────────────────────────────────────────┐
│ Step 3: Build Class Surface Name Sets         │
└────────────────────────────────────────────────┘
  ↓
  For each ClassSurface member:
    ↓
    ctx.Renamer.TryGetDecision(stableId, classScope, out decision)
    ↓
    Add decision.Final to classInstanceNames or classStaticNames
    Union into classAllNames set
  ↓
  Purpose: Collision detection for view members
  ↓
┌────────────────────────────────────────────────┐
│ Step 4: Reserve View-Scoped Member Names (M5) │
└────────────────────────────────────────────────┘
  ↓
  Reservation.ReserveViewMemberNamesOnly(ctx, graph, type, classAllNames)
  Location: Naming/Reservation.cs
  ↓
  For each ViewOnly member:
    ↓
    ├─→ ScopeFactory.ViewScope(type, sourceInterface, member.IsStatic)
    │   Creates: namespace/internal/TypeName/view/InterfaceName/instance
    │   DIFFERENT scope from class surface!
    │
    ├─→ Check collision with classAllNames
    │   If collision: Add disambiguation suffix
    │   Emit PG_NAME_003 or PG_NAME_004
    │
    └─→ ctx.Renamer.ReserveMemberName(stableId, requested, viewScope, ...)
        Store in _memberDecisions[stableId][viewScope] = RenameDecision
        Note: Same member can have DIFFERENT names in class vs view scope
  ↓
┌────────────────────────────────────────────────┐
│ Step 5: Post-Reservation Audit (fail fast)    │
└────────────────────────────────────────────────┘
  ↓
  Audit.AuditReservationCompleteness(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Normalize/Naming/Audit.cs
  ↓
  For each member where EmitScope != Omit:
    └─→ ctx.Renamer.TryGetDecision(stableId, expectedScope, out _)
        If NOT found: Fatal error - name reservation incomplete
  ↓
┌────────────────────────────────────────────────┐
│ Step 6: Apply Names to Graph (pure transform) │
└────────────────────────────────────────────────┘
  ↓
  updatedGraph = Application.ApplyNamesToGraph(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Normalize/Naming/Application.cs
  ↓
  For each type:
    ctx.Renamer.GetFinalTypeName(stableId, namespaceScope)
    Set type.TsEmitName
  ↓
  For each member:
    ctx.Renamer.GetFinalMemberName(stableId, scope)
    Set member.TsEmitName
  ↓
  Returns: New SymbolGraph with all TsEmitName fields populated
```

**Critical Invariants:**
1. Every emitted type has TsEmitName set
2. Every emitted member has TsEmitName set
3. Every TsEmitName has RenameDecision in Renamer
4. View members can have different TsEmitName from class members

---

## Phase 4: Plan Call Graph

Import planning, ordering, unification, validation:

```
SinglePhaseBuilder.Build
  ↓
plan = PlanPhase(ctx, graph)
  Location: SinglePhaseBuilder.cs:250
  ↓
┌────────────────────────────────────────────────┐
│ Step 1: Build Import Graph                    │
└────────────────────────────────────────────────┘
  ↓
importGraph = ImportGraph.Build(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Plan/ImportGraph.cs
  ↓
  For each type signature:
    └─→ Extract foreign type references (from different namespaces)
        Record: SourceNamespace → ForeignNamespace → TypeNames
  ↓
┌────────────────────────────────────────────────┐
│ Step 2: Plan Imports and Aliases              │
└────────────────────────────────────────────────┘
  ↓
imports = ImportPlanner.PlanImports(ctx, graph, importGraph)
  Location: src/tsbindgen/SinglePhase/Plan/ImportPlanner.cs
  ↓
  For each namespace:
    ├─→ Collect foreign types needed
    ├─→ Group by source namespace
    ├─→ Check for name collisions with local types
    │   If collision: Create alias (TypeName → TypeName_fromNamespace)
    └─→ Build ImportPlan with import statements and aliases
  ↓
┌────────────────────────────────────────────────┐
│ Step 3: Plan Emission Order                   │
└────────────────────────────────────────────────┘
  ↓
order = EmitOrderPlanner.PlanOrder(graph)
  Location: src/tsbindgen/SinglePhase/Plan/EmitOrderPlanner.cs
  ↓
  ├─→ Build dependency graph between namespaces
  ├─→ Topological sort (dependencies before dependents)
  └─→ Within namespace: Interfaces, base classes, derived classes, structs, enums, delegates
  ↓
┌────────────────────────────────────────────────┐
│ Phase 4.5: Overload Unification               │
└────────────────────────────────────────────────┘
  ↓
graph = OverloadUnifier.UnifyOverloads(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Normalize/OverloadUnifier.cs
  ↓
  For method groups with multiple overloads:
    ├─→ Sort by parameter count
    ├─→ Mark last as UnifiedImplementation (umbrella signature)
    └─→ Mark others as UnifiedDeclaration (declaration-only)
  ↓
┌────────────────────────────────────────────────────────┐
│ Phase 4.6: Interface Constraint Audit                 │
└────────────────────────────────────────────────────────┘
  ↓
constraintFindings = InterfaceConstraintAuditor.Audit(ctx, graph)
  Location: src/tsbindgen/SinglePhase/Plan/InterfaceConstraintAuditor.cs
  ↓
  For each (Type, Interface) pair:
    └─→ Check constructor constraints (PG_CONSTRAINT_001)
        Check base type constraints (PG_CONSTRAINT_002)
  ↓
┌────────────────────────────────────────────────┐
│ Phase 4.7: PhaseGate Validation (20+ checks)  │
└────────────────────────────────────────────────┘
  ↓
PhaseGate.Validate(ctx, graph, imports, constraintFindings)
  Location: src/tsbindgen/SinglePhase/Plan/PhaseGate.cs:24
  ↓
  [See next section for complete PhaseGate call graph]
  ↓
  If no errors: Continue to Emit phase
  If errors: Build fails
```

---

## Phase 4.7: PhaseGate Validation Call Graph

Comprehensive pre-emission validation:

```
PhaseGate.Validate(ctx, graph, imports, constraintFindings)
  ↓
validationContext = new ValidationContext
  ↓
┌────────────────────────────────────────────────┐
│ Core Validations (8 functions)                │
└────────────────────────────────────────────────┘
  ↓
ValidationCore.ValidateTypeNames(ctx, graph, validationContext)
  Location: src/tsbindgen/SinglePhase/Plan/Validation/Core.cs
  ↓
  For each type:
    └─→ Check type.TsEmitName is set (PG_NAME_001)
        Check TsEmitName is valid identifier (PG_IDENT_001)
  ↓
ValidationCore.ValidateMemberNames(ctx, graph, validationContext)
  ↓
  For each member where EmitScope != Omit:
    └─→ Check member.TsEmitName is set (PG_NAME_001)
        Check TsEmitName is valid identifier (PG_IDENT_001)
  ↓
ValidationCore.ValidateGenericParameters(ctx, graph, validationContext)
  ↓
  Check generic parameter names, constraint types exist (PG_GEN_001)
  ↓
ValidationCore.ValidateInterfaceConformance(ctx, graph, validationContext)
  ↓
  For each class implementing interfaces:
    └─→ Check all interface members have implementations
        Either ClassSurface OR ViewOnly (PG_INT_001)
  ↓
ValidationCore.ValidateInheritance(ctx, graph, validationContext)
  ↓
  Check base types exist, override signatures match (PG_INH_001)
  ↓
ValidationCore.ValidateEmitScopes(ctx, graph, validationContext)
  ↓
  Check EmitScope is valid: ClassSurface, ViewOnly, or Omit (PG_SCOPE_001)
  ↓
ValidationCore.ValidateImports(ctx, graph, imports, validationContext)
  ↓
  Check imported namespaces and types exist (PG_IMPORT_002)
  ↓
ValidationCore.ValidatePolicyCompliance(ctx, graph, validationContext)
  ↓
  Check policy rules: unsafe markers, name transforms, omissions tracked (PG_POLICY_001)
  ↓
┌────────────────────────────────────────────────┐
│ M1: Identifier Sanitization (Names module)    │
└────────────────────────────────────────────────┘
  ↓
Names.ValidateIdentifiers(ctx, graph, validationContext)
  Location: src/tsbindgen/SinglePhase/Plan/Validation/Names.cs
  ↓
  Check TsEmitName doesn't contain reserved words or special chars (PG_IDENT_002)
  ↓
┌────────────────────────────────────────────────┐
│ M2: Overload Collision Detection (Names)      │
└────────────────────────────────────────────────┘
  ↓
Names.ValidateOverloadCollisions(ctx, graph, validationContext)
  ↓
  For method groups: check overload signatures compatible (PG_OVERLOAD_002)
  ↓
┌────────────────────────────────────────────────┐
│ M3: View Integrity (Views module - 3 rules)   │
└────────────────────────────────────────────────┘
  ↓
Views.ValidateIntegrity(ctx, graph, validationContext)
  Location: src/tsbindgen/SinglePhase/Plan/Validation/Views.cs
  ↓
  Rule 1: ViewOnly members MUST have SourceInterface (PG_VIEW_001 - FATAL)
  Rule 2: ViewOnly members MUST have ClassSurface twin (PG_VIEW_002 - FATAL)
  Rule 3: ClassSurface-ViewOnly pairs MUST have same CLR signature (PG_VIEW_003 - FATAL)
  ↓
┌────────────────────────────────────────────────┐
│ M4: Constraint Findings (Constraints module)  │
└────────────────────────────────────────────────┘
  ↓
Constraints.EmitDiagnostics(ctx, constraintFindings, validationContext)
  Location: src/tsbindgen/SinglePhase/Plan/Validation/Constraints.cs
  ↓
  Emit PG_CONSTRAINT_001 (missing constructor)
  Emit PG_CONSTRAINT_002 (missing base type)
  ↓
┌────────────────────────────────────────────────────┐
│ M5: View Member Scoping (Views - PG_NAME_003/004) │
└────────────────────────────────────────────────────┘
  ↓
Views.ValidateMemberScoping(ctx, graph, validationContext)
  ↓
  For each type:
    ├─→ Build classAllNames set (instance + static)
    └─→ For each ViewOnly member:
        ├─→ Get view scope: ScopeFactory.ViewScope(...)
        ├─→ ctx.Renamer.GetFinalMemberName(stableId, viewScope)
        ├─→ Check collision with classAllNames
        │   Static collision: PG_NAME_003
        │   Instance collision: PG_NAME_004
        └─→ Check view name != class name (PG_NAME_005)
  ↓
┌────────────────────────────────────────────────────────┐
│ M5: EmitScope Invariants (Scopes - PG_INT_002/003)    │
└────────────────────────────────────────────────────────┘
  ↓
Scopes.ValidateEmitScopeInvariants(ctx, graph, validationContext)
  Location: src/tsbindgen/SinglePhase/Plan/Validation/Scopes.cs
  ↓
  For each member:
    ├─→ Check EmitScope ∈ {ClassSurface, ViewOnly, Omit} (PG_SCOPE_001)
    ├─→ If ViewOnly: Check SourceInterface is set (PG_INT_002 - FATAL)
    └─→ If ClassSurface: Check SourceInterface is null (PG_INT_003 - FATAL)
  ↓
┌────────────────────────────────────────────────────────┐
│ M5: Scope Mismatches (Scopes - PG_SCOPE_003/004)      │
└────────────────────────────────────────────────────────┘
  ↓
Scopes.ValidateScopeMismatches(ctx, graph, validationContext)
  ↓
  For each member where EmitScope != Omit:
    ├─→ Compute expected scope from EmitScope + IsStatic + SourceInterface
    ├─→ Check Renamer has decision in expected scope
    └─→ If NOT found:
        Check if in wrong scope: PG_SCOPE_003 or PG_SCOPE_004
        If not found anywhere: PG_NAME_001 (FATAL)
  ↓
┌────────────────────────────────────────────────────────────┐
│ M5: Class Surface Uniqueness (Names - PG_NAME_005)        │
└────────────────────────────────────────────────────────────┘
  ↓
Names.ValidateClassSurfaceUniqueness(ctx, graph, validationContext)
  ↓
  For each type:
    Group ClassSurface members by (TsEmitName, IsStatic)
    If duplicates: PG_NAME_005 (FATAL - deduplicator failed)
  ↓
┌────────────────────────────────────────────────────────┐
│ M6: Finalization Sweep (PG_FIN_001 through PG_FIN_009) │
└────────────────────────────────────────────────────────┘
  ↓
Finalization.Validate(ctx, graph, validationContext)
  Location: src/tsbindgen/SinglePhase/Plan/Validation/Finalization.cs
  ↓
  For each type:
    ├─→ Check TsEmitName is set (PG_FIN_001)
    ├─→ Check Accessibility is set (PG_FIN_002)
    ├─→ Check Kind is valid (PG_FIN_003)
    └─→ For each member where EmitScope != Omit:
        ├─→ Check TsEmitName (PG_FIN_004)
        ├─→ Check EmitScope (PG_FIN_005)
        ├─→ Check Provenance (PG_FIN_006)
        ├─→ If ViewOnly: Check SourceInterface (PG_FIN_007)
        ├─→ Check return type exists (PG_FIN_008)
        └─→ Check all parameter types exist (PG_FIN_009)
  ↓
┌────────────────────────────────────────────────────────┐
│ M7: Printer Name Consistency (Types - PG_PRINT_001)   │
└────────────────────────────────────────────────────────┘
  ↓
Types.ValidatePrinterNameConsistency(ctx, graph, validationContext)
  Location: src/tsbindgen/SinglePhase/Plan/Validation/Types.cs
  ↓
  For each TypeReference in signatures:
    └─→ Simulate TypeRefPrinter.Print(typeRef, scope)
        └─→ TypeNameResolver.ResolveTypeName(typeRef, scope, ctx.Renamer)
            └─→ ctx.Renamer.GetFinalTypeName(stableId, scope)
                Check result is valid (PG_PRINT_001)
  ↓
┌────────────────────────────────────────────────────────┐
│ M7a: TypeMap Compliance (Types - PG_TYPEMAP_001)      │
└────────────────────────────────────────────────────────┘
  ↓
Types.ValidateTypeMapCompliance(ctx, graph, validationContext)
  ↓
  For each TypeReference:
    └─→ Check for unsupported CLR types:
        PointerTypeReference, ByRefTypeReference (non-param), FunctionPointerReference
        Emit PG_TYPEMAP_001 if unsupported
  ↓
┌────────────────────────────────────────────────────────────┐
│ M7b: External Type Resolution (Types - PG_LOAD_001)       │
└────────────────────────────────────────────────────────────┘
  ↓
Types.ValidateExternalTypeResolution(ctx, graph, validationContext)
  ↓
  For each foreign NamedTypeReference:
    └─→ Check if type exists in graph.TypeIndex
        If NOT found and NOT built-in: PG_LOAD_001
  ↓
┌────────────────────────────────────────────────────────────┐
│ M8: Public API Surface (ImportExport - PG_API_001/002)    │
└────────────────────────────────────────────────────────────┘
  ↓
ImportExport.ValidatePublicApiSurface(ctx, graph, imports, validationContext)
  Location: src/tsbindgen/SinglePhase/Plan/Validation/ImportExport.cs
  ↓
  For each public member signature:
    └─→ Check referenced type is emitted
        Internal type: PG_API_001
        Omitted type: PG_API_002
  ↓
┌────────────────────────────────────────────────────────────┐
│ M9: Import Completeness (ImportExport - PG_IMPORT_001)    │
└────────────────────────────────────────────────────────────┘
  ↓
ImportExport.ValidateImportCompleteness(ctx, graph, imports, validationContext)
  ↓
  For each foreign type used:
    └─→ Check imports.HasImport(foreignNamespace, typeName)
        If NOT found: PG_IMPORT_001
  ↓
┌────────────────────────────────────────────────────────────┐
│ M10: Export Completeness (ImportExport - PG_EXPORT_001)   │
└────────────────────────────────────────────────────────────┘
  ↓
ImportExport.ValidateExportCompleteness(ctx, graph, imports, validationContext)
  ↓
  For each import statement:
    └─→ Check source namespace exports the type
        If NOT exported: PG_EXPORT_001
  ↓
┌────────────────────────────────────────────┐
│ Final: Report Results                     │
└────────────────────────────────────────────┘
  ↓
  Print diagnostic summary table (grouped by code, sorted by count)
  ↓
  If ErrorCount > 0:
    ctx.Diagnostics.Error(DiagnosticCodes.ValidationFailed, ...)
    Build fails
  ↓
  Write to: .tests/phasegate-diagnostics.txt
  Write to: .tests/phasegate-summary.json
```

**Validation Modules:**
- **Core.cs**: 8 fundamental validations
- **Names.cs**: Identifier, collision, uniqueness
- **Views.cs**: View integrity (3 hard rules), scoping
- **Scopes.cs**: EmitScope invariants, mismatches
- **Constraints.cs**: Interface constraint violations
- **Finalization.cs**: Comprehensive sweep (9 checks)
- **Types.cs**: TypeMap, external resolution, printer consistency
- **ImportExport.cs**: API surface, import/export completeness

**Total:** 20+ validation functions, 40+ diagnostic codes

---

## Phase 5: Emit Call Graph

File generation:

```
SinglePhaseBuilder.Build
  ↓
EmitPhase(ctx, plan, outputDirectory)
  Location: SinglePhaseBuilder.cs:285
  ↓
┌────────────────────────────────────────────────────────┐
│ Step 1: Emit Support Types (once per build)           │
└────────────────────────────────────────────────────────┘
  ↓
SupportTypesEmit.Emit(ctx, outputDirectory)
  Location: src/tsbindgen/SinglePhase/Emit/SupportTypesEmitter.cs
  ↓
  Generate: _support/types.d.ts
    Branded numeric types (int, uint, byte, decimal, etc.)
    Unsafe markers (UnsafePointer, UnsafeByRef, etc.)
  ↓
┌────────────────────────────────────────────────────────┐
│ Step 2: Emit Internal Index Files (per namespace)     │
└────────────────────────────────────────────────────────┘
  ↓
InternalIndexEmitter.Emit(ctx, plan, outputDirectory)
  Location: src/tsbindgen/SinglePhase/Emit/InternalIndexEmitter.cs
  ↓
  For each namespace in plan.EmissionOrder:
    ↓
    ├─→ EmitFileHeader(builder)
    │   Add reference to _support/types.d.ts
    │
    ├─→ EmitImports(builder, imports)
    │   Write: import type { TypeA, TypeB } from "../OtherNamespace/internal"
    │
    ├─→ EmitNamespaceDeclaration(builder, namespace)
    │   Write: export namespace NamespaceName {
    │
    ├─→ For each type:
    │   └─→ Switch on type.Kind:
    │       ├─→ Class/Struct:
    │       │   └─→ ClassPrinter.PrintClassDeclaration(builder, type, ctx)
    │       │       Location: src/tsbindgen/SinglePhase/Emit/Printers/ClassPrinter.cs
    │       │       ↓
    │       │       ├─→ Write class keyword and generic parameters
    │       │       ├─→ Print extends clause
    │       │       ├─→ Print implements clause
    │       │       ├─→ For each constructor:
    │       │       │   └─→ MethodPrinter.PrintConstructor(...)
    │       │       ├─→ For each field/property/method where EmitScope == ClassSurface:
    │       │       │   └─→ MethodPrinter.PrintMethod(builder, method, ctx)
    │       │       │       ↓
    │       │       │       ├─→ Print method signature
    │       │       │       ├─→ For each parameter:
    │       │       │       │   └─→ TypeRefPrinter.Print(param.Type, scope)
    │       │       │       │       Location: Emit/Printers/TypeRefPrinter.cs
    │       │       │       │       ↓
    │       │       │       │       └─→ TypeNameResolver.ResolveTypeName(typeRef, scope, Renamer)
    │       │       │       │           Location: Emit/Printers/TypeNameResolver.cs
    │       │       │       │           ↓
    │       │       │       │           Switch on TypeReference kind:
    │       │       │       │             NamedTypeReference → ctx.Renamer.GetFinalTypeName(...)
    │       │       │       │             GenericParameterReference → Return param name
    │       │       │       │             ArrayTypeReference → Recursive + "[]"
    │       │       │       │             PointerTypeReference → "UnsafePointer<T>"
    │       │       │       │             ByRefTypeReference → "UnsafeByRef<T>"
    │       │       │       │
    │       │       │       └─→ TypeRefPrinter.Print(method.ReturnType, scope)
    │       │       │
    │       │       └─→ Emit interface views (if any ViewOnly members)
    │       │           Group ViewOnly by SourceInterface:
    │       │             Write: export interface TypeName_View_InterfaceName {
    │       │             For each ViewOnly: Print using member.TsEmitName
    │       │
    │       ├─→ Interface:
    │       │   └─→ ClassPrinter.PrintInterfaceDeclaration(...)
    │       │       Similar to class but: interface keyword, all signatures, no constructors
    │       │
    │       ├─→ Enum:
    │       │   Write enum declaration
    │       │
    │       ├─→ Delegate:
    │       │   Write type alias: export type DelegateName = (param: T) => ReturnType
    │       │
    │       └─→ StaticNamespace:
    │           Write static class
    │
    └─→ Write to disk: namespace/internal/index.d.ts
  ↓
┌────────────────────────────────────────────────────────┐
│ Step 3: Emit Facade Files (per namespace)             │
└────────────────────────────────────────────────────────┘
  ↓
FacadeEmitter.Emit(ctx, plan, outputDirectory)
  Location: src/tsbindgen/SinglePhase/Emit/FacadeEmitter.cs
  ↓
  For each namespace:
    Generate: namespace/index.d.ts (public facade)
    Content: export * from "./internal";
  ↓
┌────────────────────────────────────────────────────────┐
│ Step 4: Emit Metadata Files (per namespace)           │
└────────────────────────────────────────────────────────┘
  ↓
MetadataEmitter.Emit(ctx, plan, outputDirectory)
  Location: src/tsbindgen/SinglePhase/Emit/MetadataEmitter.cs
  ↓
  For each namespace:
    Build JSON:
      { "namespace": "...", "types": [...], "omissions": {...} }
    Write to: namespace/metadata.json
  ↓
┌────────────────────────────────────────────────────────┐
│ Step 5: Emit Binding Files (per namespace)            │
└────────────────────────────────────────────────────────┘
  ↓
BindingEmitter.Emit(ctx, plan, outputDirectory)
  Location: src/tsbindgen/SinglePhase/Emit/BindingEmitter.cs
  ↓
  For each namespace:
    Build JSON (CLR → TypeScript name mappings)
    Write to: namespace/bindings.json
  ↓
┌────────────────────────────────────────────────────────┐
│ Step 6: Emit Module Stubs (per namespace)             │
└────────────────────────────────────────────────────────┘
  ↓
ModuleStubEmitter.Emit(ctx, plan, outputDirectory)
  Location: src/tsbindgen/SinglePhase/Emit/ModuleStubEmitter.cs
  ↓
  For each namespace:
    Generate: namespace/index.js (stub)
    Content: throw new Error("This is a type-only module");
```

**Key Printer Functions:**
- **ClassPrinter.cs**: Emits class/interface/struct declarations
- **MethodPrinter.cs**: Emits method/constructor signatures
- **TypeRefPrinter.cs**: Converts TypeReference → TypeScript syntax
- **TypeNameResolver.cs**: Resolves type names through Renamer

**Output Files Per Namespace:**
```
namespace/
  ├── internal/index.d.ts    # Full declarations
  ├── index.d.ts             # Public facade
  ├── index.js               # Module stub
  ├── metadata.json          # CLR metadata for Tsonic
  └── bindings.json          # CLR→TS mappings
```

---

## Cross-Cutting Call Graphs

Functions called across multiple phases:

### SymbolRenamer

Central naming service:

```
┌────────────────────────────────────────────┐
│ ReserveTypeName - Called By:               │
└────────────────────────────────────────────┘
  1. NameReservation.ReserveAllNames (Phase 3.5)
  2. HiddenMemberPlanner.Plan (Phase 3 - Pass 9)
  3. ClassSurfaceDeduplicator.Deduplicate (Phase 3 - Pass 10.5)

┌────────────────────────────────────────────┐
│ ReserveMemberName - Called By:             │
└────────────────────────────────────────────┘
  1. Reservation.ReserveMemberNamesOnly (Phase 3.5 - Class Surface)
  2. Reservation.ReserveViewMemberNamesOnly (Phase 3.5 - Views)
  3. IndexerPlanner.Plan (Phase 3 - Pass 8)
  4. HiddenMemberPlanner.Plan (Phase 3 - Pass 9)

┌────────────────────────────────────────────┐
│ GetFinalTypeName - Called By:              │
└────────────────────────────────────────────┘
  1. Application.ApplyNamesToGraph (Phase 3.5)
  2. TypeNameResolver.ResolveTypeName (Phase 5 - Emit)
  3. PhaseGate validation modules (Phase 4.7)

┌────────────────────────────────────────────┐
│ GetFinalMemberName - Called By:            │
└────────────────────────────────────────────┘
  1. Application.ApplyNamesToGraph (Phase 3.5)
  2. ClassPrinter.PrintMethod/PrintProperty (Phase 5)
  3. Views.ValidateMemberScoping (Phase 4.7)

┌────────────────────────────────────────────┐
│ TryGetDecision - Called By:                │
└────────────────────────────────────────────┘
  1. NameReservation.ReserveAllNames (Phase 3.5)
  2. Scopes.ValidateScopeMismatches (Phase 4.7)
  3. Audit.AuditReservationCompleteness (Phase 3.5)
```

**Data Structures:**
```csharp
// Type decisions: StableId → Scope → RenameDecision
Dictionary<TypeStableId, Dictionary<Scope, RenameDecision>> _typeDecisions;

// Member decisions: StableId → Scope → RenameDecision
Dictionary<MemberStableId, Dictionary<Scope, RenameDecision>> _memberDecisions;

record RenameDecision
{
    string Requested;    // Original requested name
    string Final;        // Final name after disambiguation
    string Context;      // What triggered reservation
    string Source;       // Which pass reserved this
}
```

### DiagnosticBag

Error tracking:

```
┌────────────────────────────────────────────┐
│ Error - Called By:                       │
└────────────────────────────────────────────┘
  1. AssemblyLoader.LoadClosure (Phase 1)
  2. PhaseGate validation modules (Phase 4.7)
  3. BuildContext exception handling (Any phase)

┌────────────────────────────────────────────┐
│ HasErrors - Called By:                   │
└────────────────────────────────────────────┘
  1. SinglePhaseBuilder.Build (End of pipeline)
  2. PhaseGate.Validate (Phase 4.7)
```

### Policy

Configuration:

```
Policy.Emission.MemberNameTransform
  → Shared.ComputeMemberRequestedBase (Phase 3.5)

Policy.Omissions.OmitIndexers
  → IndexerPlanner.Plan (Phase 3 - Pass 8)

Policy.Safety.RequireUnsafeMarkers
  → TypeRefPrinter.Print (Phase 5)

Policy.Validation.StrictVersionChecks
  → AssemblyLoader.ValidateAssemblyIdentity (Phase 1)
```

### Logging

```
ctx.Log("category", "message")

Called from everywhere:
  - All phases: Load, Shape, NameReservation, Plan, PhaseGate, Emit
  - All Shape passes
  - All validation modules

Only logs if:
  - verboseLogging == true, OR
  - logCategories.Contains("category")
```

---

## Complete Example: List<T>

Condensed trace from CLI to file write:

```
User: dotnet run -- generate --use-new-pipeline -a System.Collections.dll -o out

Main → GenerateCommand.ExecuteNewPipelineAsync → SinglePhaseBuilder.Build

Phase 1: Load
  AssemblyLoader.LoadClosure(["System.Collections.dll"], ...)
    → ResolveClosure finds 3 assemblies (System.Collections, System.Runtime, System.Private.CoreLib)
    → new MetadataLoadContext(resolver, "System.Private.CoreLib")
  ReflectionReader.ReadAssemblies(loadContext, paths)
    For System.Collections.Generic.List`1:
      ReadType(List`1)
        → DetermineTypeKind → TypeKind.Class
        → ComputeAccessibility → Accessibility.Public
        → TypeReferenceFactory.CreateGenericParameterSymbol(T)
        → TypeReferenceFactory.Create(System.Object) for base
        → ReadMembers:
            ReadMethod(Add) → MethodSymbol { ClrName: "Add", Signature: "Add(T):void", EmitScope: ClassSurface }
            ReadProperty(Count) → PropertySymbol { ClrName: "Count", ... }
        → Creates: TypeSymbol { ClrFullName: "System.Collections.Generic.List`1", Arity: 1, ... }
  InterfaceMemberSubstitution.SubstituteClosedInterfaces
    → BuildSubstitutionMap(IList`1, IList<T>) creates map { T → T }

Phase 2: Normalize
  graph.WithIndices
    → TypeIndex["System.Collections.Generic.List`1"] = List`1 TypeSymbol

Phase 3: Shape (14 passes)
  GlobalInterfaceIndex.Build → Indexes IList<T>, ICollection<T>, IEnumerable<T>
  StructuralConformance.Analyze → List<T> has all interface methods, no ViewOnly synthesis
  InterfaceInliner.Inline → No inlining needed
  [Other passes unchanged]
  IndexerPlanner.Plan → List<T>.Item[int] set to EmitScope.Omit
  ClassSurfaceDeduplicator.Deduplicate → No duplicates

Phase 3.5: Name Reservation
  NameReservation.ReserveAllNames
    For List`1:
      Shared.ComputeTypeRequestedBase("List`1") → "List"
      ctx.Renamer.ReserveTypeName(List`1, "List", "System.Collections.Generic/internal", ...)
        → Final: "List_1"

    Reservation.ReserveMemberNamesOnly(List<T>)
      For Add:
        Shared.ComputeMemberRequestedBase("Add") → "Add"
        ScopeFactory.ClassSurface(List<T>, isStatic: false) → ".../List_1/instance"
        ctx.Renamer.ReserveMemberName(Add.StableId, "Add", instance scope, ...)
          → Final: "Add"

    Audit.AuditReservationCompleteness → All members have decisions ✓

    Application.ApplyNamesToGraph
      → Set List`1.TsEmitName = "List_1"
      → Set Add.TsEmitName = "Add"
      → Set Count.TsEmitName = "Count"

Phase 4: Plan
  ImportGraph.Build → Collect foreign types (System.Object, System.Array)
  ImportPlanner.PlanImports → Plan imports from System namespace
  EmitOrderPlanner.PlanOrder → Topological sort
  OverloadUnifier.UnifyOverloads → List<T>.Add is single overload, no unification
  InterfaceConstraintAuditor.Audit → (List<T>, IList<T>) satisfies all constraints

  PhaseGate.Validate:
    ValidationCore.ValidateTypeNames → List<T>.TsEmitName = "List_1" ✓
    ValidationCore.ValidateMemberNames → Add.TsEmitName = "Add" ✓
    [All 20+ validations pass]
    → ErrorCount: 0, validation passed

Phase 5: Emit
  SupportTypesEmit.Emit → Write _support/types.d.ts

  InternalIndexEmitter.Emit
    For System.Collections.Generic:
      EmitFileHeader → Add reference to _support/types.d.ts
      EmitImports → import type { Object } from "../System/internal"
      EmitNamespaceDeclaration → export namespace System.Collections.Generic {

      For List<T>:
        ClassPrinter.PrintClassDeclaration
          Write: export class List_1<T>
          Write: extends Object
          Write: implements IList_1<T>, ICollection_1<T>, IEnumerable_1<T> {

          For Add:
            MethodPrinter.PrintMethod
              TypeRefPrinter.Print(T) → TypeNameResolver.ResolveTypeName(T) → "T"
              Write: Add(item: T): void;

          For Count:
            Write: get Count: int;

          Write: }

      Write to: out/System.Collections.Generic/internal/index.d.ts

  FacadeEmitter.Emit
    → Write out/System.Collections.Generic/index.d.ts with: export * from "./internal"

  MetadataEmitter.Emit
    → Write out/System.Collections.Generic/metadata.json with CLR metadata

  BindingEmitter.Emit
    → Write out/System.Collections.Generic/bindings.json with name mappings

  ModuleStubEmitter.Emit
    → Write out/System.Collections.Generic/index.js stub

BuildResult { Success: true, TypeCount: 1, ... }
GenerateCommand reports success
Process exits with code 0
```

---

## Summary

Complete call chains through SinglePhase pipeline:

1. **Entry Point** - CLI → GenerateCommand → SinglePhaseBuilder
2. **Phase 1: Load** - Assembly loading, reflection, member reading
3. **Phase 2: Normalize** - Index building
4. **Phase 3: Shape** - 14 transformation passes
5. **Phase 3.5: Name Reservation** - Central naming through Renamer (6 steps)
6. **Phase 4: Plan** - Import planning, ordering, validation prep
7. **Phase 4.7: PhaseGate** - 20+ validation functions, 40+ diagnostic codes
8. **Phase 5: Emit** - File generation (TypeScript, metadata, bindings, stubs)
9. **Cross-Cutting** - SymbolRenamer, DiagnosticBag, Policy, Logging
10. **Complete Example** - Full trace for List<T>

**Key Insights:**
- Shape passes are PURE (return new graph)
- Renamer is MUTATED (accumulates decisions)
- PhaseGate validates before emission (fail-fast)
- Emit phase uses TsEmitName from graph (no further name transformation)

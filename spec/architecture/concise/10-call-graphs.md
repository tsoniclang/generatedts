# Call Graphs: Execution Flow

## Overview

Complete call chains from entry point through all phases to file output.

**Purpose:** Show who calls what, understand execution flow, trace bugs.

---

## Entry Point

```
Program.Main(string[] args)
  ↓
CommandLineParser.Parse(args) → Options
  ↓
SinglePhaseBuilder.Build(Options) → BuildResult
  ↓
[Pipeline Execution - see below]
  ↓
WriteOutput(BuildResult) → Files on disk
```

---

## Phase 1: LOAD

```
SinglePhaseBuilder.Build()
  ├─► BuildContext.Create(policy, logger)
  │     └─► SymbolRenamer.Create()
  │           └─► NameReservationTable.Create()
  │
  └─► AssemblyLoader.LoadClosure(seedPaths, refPaths)
        ├─► BuildCandidateMap(refPaths)
        │     └─► For each *.dll: AssemblyName.GetAssemblyName()
        │
        ├─► ResolveClosure(seedPaths, candidateMap)
        │     └─► BFS: PEReader.ReadMetadata()
        │
        ├─► ValidateAssemblyIdentity(resolvedPaths)
        │     ├─► Check PublicKeyToken conflicts
        │     └─► Check version drift
        │
        ├─► FindCoreLibrary(resolvedPaths)
        │
        └─► CreateLoadContext() + LoadAssemblies()
              ├─► PathAssemblyResolver.Create()
              └─► MetadataLoadContext.Create()
                    └─► For each: loadContext.LoadFromAssemblyPath()
```

```
SinglePhaseBuilder.Build() (continued)
  └─► ReflectionReader.ReadAssemblies(loadContext, assemblies)
        ├─► For each assembly:
        │     ├─► assembly.GetTypes() → Type[]
        │     ├─► For each type:
        │     │     ├─► ComputeAccessibility(type)
        │     │     ├─► Skip if non-public
        │     │     └─► ReadType(type)
        │     │           ├─► DetermineTypeKind(type)
        │     │           ├─► TypeReferenceFactory.CreateGenericParameterSymbol()
        │     │           ├─► TypeReferenceFactory.Create(baseType)
        │     │           ├─► TypeReferenceFactory.Create(iface) for each interface
        │     │           ├─► ReadMembers(type)
        │     │           │     ├─► ReadMethod()
        │     │           │     ├─► ReadProperty()
        │     │           │     ├─► ReadField()
        │     │           │     ├─► ReadEvent()
        │     │           │     └─► ReadConstructor()
        │     │           └─► ReadType(nestedType) for each nested
        │     │
        │     └─► Group by namespace
        │
        └─► Build SymbolGraph
              └─► SymbolGraph.WithIndices()
```

```
TypeReferenceFactory.Create(System.Type)
  ├─► Check cache → return cached
  ├─► Detect cycle → PlaceholderTypeReference
  └─► CreateInternal(type)
        ├─► if IsByRef → ByRefTypeReference
        ├─► if IsPointer → PointerTypeReference
        ├─► if IsArray → ArrayTypeReference
        ├─► if IsGenericParameter → GenericParameterReference
        └─► else → CreateNamed(type)
              ├─► Extract assemblyName, namespace, name
              ├─► If generic:
              │     ├─► GetGenericArguments()
              │     └─► Recursively Create() each arg
              └─► Return NamedTypeReference
```

---

## Phase 2: NORMALIZE

```
Single PhaseBuilder.Build() (continued)
  └─► SymbolGraph.WithIndices()
        ├─► Build NamespaceIndex (namespace name → NamespaceSymbol)
        ├─► Build TypeIndex (CLR full name → TypeSymbol)
        │     └─► Recursive: include nested types
        └─► Return new graph
```

---

## Phase 3: SHAPE (18 Passes)

```
SinglePhaseBuilder.Build() (continued)
  └─► ShapePhase.Transform(ctx, graph)
        ├─► Pass 1: GlobalInterfaceIndex.Build(ctx, graph)
        ├─► Pass 2: InterfaceDeclIndex.Build(ctx, graph)
        ├─► Pass 3: InterfaceInliner.Inline(ctx, graph)
        │     └─► For each interface:
        │           ├─► BFS collect inherited members
        │           ├─► BuildSubstitutionMap()
        │           ├─► SubstituteMethodMembers()
        │           ├─► SubstitutePropertyMembers()
        │           └─► Clear Interfaces array
        │
        ├─► Pass 3.5: InternalInterfaceFilter.Filter(ctx, graph)
        ├─► Pass 4: StructuralConformance.Analyze(ctx, graph)
        ├─► Pass 5: ExplicitImplSynthesizer.Synthesize(ctx, graph)
        ├─► Pass 6: InterfaceResolver.Resolve(ctx, graph)
        ├─► Pass 7: DiamondResolver.Resolve(ctx, graph)
        ├─► Pass 8: BaseOverloadAdder.AddOverloads(ctx, graph)
        ├─► Pass 9: OverloadReturnConflictResolver.Resolve(ctx, graph)
        ├─► Pass 10: MemberDeduplicator.Deduplicate(ctx, graph)
        ├─► Pass 11: ViewPlanner.Plan(ctx, graph)
        ├─► Pass 12: ClassSurfaceDeduplicator.Deduplicate(ctx, graph)
        ├─► Pass 13: HiddenMemberPlanner.Plan(ctx, graph)
        │     └─► ctx.Renamer.ReserveMemberName() for each 'new' member
        │
        ├─► Pass 14: IndexerPlanner.Plan(ctx, graph)
        │     └─► ctx.Renamer.ReserveMemberName() for get_Item/set_Item
        │
        ├─► Pass 15: FinalIndexersPass.Run(ctx, graph)
        ├─► Pass 16: StaticSideAnalyzer.Analyze(ctx, graph)
        └─► Pass 17: ConstraintCloser.Close(ctx, graph)
```

---

## Phase 3.5: NAME RESERVATION

```
SinglePhaseBuilder.Build() (continued)
  └─► NameReservation.ReserveAllNames(ctx, graph)
        ├─► For each type:
        │     ├─► Shared.ComputeTypeRequestedBase(type)
        │     └─► ctx.Renamer.ReserveTypeName(stableId, requested, scope, reason)
        │           ├─► Apply transforms (PascalCase)
        │           ├─► Sanitize reserved words
        │           ├─► Check collision → numeric suffix if needed
        │           └─► Record decision
        │
        ├─► For each member (ClassSurface):
        │     ├─► Shared.ComputeMemberRequestedBase(member)
        │     └─► ctx.Renamer.ReserveMemberName(stableId, requested, scope, reason, isStatic)
        │           ├─► Apply transforms (camelCase)
        │           ├─► Check collision → numeric suffix
        │           └─► Record decision
        │
        ├─► Rebuild class surface name sets
        │     └─► Collect all ClassSurface member names (for collision detection)
        │
        ├─► For each view member (ViewOnly):
        │     ├─► Shared.ComputeMemberRequestedBase(member)
        │     ├─► Check collision with classAllNames → append $view if collision
        │     └─► ctx.Renamer.ReserveMemberName()
        │
        ├─► Audit.AuditReservationCompleteness(ctx, graph)
        │     └─► Check every emitted member has decision
        │
        └─► Application.ApplyNamesToGraph(ctx, graph)
              ├─► For each type:
              │     ├─► ctx.Renamer.GetFinalTypeName(type)
              │     └─► Set type.TsEmitName
              │
              └─► For each member:
                    ├─► Determine scope (ClassSurface vs ViewOnly)
                    ├─► ctx.Renamer.GetFinalMemberName(stableId, scope)
                    └─► Set member.TsEmitName
```

---

## Phase 4: PLAN

```
SinglePhaseBuilder.Build() (continued)
  └─► PlanPhase.Plan(ctx, graph, loadContext)
        ├─► ImportGraph.Build(ctx, graph)
        │     ├─► Scan all TypeReferences
        │     ├─► Extract foreign namespace dependencies
        │     └─► Build dependency graph
        │
        ├─► ImportPlanner.PlanImports(ctx, graph, importGraph)
        │     ├─► Determine exports (public types)
        │     ├─► Determine imports (foreign types)
        │     ├─► Determine aliases (name collisions)
        │     ├─► Collect unresolved types (cross-assembly)
        │     └─► DeclaringAssemblyResolver.ResolveBatch(unresolvedKeys)
        │           └─► loadContext.LoadFromAssemblyName() for each
        │
        ├─► EmitOrderPlanner.PlanOrder(ctx, graph, importGraph)
        │     ├─► Build dependency graph
        │     ├─► Topological sort (DFS)
        │     └─► Alphabetical within level
        │
        └─► Return EmissionPlan
```

---

## Phase 4.5-4.7: Validation

```
SinglePhaseBuilder.Build() (continued)
  ├─► OverloadUnifier.UnifyOverloads(ctx, graph)
  │     └─► For each overload group:
  │           ├─► TsErase.EraseToTsSignature()
  │           └─► Mark duplicates Omitted
  │
  ├─► InterfaceConstraintAuditor.Audit(ctx, graph)
  │     └─► Check new() constraints
  │
  └─► PhaseGate.Validate(ctx, graph, importPlan, constraintFindings)
        ├─► Core.ValidateFinalization()
        ├─► Names.ValidateNameUniqueness()
        ├─► Views.ValidateViewIntegrity()
        ├─► Scopes.ValidateScopeIntegrity()
        ├─► Types.ValidateTypeResolution()
        ├─► ImportExport.ValidateImportsExports()
        ├─► Constraints.ValidateConstraints()
        └─► Overloads.ValidateOverloadCollisions()
              └─► ctx.Diagnostics.Error/Warning/Info()
```

```
SinglePhaseBuilder.Build() (continued)
  └─► if (ctx.Diagnostics.HasErrors())
        └─► Return BuildResult { Success = false }
```

---

## Phase 5: EMIT

```
SinglePhaseBuilder.Build() (continued)
  └─► EmitPhase.Emit(ctx, plan, outputDir)
        ├─► SupportTypesEmitter.Emit()
        │     └─► Write _support/types.d.ts
        │
        └─► For each namespace (in emission order):
              ├─► InternalIndexEmitter.Emit(ns)
              │     ├─► For each type:
              │     │     ├─► if Class: ClassPrinter.EmitClass()
              │     │     │     ├─► EmitTypeParameters()
              │     │     │     ├─► EmitBaseClass()
              │     │     │     ├─► EmitInterfaces()
              │     │     │     ├─► EmitInstanceMembers()
              │     │     │     │     ├─► MethodPrinter.EmitMethod()
              │     │     │     │     │     ├─► EmitParameters()
              │     │     │     │     │     └─► TypeRefPrinter.Print(returnType)
              │     │     │     │     ├─► PropertyPrinter.EmitProperty()
              │     │     │     │     │     └─► TypeRefPrinter.Print(propType)
              │     │     │     │     ├─► FieldPrinter.EmitField()
              │     │     │     │     └─► EventPrinter.EmitEvent()
              │     │     │     ├─► EmitStaticMembers()
              │     │     │     │     └─► LiftClassGenericsToMethod() if needed
              │     │     │     └─► EmitViews()
              │     │     │           └─► ViewPrinter.EmitView()
              │     │     │
              │     │     ├─► if Interface: InterfacePrinter.EmitInterface()
              │     │     ├─► if Enum: EnumPrinter.EmitEnum()
              │     │     └─► if Delegate: DelegatePrinter.EmitDelegate()
              │     │
              │     └─► Write {ns}/internal/index.d.ts
              │
              ├─► FacadeEmitter.Emit(ns)
              │     └─► Write {ns}/index.d.ts
              │
              ├─► MetadataEmitter.Emit(ns)
              │     └─► Write {ns}/metadata.json
              │
              ├─► BindingEmitter.Emit(ns)
              │     ├─► ctx.Renamer.GetAllDecisions()
              │     └─► Write {ns}/bindings.json
              │
              └─► ModuleStubEmitter.Emit(ns)
                    └─► Write {ns}/index.js
```

```
TypeRefPrinter.Print(TypeReference)
  ├─► if NamedTypeReference:
  │     ├─► PrintNamed(ref)
  │     │     ├─► Lookup in TypeIndex
  │     │     ├─► ctx.Renamer.GetFinalTypeName()
  │     │     ├─► If generic: recursively Print() each type arg
  │     │     └─► Format: TypeName<T1, T2, ...>
  │     │
  │     └─► Return TS type string
  │
  ├─► if GenericParameterReference:
  │     └─► Return parameter name (T, U, etc.)
  │
  ├─► if ArrayTypeReference:
  │     ├─► Recursively Print(elementType)
  │     └─► Return elementType[]
  │
  ├─► if PointerTypeReference:
  │     └─► Return "any" (TS doesn't support pointers)
  │
  └─► if ByRefTypeReference:
        └─► Recursively Print(referencedType)
```

---

## Cross-Cutting Calls

### Renamer (SymbolRenamer)

**Called by:**
- Shape Phase: HiddenMemberPlanner, IndexerPlanner
- Normalize Phase: NameReservation
- Emit Phase: All Printers (GetFinalTypeName, GetFinalMemberName)

### Diagnostics (DiagnosticBag)

**Called by:**
- All phases: Error/Warning/Info logging
- PhaseGate: Comprehensive validation (50+ checks)

### Policy (GenerationPolicy)

**Called by:**
- Normalize: Name transforms (PascalCase, camelCase)
- Shape: Indexer policy (EmitPropertyWhenSingle)
- Emit: Import style, branded primitives

### Logger

**Called by:**
- All phases: Progress reporting
- Categories: "Build", "Load", "Shape", "ViewPlanner", "PhaseGate", "Emit"

---

## Example End-to-End: List<T>

```
1. CLI: dotnet run -- generate -a System.Private.CoreLib.dll

2. LOAD Phase:
   ReflectionReader.ReadType("System.Collections.Generic.List`1")
     → TypeSymbol with ClrFullName="List`1"

3. SHAPE Phase:
   InterfaceInliner.Inline()
     → Flattens ICollection<T>, IEnumerable<T>
   BaseOverloadAdder.AddOverloads()
     → Adds base class overloads

4. NORMALIZE Phase:
   Renamer.ReserveTypeName("List`1") →
"List_1"
   Renamer.ReserveMemberName("Add") → "add"

5. PLAN Phase:
   ImportPlanner: Determines List_1 needs IEnumerable_1
   EmitOrderPlanner: IEnumerable_1 before List_1

6. PHASEGATE:
   Validates all 50+ rules → PASS

7. EMIT Phase:
   ClassPrinter.EmitClass("List_1"):
     export class List_1<T> implements ICollection_1<T>, IEnumerable_1<T> {
       add(item: T): void;
       ...
     }

8. OUTPUT:
   System.Collections.Generic/internal/index.d.ts
   System.Collections.Generic/index.d.ts
   System.Collections.Generic/metadata.json
   System.Collections.Generic/bindings.json
   System.Collections.Generic/index.js
```

---

## Summary

**Complete pipeline flow:**
1. **Entry** → SinglePhaseBuilder.Build()
2. **Load** → AssemblyLoader + ReflectionReader
3. **Normalize** → WithIndices()
4. **Shape** → 18 transformation passes
5. **Name Reservation** → NameReservation + Renamer
6. **Plan** → ImportPlanner + EmitOrderPlanner
7. **Validate** → PhaseGate (50+ rules)
8. **Emit** → 6 file types per namespace

**Cross-cutting:** Renamer (naming), Diagnostics (validation), Policy (config), Logger (progress)

**Result:** Type-safe TypeScript declarations with 100% data integrity

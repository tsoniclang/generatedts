# Semantic Analysis & Transforms

The analysis phase runs between raw reflection and the emitters.  It polishes
the type model so the generated TypeScript matches CLR semantics while avoiding
compiler errors (TS2416, TS2417, TS2320, etc.).

## Filters

- **Namespace/type filtering**: `Analysis/TypeFilters.ShouldIncludeType` removes
  non-public types, compiler-generated types, and any namespace configured in
  `GeneratorConfig.SkipNamespaces`.  Whitelists from the CLI are also enforced
  here.
- **Member filtering**: `Analysis/MemberFilters.ShouldIncludeMember` omits
  “noise” methods (`Equals`, `GetHashCode`, `GetType`, `ToString`) unless
  explicitly configured.

## Explicit interface implementations

`Analysis/ExplicitInterfaceAnalyzer` inspects interface maps and classifies
explicit implementations (non-public accessor/method).  The emitters remove such
members from the generated class/interface body but keep them in metadata so the
runtime knows the implementation exists.

## Diamond inheritance & intersection aliases

- `InterfaceAnalysis.HasDiamondInheritance` detects when a CLR interface inherits
  the same ancestor through multiple parents.
- `InterfaceAnalysis.GenerateBaseInterface` creates a `_Base` interface
  containing only the unique members for the diamonded interface.
- `InterfaceAnalysis.CreateIntersectionAlias` emits a type alias that intersects
  `_Base` with all parents, preserving the CLR surface while keeping TypeScript
  happy.
- `InterfaceAnalysis.PruneRedundantInterfaceExtends` removes duplicate parents
  implied by other extends clauses.

## Property covariance & hiding

`Emit/PropertyEmitter.ApplyCovariantWrapperIfNeeded`:

1. Compares the mapped type of the derived property against base class properties
   (walking the entire base chain).  If the types differ, the property is emitted
   as `Covariant<TSpecific, TContract>` to satisfy TS2416.
2. For readonly properties implementing interfaces, compares the concrete type
   against the interface contract and applies the same wrapper when necessary.
3. `PropertyEmitter.IsRedundantPropertyRedeclaration` skips duplicate
   declarations when a base property already produces the same mapped type.

## Static member compatibility

`Analysis/OverloadBuilder.AddBaseClassCompatibleOverloads` ensures that the
static side of derived classes exposes the same signatures as the base class:

- Uses `EnumerateBaseStaticMethods` to pull both constructed-type and type-definition
  methods (capturing method-level generics).
- Filters out signatures that reference the derived class’s type parameters via
  `TypeReferenceChecker.TypeReferencesAnyTypeParam` (avoids TS2302).
- Adds both generic and non-generic overloads to the derived class.

## Interface-compatible overloads

`OverloadBuilder.AddInterfaceCompatibleOverloads` inspects interface maps and
injects any missing signatures into the class, covering covariant return types
and explicitly-implemented methods.

## Dependency tracking

- `Analysis/DependencyHelpers.TrackTypeDependency` records every external type
  reference (including generic arguments, array element types, by-ref/pointers).
- The data is persisted by `Pipeline/DependencyTracker` and used by the renderer
  to emit `import type` statements and `.dependencies.json`.

Together, these transforms make the emit phase straightforward: by the time a
type reaches an emitter it already models the CLR behaviour in a way TypeScript
can consume without errors.

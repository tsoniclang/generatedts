# Phase 4: Shape - CLR to TypeScript Semantic Transformation

## Overview

**Shape phase** transforms CLR semantics → TypeScript-compatible semantics via **16 sequential transformation passes**. Operates on normalized `SymbolGraph` from Phase 3.

**Key Responsibilities**:
- Flatten interface hierarchies (remove `extends`)
- Synthesize missing interface members
- Resolve diamond inheritance, add base overloads
- Deduplicate members, plan explicit interface views
- Handle indexers, static-side conflicts, generic constraints

**PURE Transformations**: All passes return new immutable `SymbolGraph` instances.

---

## Pass 1: GlobalInterfaceIndex

**File**: `GlobalInterfaceIndex.cs`

### Purpose
Build cross-assembly interface indexes: `GlobalInterfaceIndex` (all signatures) and `InterfaceDeclIndex` (declared-only).

### Public API

**`GlobalInterfaceIndex.Build(BuildContext ctx, SymbolGraph graph)`**
- Index ALL public interfaces across assemblies
- Compute method/property signatures per interface
- Store in `_globalIndex[ClrFullName]`

**`GetInterface(string fullName)`** - Lookup interface by full name

**`ContainsInterface(string fullName)`** - Check existence

### Data Structures

```csharp
public record InterfaceInfo(
    TypeSymbol Symbol,
    string FullName,
    string AssemblyName,
    HashSet<string> MethodSignatures,
    HashSet<string> PropertySignatures)
```

---

## Pass 2: InterfaceDeclIndex

**File**: `GlobalInterfaceIndex.cs`

### Purpose
Index interface members that are DECLARED (not inherited). Used to resolve which interface actually declares a member.

### Public API

**`InterfaceDeclIndex.Build(BuildContext ctx, SymbolGraph graph)`**
- Collect inherited signatures from base interfaces
- Filter to declared-only members
- Store in `_declIndex[ClrFullName]`

**`DeclaresMethod/Property(string ifaceFullName, string canonicalSig)`** - Check if interface declares member

### Data Structures

```csharp
public record DeclaredMembers(
    string InterfaceFullName,
    HashSet<string> MethodSignatures,
    HashSet<string> PropertySignatures)
```

---

## Pass 3: StructuralConformance

**File**: `StructuralConformance.cs`

### Purpose
Analyze structural conformance for interfaces. Synthesize ViewOnly members for interfaces that can't be structurally implemented on class surface.

### Public API

**`StructuralConformance.Analyze(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each class/struct implementing interfaces:
   - Build class surface (representable members, exclude ViewOnly)
   - For each interface:
     - Build substituted interface surface (flattened + type args)
     - Check TS assignability: `classSurface.IsTsAssignableMethod/Property(ifaceMember)`
     - For missing: synthesize ViewOnly member with interface StableId
2. Add synthesized ViewOnly members to type immutably
3. Return new SymbolGraph

**Key**: Uses interface member's StableId (NOT class StableId) to prevent ID conflicts.

**Synthesized member**:
```csharp
new MethodSymbol {
    StableId = ifaceMethod.StableId,  // From interface!
    Provenance = MemberProvenance.ExplicitView,
    EmitScope = EmitScope.ViewOnly,
    SourceInterface = declaringInterface
}
```

---

## Pass 4: InterfaceInliner

**File**: `InterfaceInliner.cs`

### Purpose
Flatten interface hierarchies - remove `extends` chains. Copy all inherited members into each interface.

**Why?** TypeScript `extends` causes variance issues. Safer to flatten.

### Public API

**`InterfaceInliner.Inline(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each interface:
   - BFS traversal of base interfaces
   - Collect all members (methods, properties, events)
   - Deduplicate by canonical signature
   - Clear `Interfaces` array (no more extends)
2. Return updated graph

### FIX D: Generic Parameter Substitution

**Purpose**: Substitute type parameters when flattening generic interface hierarchies.

**Problem Without FIX D**:
```csharp
// C#:
interface IBase<T> { T GetValue; }
interface IDerived : IBase<string> { }

// WITHOUT FIX D:
interface IDerived {
  GetValue: T;  // ERROR: T orphaned
}

// WITH FIX D:
interface IDerived {
  GetValue: string;  // CORRECT
}
```

**What FIX D Handles**:
1. Direct substitution: `ICollection<T>` → `ICollection<string>`
2. Nested generics: `ICollection<KeyValuePair<TKey, TValue>>`
3. Chained substitution: Grandparent generics through parent
4. Method-level generic protection: Don't substitute method's own type params

**Key Methods**:

#### `BuildSubstitutionMapForInterface(TypeSymbol baseIface, TypeReference baseIfaceRef)`
```csharp
// Input: ICollection<T>, ICollection<string>
// Output: { "T" -> string }
```

Maps interface generic parameter names to actual type arguments.

#### `ComposeSubstitutions(parent, current)`
Composes two substitution maps for chained generics (grandparent → parent → child).

#### `SubstituteMethodMembers(methods, substitutionMap)`
Apply substitution to method signatures.

**CRITICAL**: Filters out method-level generic parameters:
```csharp
var methodLevelParams = new HashSet<string>(method.GenericParameters.Select(gp => gp.Name));
var filteredMap = substitutionMap.Where(kv => !methodLevelParams.Contains(kv.Key));
```

**Why**: Method's own generics must NOT be substituted.

#### `SubstitutePropertyMembers(properties, substitutionMap)`
Apply substitution to property types and index parameters.

#### `SubstituteEventMembers(events, substitutionMap)`
Apply substitution to event handler types.

**Complete Example**:
```csharp
// IList<string> : ICollection<string> : IEnumerable<string>
// Result: IList<string> gets all members with T → string substituted
```

**Impact**: Eliminates orphaned generic parameter errors in flattened interfaces.

---

## Pass 4.5: InternalInterfaceFilter

**File**: `InternalInterfaceFilter.cs`

### Purpose
Filter internal BCL interfaces from type interface lists. Internal interfaces are BCL implementation details causing TS2304 errors.

### Public API

**`InternalInterfaceFilter.FilterGraph(BuildContext ctx, SymbolGraph graph)`**
- Iterate all types, filter internal interfaces
- Log removed count
- Return new SymbolGraph with filtered lists

**`FilterInterfaces(BuildContext ctx, TypeSymbol type)`**
- Filter internal interfaces from single type
- Check `IsInternalInterface(iface)`
- Return updated type or original if none removed

### Pattern Matching

**`IsInternalInterface(TypeReference typeRef)`**
1. Check explicit list: `ExplicitInternalInterfaces.Contains(fullName)`
2. Check patterns: `name.Contains(pattern)` on simple name

**InternalPatterns**:
```csharp
{ "Internal", "Debugger", "ParseAndFormatInfo", "Runtime",
  "StateMachineBox", "SecurePooled", "BuiltInJson", "DeferredDisposable" }
```

**ExplicitInternalInterfaces**:
```csharp
{ "System.Runtime.Intrinsics.ISimdVector`2",
  "System.IUtfChar`1",
  "System.Collections.Immutable.IStrongEnumerator`1",
  "System.Runtime.CompilerServices.ITaskAwaiter", ... }
```

**Why patterns on simple name?** Prevents false positives like filtering `System.Runtime.InteropServices.ISerializable`.

**Impact**: Eliminated 75 TS2304 errors (75.8% reduction).

---

## Pass 5: ExplicitImplSynthesizer

**File**: `ExplicitImplSynthesizer.cs`

### Purpose
Synthesize missing interface members for classes/structs. Ensures all interface-required members exist. In C#, explicit interface implementations (EII) are invisible on class.

### Public API

**`ExplicitImplSynthesizer.Synthesize(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each class/struct:
   - Collect required members: `CollectInterfaceMembers(ctx, graph, type)`
   - Find missing: `FindMissingMembers(ctx, type, requiredMembers)`
   - Synthesize missing methods/properties with interface StableId
   - Deduplicate by StableId (multiple interfaces may require same member)
   - Add to type immutably
2. Return updated graph

**Key Fix**: Compare by StableId directly (not re-canonicalizing signatures).

---

## Pass 6: InterfaceResolver

**File**: `InterfaceResolver.cs`

### Purpose
Resolve interface members to declaring interface. Determines which interface in inheritance chain actually declares a member.

### Public API

**`InterfaceResolver.FindDeclaringInterface(TypeReference closedIface, string memberCanonicalSig, bool isMethod, BuildContext ctx)`**

**Algorithm**:
1. Build inheritance chain: `BuildInterfaceChain(closedIface, ctx)` (top-down order)
2. Walk chain from ancestors to immediate
3. Check `InterfaceDeclIndex.DeclaresMethod/Property(ifaceDefName, memberCanonicalSig)`
4. Pick winner: most ancestral if multiple candidates
5. Cache result

**Returns**: Closed interface reference declaring member, or null.

---

## Pass 7: DiamondResolver

**File**: `DiamondResolver.cs`

### Purpose
Resolve diamond inheritance conflicts. When multiple paths bring same method with different signatures, ensure all variants available.

**Diamond Pattern**:
```
    IBase
   /    \
  IA    IB
   \    /
   Class
```

### Public API

**`DiamondResolver.Resolve(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. Check policy: `ctx.Policy.Interfaces.DiamondResolution`
   - If `Error` → analyze and report
2. For each type: group methods by CLR name in each EmitScope
3. For groups with multiple signatures → diamond conflict detected
4. Log conflicts (PhaseGate validates)
5. Return graph unchanged (detection only)

---

## Pass 8: BaseOverloadAdder

**File**: `BaseOverloadAdder.cs`

### Purpose
Add base class overloads when derived class differs. In TypeScript, all overloads must be present on derived class.

**Example**:
```csharp
class Base { void M(int x) { } void M(string s) { } }
class Derived : Base { override void M(int x) { } }
// TS error: missing M(string) - must add base overload
```

### Public API

**`BaseOverloadAdder.AddOverloads(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each class with base type:
   - Find base class
   - Group methods by name (derived + base)
   - For each base method name:
     - If derived doesn't override → skip
     - For each base method:
       - Check if derived has exact signature (by StableId)
       - If missing → synthesize with `CreateBaseOverloadMethod`
   - Deduplicate, validate, add to derived
2. Return updated graph

**Synthesized member**:
```csharp
new MethodSymbol {
    StableId = new MemberStableId { ... },  // Derived location
    ClrName = baseMethod.ClrName,
    ReturnType = baseMethod.ReturnType,
    Provenance = MemberProvenance.BaseOverload,
    EmitScope = EmitScope.ClassSurface
}
```

---

## Pass 9: OverloadReturnConflictResolver

**File**: `OverloadReturnConflictResolver.cs`

### Purpose
Detect return-type conflicts in overloads. TypeScript doesn't support overloads differing only in return type.

**Example**:
```csharp
// C# allows:
int GetValue(string key);
string GetValue(string key);  // Different return
// TypeScript DOESN'T allow this!
```

### Public API

**`OverloadReturnConflictResolver.Resolve(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each type, per EmitScope:
   - Group methods by signature excluding return: `GetSignatureWithoutReturn(ctx, m)`
   - For groups with multiple methods:
     - Get distinct return types
     - If multiple → conflict detected, log
2. Return graph unchanged (detection only)

---

## Pass 10: MemberDeduplicator

**File**: `MemberDeduplicator.cs`

### Purpose
Final deduplication to remove duplicates introduced by multiple Shape passes. Keeps first occurrence by StableId.

### Public API

**`MemberDeduplicator.Deduplicate(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each namespace → type:
   - Deduplicate methods, properties, fields, events, constructors by StableId
   - Use `HashSet<StableId>` to track seen
2. Return new graph with unique members

---

## Pass 11: ViewPlanner

**File**: `ViewPlanner.cs`

### Purpose
Plan explicit interface views (As_IInterface properties). Creates views for interfaces that couldn't be structurally implemented.

### Public API

**`ViewPlanner.Plan(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each class/struct:
   - Collect ALL ViewOnly members with SourceInterface
   - Group by interface StableId
   - For each interface:
     - Collect ViewMembers: `new ViewMember(Kind, StableId, ClrName)`
     - Create ExplicitView:
       ```csharp
       new ExplicitView(
           InterfaceReference: ifaceRef,
           ViewPropertyName: CreateViewName(ifaceRef),
           ViewMembers: viewMembers)
       ```
   - Attach views to type: `t.WithExplicitViews(plannedViews)`
2. Return updated graph

**View names**: `IDisposable` → `As_IDisposable`, `IEnumerable<string>` → `As_IEnumerable_1_of_string`

### Data Structures

```csharp
public record ExplicitView(
    TypeReference InterfaceReference,
    string ViewPropertyName,
    ImmutableArray<ViewMember> ViewMembers)

public record ViewMember(
    ViewMemberKind Kind,  // Method, Property, Event
    MemberStableId StableId,
    string ClrName)
```

---

## Pass 12: ClassSurfaceDeduplicator

**File**: `ClassSurfaceDeduplicator.cs`

### Purpose
Deduplicate class surface by emitted name (post-camelCase). When multiple properties emit to same name, keep most specific, demote others to ViewOnly.

**Example**:
```csharp
class Foo {
    object Current { get; }   // IEnumerator.Current
    string Current { get; }   // IEnumerator<string>.Current
}
// Both emit to "current" → keep string, demote object to ViewOnly
```

### Public API

**`ClassSurfaceDeduplicator.Deduplicate(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each type:
   - Group class-surface properties by emitted name (camelCase)
   - For duplicate groups:
     - Pick winner: `PickWinner(candidates)`
     - Demote losers to ViewOnly
2. Return updated graph

**PickWinner preference**:
1. Non-explicit over explicit
2. Generic over non-generic
3. Narrower type over `object`
4. Stable ordering

---

## Pass 13: HiddenMemberPlanner

**File**: `HiddenMemberPlanner.cs`

### Purpose
Plan C# 'new' hidden members. When derived hides base with 'new', reserve renamed version (e.g., `Method_new`) via Renamer.

### Public API

**`HiddenMemberPlanner.Plan(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each class/struct with base type:
   - Find methods marked `IsNew`
   - Reserve renamed version: `ctx.Renamer.ReserveMemberName(stableId, clrName + "_new", scope, "HiddenNewConflict", isStatic)`
2. DOES NOT modify graph (pure planning - Renamer handles names)

---

## Pass 14: IndexerPlanner

**File**: `IndexerPlanner.cs`

### Purpose
Plan indexer representation (property vs methods).
- Single uniform indexer → keep as property
- Multiple/heterogeneous → convert to get/set methods

**Policy**: `ctx.Policy.Indexers.EmitPropertyWhenSingle`

### Public API

**`IndexerPlanner.Plan(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each type with indexers:
   - If 1 indexer AND `EmitPropertyWhenSingle` → keep as property
   - Otherwise → convert ALL to methods:
     - `get_Item(index)`, `set_Item(index, value)`
     - Remove indexer properties
2. Return updated graph

---

## Pass 15: FinalIndexersPass

**File**: `FinalIndexersPass.cs`

### Purpose
Final, definitive pass to enforce indexer policy. Ensures no indexer properties leak through.

**Invariant**:
- 0 indexers → nothing
- 1 indexer → keep ONLY if `policy.EmitPropertyWhenSingle == true`
- ≥2 indexers → convert ALL to methods

### Public API

**`FinalIndexersPass.Run(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each type with indexers:
   - Check invariant
   - If needs conversion:
     - Emit INFO diagnostic: `DiagnosticCodes.IndexerConflict`
     - Synthesize get/set methods
     - Remove indexer properties
2. Return updated graph

---

## Pass 16: StaticSideAnalyzer

**File**: `StaticSideAnalyzer.cs`

### Purpose
Analyze static-side inheritance issues. TypeScript doesn't allow static side of class to extend static side of base, causing TS2417 errors.

**Policy**: `ctx.Policy.StaticSide.Action` (Analyze, AutoRename, Error)

### Public API

**`StaticSideAnalyzer.Analyze(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. For each class with base type:
   - Collect static members (derived + base)
   - Find name conflicts
   - Apply policy action:
     - `Error` → emit error diagnostic
     - `AutoRename` → `RenameConflictingStatic(ctx, ...)` (adds `_static` suffix via Renamer)
     - `Analyze` → emit warning
2. DOES NOT modify graph (uses Renamer for renames)

---

## Pass 17: ConstraintCloser

**File**: `ConstraintCloser.cs`

### Purpose
Close generic constraints for TypeScript. Resolve raw `System.Type` constraints → `TypeReference`s, validate compatibility.

**Policy**: `ctx.Policy.Constraints.MergeStrategy`

### Public API

**`ConstraintCloser.Close(BuildContext ctx, SymbolGraph graph)`**

**Algorithm**:
1. Resolve constraints: `ResolveAllConstraints(ctx, graph)` (raw types → TypeReferences)
2. For each type:
   - Close type-level generic parameters: `CloseConstraints(ctx, gp)`
   - Close method-level generic parameters
3. Validate constraints: check incompatible/unrepresentable
4. Return updated graph

**Checks**:
- Both `struct` and `class` constraints → warning
- Unrepresentable types (pointers, byrefs) → warning

---

## Pass Order and Dependencies

**CRITICAL: Passes MUST run in exact order**:

1. **GlobalInterfaceIndex** - Build global index (required by all)
2. **InterfaceDeclIndex** - Build declared-only index
3. **InterfaceInliner** - Flatten hierarchies BEFORE conformance
4. **StructuralConformance** - Synthesize ViewOnly members
5. **ExplicitImplSynthesizer** - Synthesize missing EII
6. **InterfaceResolver** - Resolve declaring interfaces
7. **DiamondResolver** - Detect diamonds AFTER synthesis
8. **BaseOverloadAdder** - Add base overloads AFTER diamonds
9. **OverloadReturnConflictResolver** - Detect return conflicts
10. **MemberDeduplicator** - Remove duplicates BEFORE view planning
11. **ViewPlanner** - Plan views AFTER all ViewOnly synthesized
12. **ClassSurfaceDeduplicator** - Deduplicate by emitted name
13. **HiddenMemberPlanner** - Plan 'new' hidden members
14. **IndexerPlanner** - Convert indexers
15. **FinalIndexersPass** - Final indexer enforcement
16. **StaticSideAnalyzer** - Analyze static conflicts
17. **ConstraintCloser** - Close constraints (final pass)

**Key Dependencies**:
- Passes 4-5 depend on 3 (flattened interfaces)
- Pass 6 depends on 1-2 (global indexes)
- Pass 11 depends on 4-5 (ViewOnly members)
- Pass 12 depends on 11 (can safely demote)
- Pass 15 depends on 14 (ensures no leaks)

---

## Key Transformations Summary

### Interface Flattening
```typescript
// Before: interface IEnumerable<T> extends IEnumerable { }
// After:  interface IEnumerable_1<T> { GetEnumerator: IEnumerator_1<T>; GetEnumerator: IEnumerator; }
```

### ViewOnly Synthesis
```csharp
// Before: class Decimal : IConvertible { } // Missing ToBoolean
// After:  class Decimal { [ViewOnly] ToBoolean(provider): boolean }
```

### Explicit Views
```typescript
class Decimal {
    As_IConvertible: { toBoolean(provider): boolean; toByte(provider): byte; }
}
```

### Base Overload Addition
```typescript
// Before: class Derived { method(x: int): void }
// After:  class Derived { method(x: int): void; method(s: string): void }
```

### Indexer Conversion
```typescript
// Before: class Array<T> { [indexer] this[int]: T }
// After:  class Array_1<T> { get_Item(index: int): T; set_Item(index: int, value: T): void }
```

### Class Surface Deduplication
```typescript
// Before: class Enumerator<T> { current: object; current: T }
// After:  class Enumerator_1<T> { current: T; As_IEnumerator: { current: object } }
```

---

## Output

**Shape phase produces**:
- Flattened interfaces (no `extends`)
- ViewOnly members synthesized
- Explicit views planned (As_IInterface properties)
- Base overloads added
- Indexers converted to methods
- Diamond/return-type conflicts detected
- Static-side issues analyzed/renamed
- Generic constraints resolved
- Clean graph ready for Renaming and Emit

**Next Phase**: Renaming (reserve all names, apply transformations)

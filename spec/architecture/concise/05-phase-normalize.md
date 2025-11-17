# Phase 5: Normalize - Name Reservation and Signature Unification

## Overview

**Normalize phase** runs after Shape (Phase 4), before Plan (Phase 6). Two primary responsibilities:

1. **Name Reservation**: Assign final TypeScript names to all types/members via central Renamer
2. **Overload Unification**: Unify method overloads TypeScript cannot distinguish

**Key Characteristics**:
- Pure transformation - returns new graph with `TsEmitName` set
- Dual-scope algorithm (class surface vs view surface)
- Collision detection with `$view` suffix for view members
- Unifies overloads differing by ref/out or constraints
- Validates completeness (every emitted member has rename decision)

---

## File: NameReservation.cs

### Purpose
Orchestrates entire name reservation. **ONLY** place where names are reserved - all other components use `Renamer.GetFinal*` to retrieve.

### Method: `ReserveAllNames(BuildContext, SymbolGraph) -> SymbolGraph`

**Algorithm**:

1. **Reserve Type Names**:
   - Iterate namespaces/types (deterministic order)
   - Compute base: `Shared.ComputeTypeRequestedBase`
   - Reserve: `Renamer.ReserveTypeName` with namespace scope
   - Skip compiler-generated (`<`, `>` in name)

2. **Reserve Class Surface Member Names**:
   - Call `Reservation.ReserveMemberNamesOnly` per type
   - Reserve methods, properties, fields, events, constructors
   - Use class scope: `ScopeFactory.ClassSurface(type, isStatic)`
   - Skip `ViewOnly` (separate), skip `Omitted` (don't need names)

3. **Rebuild Class Surface Name Sets**:
   - After reservation, collect ALL class-surface names
   - Separate sets: instance and static
   - Union set (`classAllNames`) for collision detection
   - **Critical**: Include members renamed by earlier passes

4. **Reserve View Member Names**:
   - Call `Reservation.ReserveViewMemberNamesOnly` per type with views
   - Pass `classAllNames` for collision detection
   - Use view scope: `ScopeFactory.ViewSurface(type, interfaceStableId, isStatic)`
   - Apply `$view` suffix if collides with class surface

5. **Post-Reservation Audit**:
   - Call `Audit.AuditReservationCompleteness` - verify every emitted member has decision
   - Throw if missing (fail-fast)

6. **Apply Names to Graph**:
   - Call `Application.ApplyNamesToGraph` - create new graph
   - Set `TsEmitName` on all types/members
   - Return pure transformation

**Class Surface vs View Surface**:

- **Class Surface**: Members on class declaration
  - Scope: `ScopeFactory.ClassSurface(type, isStatic)`
  - Names unique within instance/static scope

- **View Surface**: Members on interface views only
  - Scope: `ScopeFactory.ViewSurface(type, interfaceStableId, isStatic)`
  - Separate scope per interface
  - Names can differ from class (using `$view` if collision)

**Static vs Instance Scopes**:
TypeScript has separate namespaces for instance/static:
```typescript
class Example {
    static foo: string;  // Static scope
    foo: number;         // Instance scope - NO COLLISION!
}
```

Renamer uses separate scopes:
- Class instance: `ClassSurface(type, isStatic: false)`
- Class static: `ClassSurface(type, isStatic: true)`
- View instance: `ViewSurface(type, interfaceId, isStatic: false)`
- View static: `ViewSurface(type, interfaceId, isStatic: true)`

---

## File: Naming/Reservation.cs

### Purpose
Core name reservation logic - reserves through Renamer without mutating symbols.

### Method: `ReserveMemberNamesOnly(BuildContext, TypeSymbol) -> (int Reserved, int Skipped)`

**Algorithm**:
1. Create base scope: `ScopeFactory.ClassBase(type)`
2. Iterate all members (deterministic order by ClrName):
   - **Skip if `ViewOnly`**: Handled in view-scoped reservation
   - **Skip if `Omitted`**: Doesn't need name
   - **Throw if `Unspecified`**: Developer mistake (must be set in Shape)
   - **Skip if already renamed**: Check `Renamer.TryGetDecision` with class scope
   - **Compute requested base**: `Shared.ComputeMethodBase` or `Shared.RequestedBaseForMember`
   - **Reserve**: `Renamer.ReserveMemberName` with base scope
3. Return (Reserved count, Skipped count)

**Collision Detection**: Renamer appends numeric suffixes (`toInt`, `toInt2`, `toInt3`).

### Method: `ReserveViewMemberNamesOnly(BuildContext, SymbolGraph, TypeSymbol, HashSet<string>) -> (int Reserved, int Skipped)`

**Purpose**: Reserve view member names in separate view-scoped namespaces.

**Algorithm**:
1. Check for views: return (0, 0) if none
2. For each view (deterministic order by interface StableId):
   - Create view base scope: `ScopeFactory.ViewBase(type, interfaceStableId)`
   - For each ViewOnly member:
     - Verify `EmitScope = ViewOnly`
     - Find `isStatic`: `Shared.FindMemberIsStatic`
     - Compute requested base: `Shared.RequestedBaseForMember(clrName)`
     - **Peek at final name**: `Renamer.PeekFinalMemberName` in view scope
     - **Check collision**: Does peek exist in `classAllNames`?
     - **Apply suffix if collision**: Use `requested + "$view"` (or `$view2` if taken)
     - **Reserve in view scope**: `Renamer.ReserveMemberName` with view base scope
3. Return (Reserved count, Skipped count)

**View-vs-Class Collision Example**:
```csharp
// Peek at what view would get
var peek = ctx.Renamer.PeekFinalMemberName(viewScope, "getEnumerator", false);
// peek = "getEnumerator"

// Check collision
var collided = classAllNames.Contains(peek);
// collided = true (class already has "getEnumerator")

// Apply $view suffix
var finalRequested = "getEnumerator$view";

// Reserve
ctx.Renamer.ReserveMemberName(stableId, finalRequested, viewScope, ...);
```

**Why Separate View Scopes?** Class can implement same interface member differently through explicit interface implementation.

---

## File: Naming/Application.cs

### Purpose
Apply reserved names from Renamer to symbol graph. Pure transformation creating new graph with `TsEmitName` set.

### Method: `ApplyNamesToGraph(BuildContext, SymbolGraph) -> SymbolGraph`

**Algorithm**:
- Iterate namespaces → `ApplyNamesToNamespace`
- Return new graph with updated namespaces
- Call `graph.WithIndices` to rebuild lookups

### Private Method: `ApplyNamesToMembers(BuildContext, TypeSymbol, TypeMembers, TypeScope) -> TypeMembers`

**Algorithm**:

For each member:

1. **ViewOnly Members** (methods, properties):
   - Get interface StableId from `member.SourceInterface`
   - Create view scope: `ScopeFactory.ViewSurface(declaringType, interfaceStableId, isStatic)`
   - Get name: `Renamer.GetFinalMemberName(stableId, viewScope)`

2. **ClassSurface Members**:
   - Create class scope: `ScopeFactory.ClassSurface(declaringType, isStatic)`
   - Get name: `Renamer.GetFinalMemberName(stableId, classScope)`

3. Return new member with `TsEmitName` set

**Critical**: Must use **exact same scopes** as reservation to ensure names match.

---

## File: Naming/Audit.cs

### Purpose
Verify completeness - ensures every emitted type/member has rename decision in appropriate scope.

### Method: `AuditReservationCompleteness(BuildContext, SymbolGraph) -> void`

**Algorithm**:
1. Iterate namespaces/types:
   - Skip compiler-generated
   - Verify type name reserved in namespace scope
   - Call `AuditClassSurfaceMembers` for class members
   - Call `AuditViewSurfaceMembers` for view members
2. Collect errors: missing rename decisions with context
3. Report results:
   - Log audit metrics
   - Throw if errors found (fail-fast)
   - Show first 10 errors

**Throws**: `InvalidOperationException` if any types/members missing decisions.

### Private Methods

**`AuditClassSurfaceMembers`**:
- Filter to `EmitScope.ClassSurface` members
- Create class scope: `ScopeFactory.ClassSurface(type, isStatic)`
- Check decision exists: `Renamer.TryGetDecision(stableId, scope, out _)`
- Add error if missing (code PG_FIN_003)

**`AuditViewSurfaceMembers`**:
- Iterate `ExplicitViews`
- For each ViewMember:
   - Find `isStatic` from member symbol
   - Create view scope: `ScopeFactory.ViewSurface(type, interfaceStableId, isStatic)`
   - Check decision exists
   - Add error if missing (with view name and interface)

---

## File: Naming/Shared.cs

### Purpose
Utilities for name computation and sanitization. Ensures consistent transformation.

### Key Methods

**`ComputeTypeRequestedBase(string clrName) -> string`**

Transformations:
1. Replace `+` with `_` (nested: `Outer+Inner` → `Outer_Inner`)
2. Replace `` ` `` with `_` (arity: `List`1` → `List_1`)
3. Replace invalid TS chars: `<`, `>`, `[`, `]` → `_`
4. Apply reserved word sanitization

**`ComputeMethodBase(MethodSymbol method) -> string`**

Handles operators and accessors:
- Operator mapping: `op_Addition` → `add`, `op_Equality` → `equals`
- Accessors (`get_`, `set_`) use CLR name as-is
- Regular methods: `SanitizeMemberName`

**`SanitizeMemberName(string name) -> string`**

Transformations:
1. Replace invalid chars: `<`, `>`, `[`, `]`, `+` → `_`
2. Apply reserved word sanitization

**`IsCompilerGenerated(string clrName) -> bool`**

Detection: Names containing `<` or `>`

**`FindMemberIsStatic(TypeSymbol type, ViewMember viewMember) -> bool`**

Find whether view member is static by looking up in type's member collection.

---

## File: OverloadUnifier.cs

### Purpose
Unify method overloads differing only in ways TypeScript can't distinguish (ref/out, constraints).

### Method: `UnifyOverloads(BuildContext, SymbolGraph) -> SymbolGraph`

**Algorithm**:
1. Iterate namespaces/types
2. Call `UnifyTypeOverloads` per type
3. Return new graph with unified overloads

### Private Method: `UnifyTypeOverloads(TypeSymbol type) -> (TypeSymbol, int)`

**Algorithm**:
1. **Group Methods by Erasure Key**:
   - Filter to `ClassSurface`/`StaticSurface` methods
   - Group by `ComputeErasureKey` (name|arity|paramCount)
   - Keep only groups with 2+ methods
2. **For Each Group**:
   - Call `SelectWidestSignature` to pick best
   - Mark others as `EmitScope.Omitted`
3. Return (updated type, unified count)

### Private Method: `ComputeErasureKey(MethodSymbol method) -> string`

**Format**: `"name|arity|paramCount"`

**Example**:
```csharp
// void Write(ref int value) → "write|0|1"
// void Write(int value)     → "write|0|1"
// COLLISION: Same erasure key!
```

### Private Method: `SelectWidestSignature(List<MethodSymbol> overloads) -> MethodSymbol`

**Preference Order**:
1. Fewer ref/out parameters (TypeScript doesn't support)
2. Fewer generic constraints (TypeScript has weaker constraints)
3. First in declaration order (stable tie-breaker)

**Example**:
```csharp
// void Write(int value)                    RefOut=0, Constraints=0 ← Winner
// void Write(ref int value)                RefOut=1, Constraints=0
// void Write<T>(T value) where T : struct  RefOut=0, Constraints=1
```

---

## File: SignatureNormalization.cs

### Purpose
Canonical signatures for complete member matching. Used by BindingEmitter, MetadataEmitter, StructuralConformance, ViewPlanner.

### Method: `NormalizeMethod(MethodSymbol method) -> string`

**Format**: `"MethodName|arity=N|(param1:kind,param2:kind)|->ReturnType|static=bool"`

**Example**: `"Parse|arity=0|(string:in,int:out)|->int|static=true"`

**Components**:
1. Method name (CLR name)
2. Generic arity: `arity=N`
3. Parameters with kinds: `in`, `out`, `ref`, `params`, `?` (optional)
4. Return type: `->` + normalized type name
5. Static flag: `static=true/false`

### Method: `NormalizeProperty(PropertySymbol property) -> string`

**Format**: `"PropertyName|(indexParam1,indexParam2)|->PropertyType|static=bool|accessor=get/set/getset"`

**Examples**:
- `"Count|->int|static=false|accessor=get"`
- `"Item|(int)|->T|static=false|accessor=getset"`

---

## Key Algorithms

### Dual-Scope Naming (Class vs View)

Sophisticated dual-scope algorithm: single CLR member can have different TS names depending on access location.

**Class Surface Scope**:
- Members emitted on class declaration
- Scope key: `ns:TypeStableId#instance` or `ns:TypeStableId#static`
- Collision resolution: Numeric suffixes (`toInt`, `toInt2`)

**View Surface Scope**:
- Members emitted on interface views
- Scope key: `ns:TypeStableId:InterfaceStableId#instance` or `ns:TypeStableId:InterfaceStableId#static`
- Separate scope per interface
- Collision resolution: `$view` suffix first, then numeric (`toInt$view`, `toInt$view2`)

**Example**:
```typescript
class Array_1<T> {
    // Class surface (ClassSurface scope)
    getEnumerator: ArrayEnumerator;

    // View for IEnumerable<T> (View scope)
    readonly asIEnumerable_1: {
        getEnumerator$view: IEnumerator_1<T>;
    };

    // View for IEnumerable (different View scope)
    readonly asIEnumerable: {
        getEnumerator$view: IEnumerator;  // Same $view name (different view scope)
    };
}
```

### Collision Detection and Resolution

**Class Surface Collisions**:
1. First member: `toInt`
2. Subsequent: `toInt2`, `toInt3`
3. Renamer maintains counters per scope

**View-vs-Class Collisions**:
1. Peek: `Renamer.PeekFinalMemberName(viewScope, requested, isStatic)`
2. Check if peek in `classAllNames` set
3. If collision: apply `$view` suffix
4. If `$view` taken in view scope: try `$view2`, `$view3`

---

## Pipeline Integration

**Position**: Between Shape and Plan

```
Shape Phase (Phase 4) → Sets EmitScope, plans views, marks ViewOnly
  ↓
Normalize Phase (Phase 5) ← HERE
  ↓ Reserves names, sets TsEmitName, unifies overloads
Plan Phase (Phase 6) → Uses TsEmitName for emission planning
```

**Key Invariants**:
- **Input**: All members have `EmitScope` set (Unspecified = error)
- **Input**: ViewOnly members have `SourceInterface` set
- **Output**: All emitted members have `TsEmitName` set
- **Output**: All emitted members have rename decision in Renamer
- **Output**: Colliding overloads unified

---

## Summary

Normalize phase is **central name assignment** that:

1. **Reserves Names**: All TS names via central Renamer
2. **Dual-Scope Algorithm**: Separate scopes for class/view
3. **Collision Detection**: Class-vs-view collisions resolved with `$view`
4. **Overload Unification**: Indistinguishable overloads unified to widest
5. **Completeness Validation**: Audit ensures every member has decision

**Design Principles**:
- **Centralized Reservation**: Only Normalize reserves - others retrieve
- **Pure Transformation**: Returns new graph, no mutation
- **Fail-Fast Validation**: Audit throws if missing decisions
- **Deterministic Ordering**: Stable ordering for reproducibility
- **Scope Consistency**: Same scope construction everywhere

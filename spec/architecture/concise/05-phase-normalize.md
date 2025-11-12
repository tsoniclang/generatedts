# Phase NORMALIZE: Name Reservation and Overload Unification

## Overview

Central name assignment phase. Runs after Shape, before Plan.

**Responsibilities:**
1. **Name Reservation** - Assign final TypeScript names via Renamer
2. **Overload Unification** - Merge method overloads TS cannot distinguish

**Input**: `SymbolGraph` (EmitScope set, views planned)
**Output**: `SymbolGraph` (TsEmitName set on all symbols)
**Mutability**: Pure transformation

---

## NameReservation.cs

### Method: ReserveAllNames(BuildContext, SymbolGraph) → SymbolGraph

**Algorithm:**

1. **Reserve Type Names**:
   - Iterate namespaces/types (deterministic order)
   - Compute base name via `Shared.ComputeTypeRequestedBase()`
   - Call `Renamer.ReserveTypeName()` with namespace scope
   - Skip compiler-generated (names with `<` or `>`)

2. **Reserve Class Surface Members**:
   - Call `Reservation.ReserveMemberNamesOnly()` per type
   - Reserve methods, properties, fields, events, constructors
   - Scope: `ScopeFactory.ClassSurface(type, isStatic)`
   - Skip existing decisions (from earlier passes)
   - Skip `ViewOnly` (handled separately)
   - Skip `Omitted` (no names needed)

3. **Rebuild Class Surface Name Sets**:
   - After reservation, rebuild complete class-surface name sets
   - Separate sets: instance + static
   - Check ALL `ClassSurface` members (including pre-existing)
   - Union set (`classAllNames`) for collision detection
   - **Critical**: Include members renamed by earlier passes (HiddenMemberPlanner)

4. **Reserve View Members**:
   - Call `Reservation.ReserveViewMemberNamesOnly()` per type with views
   - Pass `classAllNames` for collision detection
   - Scope: `ScopeFactory.ViewSurface(type, interfaceStableId, isStatic)`
   - Apply `$view` suffix if view name collides with class

5. **Post-Reservation Audit**:
   - Call `Audit.AuditReservationCompleteness()`
   - Verify every emitted member has rename decision
   - Throw if missing (fail-fast)

6. **Apply Names to Graph**:
   - Call `Application.ApplyNamesToGraph()`
   - Set `TsEmitName` on all types/members
   - Return new graph (pure)

**Class Surface vs View Surface:**
- **Class Surface**: Members on class declaration
  - Scope: `ScopeFactory.ClassSurface(type, isStatic)`
  - Unique within instance/static scope
- **View Surface**: Members only on interface views
  - Scope: `ScopeFactory.ViewSurface(type, interfaceStableId, isStatic)`
  - Separate scope per interface
  - Can differ from class (using `$view` suffix)

**Static vs Instance Scopes:**
TypeScript separates instance/static namespaces:
```typescript
class Example {
    static foo: string;  // Static scope
    foo: number;         // Instance scope - NO COLLISION
}
```
Renamer uses separate scopes: `#instance` vs `#static`

---

## Naming/Reservation.cs

### Method: ReserveMemberNamesOnly(BuildContext, TypeSymbol, RenameScope)

**What it does**: Reserves names for class surface members (non-view).

**Algorithm:**
1. For each method/property/field/event/constructor:
   - Skip if `EmitScope != ClassSurface` (views handled separately)
   - Skip if `EmitScope == Omitted`
   - Skip if already has decision (from HiddenMemberPlanner, IndexerPlanner)
   - Compute requested base: `Shared.ComputeMemberRequestedBase()`
   - Reserve: `Renamer.ReserveMemberName()`

### Method: ReserveViewMemberNamesOnly(BuildContext, TypeSymbol, HashSet<string> classAllNames)

**What it does**: Reserves names for view-only members with collision detection.

**Algorithm:**
1. For each view in `type.ExplicitViews`:
   - Extract interface StableId
   - For each view member:
     - Compute requested base: `Shared.ComputeMemberRequestedBase()`
     - **Collision Detection**: Check if `classAllNames.Contains(requestedBase)`
     - If collision: append `$view` suffix
     - Reserve: `Renamer.ReserveMemberName()` with view scope

**Why Collision Detection?**
Prevents members on class surface from conflicting with view members:
```typescript
class Foo {
    toString(): string;          // Class surface
    As_IFormattable: {
        toString$view(fmt): string;  // View - collision avoided
    };
}
```

---

## Naming/Application.cs

### Method: ApplyNamesToGraph(BuildContext, SymbolGraph) → SymbolGraph

**What it does**: Retrieves final names from Renamer and applies to SymbolGraph.

**Algorithm:**
1. For each namespace:
   - For each type:
     - Get final type name: `Renamer.GetFinalTypeName(type, NamespaceArea.Internal)`
     - Set `type.TsEmitName`
     - For each member:
       - Determine scope (ClassSurface vs View)
       - Get final member name: `Renamer.GetFinalMemberName(member.StableId, scope)`
       - Set `member.TsEmitName`
2. Return new graph with `TsEmitName` populated

**Scope Determination:**
- `ClassSurface` members: Use `ScopeFactory.ClassSurface(type, member.IsStatic)`
- `ViewOnly` members: Use `ScopeFactory.ViewSurface(type, member.SourceInterface, member.IsStatic)`

---

## Naming/Audit.cs

### Method: AuditReservationCompleteness(BuildContext, SymbolGraph)

**What it does**: Validates every emitted member has a rename decision.

**Algorithm:**
1. For each type:
   - For each member (all scopes):
     - Skip if `EmitScope == Omitted` (intentionally not emitted)
     - Determine scope
     - Check: `Renamer.HasFinalMemberName(member.StableId, scope)`
     - If missing: log ERROR and throw
2. Throw if any missing (fail-fast)

**Purpose**: Catch bugs where Shape passes forget to reserve names.

---

## Naming/Shared.cs

### Method: ComputeTypeRequestedBase(TypeSymbol, Policy) → string

**What it does**: Computes requested TypeScript name for a type (before collision resolution).

**Algorithm:**
1. Start with `type.ClrSimpleName`
2. Apply generic arity suffix: `List\`1` → `List_1`
3. Apply policy transforms (PascalCase, camelCase, snake_case, etc.)
4. Sanitize TypeScript reserved words (add `_` suffix)
5. Return requested base name

**Example:** `Dictionary\`2` → `Dictionary_2`

### Method: ComputeMemberRequestedBase(MemberSymbol, Policy) → string

**What it does**: Computes requested TypeScript name for a member.

**Algorithm:**
1. Start with `member.ClrName`
2. Strip explicit interface prefix if present: `IDisposable.Dispose` → `Dispose`
3. Apply policy transforms (camelCase for members)
4. Sanitize TypeScript reserved words
5. Return requested base name

**Example:** `ToString` → `toString` (camelCase)

---

## OverloadUnifier.cs

### Method: UnifyOverloads(BuildContext, SymbolGraph) → SymbolGraph

**What it does**: Merges method overloads that TypeScript cannot distinguish.

**Algorithm:**
1. For each type:
   - Group methods by `(ClrName, EmitScope, IsStatic)`
   - For each overload group:
     - Build TS signature (erase ref/out/constraints)
     - Group by TS signature
     - If multiple CLR sigs map to same TS sig → UNIFY
     - Keep first, mark rest as `EmitScope.Omitted`
2. Return new graph with overloads unified

**Unification Logic:**
- Methods differing only by ref/out → keep one
- Methods with same TS signature but different constraints → keep one
- Preserved method gets merged parameter info (union of all variants)

**Example:**
```csharp
void Method(int x);
void Method(ref int x);   // Same TS signature → UNIFY
```

---

## Integration Flow

```
Shape Phase Output (SymbolGraph with EmitScope set)
  ↓
NameReservation.ReserveAllNames()
  ├─► Reserve type names (namespace scope)
  ├─► Reserve class surface members (class scope)
  ├─► Rebuild class surface name sets (for collision detection)
  ├─► Reserve view members (view scope, with $view suffix if collision)
  ├─► Audit completeness (fail if missing decisions)
  └─► Apply names to graph (set TsEmitName)
  ↓
SymbolGraph (TsEmitName set on all symbols)
  ↓
OverloadUnifier.UnifyOverloads()
  └─► Merge indistinguishable overloads (mark rest Omitted)
  ↓
SymbolGraph (ready for Plan phase)
```

---

## Key Algorithms

### Collision Detection with $view Suffix

**Problem**: View member names may collide with class surface names.

**Solution**:
1. Build `classAllNames` set (all ClassSurface member names)
2. For each view member:
   - Compute `requestedBase`
   - If `classAllNames.Contains(requestedBase)` → append `$view`
3. Reserve with modified name

**Example:**
```typescript
class Foo {
    toString(): string;          // Class surface
    As_IFormattable: {
        toString$view(fmt): string;  // View - collision avoided
    };
}
```

### Dual-Scope Algorithm

**Class Surface Scope**:
- Format: `type:System.Decimal#instance` or `type:System.Decimal#static`
- Unique within class instance/static scope

**View Surface Scope**:
- Format: `view:CoreLib:System.Decimal:CoreLib:IConvertible#instance`
- Separate scope per interface
- Allows different names for same member across views

**Benefits**:
- No artificial suffixes for most members
- Views can have different names than class
- Static/instance separation works correctly

---

## Summary

**Normalize phase responsibilities:**
1. Reserve all type names (namespace scope)
2. Reserve all class surface member names (class scope, instance/static separate)
3. Reserve all view member names (view scope, collision detection with `$view`)
4. Audit completeness (fail if any emitted member lacks decision)
5. Apply names to graph (set TsEmitName)
6. Unify indistinguishable overloads (erase ref/out, mark duplicates Omitted)

**Output**: SymbolGraph with TsEmitName set, ready for Plan phase

**Key design decisions:**
- Dual-scope algorithm (class vs view)
- Separate static/instance scopes
- `$view` suffix for collision avoidance
- Fail-fast completeness audit
- Pure functional transformation
- Overload unification based on TS assignability

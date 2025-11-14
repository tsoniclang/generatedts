# Renaming System

## Overview

The Renaming system is the **centralized naming authority** for the entire generation pipeline. All TypeScript identifiers flow through `SymbolRenamer`, which:

- **Materializes final TS identifiers** for types and members
- **Records every rename** with full provenance (`RenameDecision`)
- **Provides deterministic suffix allocation** for collision resolution
- **Separates static and instance member scopes** to prevent false collisions
- **Supports dual-scope reservations** (class surface + view surface for same member)

**Key Principle**: No component guesses names. All names are reserved during planning phases and looked up during emission.

---

## File: SymbolRenamer.cs

### Purpose

Central renaming service with dual-scope algorithm. Manages name reservations across multiple scope types (namespace, class surface, view surface) with separate static/instance tracking.

### Properties (Private Fields)

- `_tablesByScope` - `Dictionary<string, NameReservationTable>` - Scope key → reservation table
- `_decisions` - `Dictionary<(StableId Id, string ScopeKey), RenameDecision>` - Records all rename decisions keyed by StableId + scope
- `_explicitOverrides` - `Dictionary<StableId, string>` - CLI/user-specified name overrides
- `_typeStyleTransform` - `Func<string, string>` - Style transform for type names (e.g., PascalCase)
- `_memberStyleTransform` - `Func<string, string>` - Style transform for member names (e.g., camelCase)

**M5 CRITICAL FIX**: The `_decisions` dictionary was changed from keying by `StableId` alone to `(StableId, ScopeKey)` to support dual-scope reservations. This allows the same member to have different final names in class scope vs view scope.

---

### Key Methods

#### ReserveTypeName(StableId, string requested, RenameScope, string reason, string decisionSource)

Reserves a type name in a namespace scope. Applies the type style transform.

**Algorithm**:
1. Get or create reservation table for scope
2. Apply explicit overrides (if any)
3. Apply type style transform to requested name
4. Sanitize for TS reserved words (adds trailing underscore)
5. Try to reserve sanitized name
6. If collision, apply numeric suffix strategy (name2, name3, etc.)
7. Record decision in `_decisions` dictionary

**Collision Handling**:
- First call for "Foo" → "Foo"
- Second call for "Foo" → "Foo2"
- Third call for "Foo" → "Foo3"

#### ReserveMemberName(StableId, string requested, RenameScope, string reason, bool isStatic, string decisionSource)

Reserves a member name in a type scope. Static and instance members are tracked separately. Applies the member style transform.

**Dual-Scope Reservation**:
- Creates sub-scope: `{baseScope}#static` or `{baseScope}#instance`
- Class members: `type:System.String#instance`, `type:System.String#static`
- View members: `view:{TypeStableId}:{InterfaceStableId}#instance`

**Algorithm**:
1. Create effective scope with `#static` or `#instance` suffix
2. Get or create reservation table for effective scope
3. Apply explicit overrides (if any)
4. Apply member style transform to requested name
5. Sanitize for TS reserved words
6. Try to reserve sanitized name
7. If collision, check if explicit interface implementation:
   - Extract interface short name from qualified member name
   - Try: `{base}_{InterfaceName}` (e.g., `get_ICollection`)
   - If still collides, apply numeric suffix
8. If not explicit interface impl, apply standard numeric suffix
9. Record decision in `_decisions` with scope key

**Example**:
```csharp
// Class surface member
renamer.ReserveMemberName(
    memberStableId,
    "ToString",
    ScopeFactory.ClassBase(typeSymbol),
    "NameTransform(CamelCase)",
    isStatic: false,
    "MemberPlanner");
// Reserves in scope: "type:System.String#instance"

// View surface member (explicit interface impl)
renamer.ReserveMemberName(
    memberStableId,
    "System.Collections.ICollection.Count",
    ScopeFactory.ViewBase(typeSymbol, interfaceStableId),
    "ExplicitInterfaceImplementation",
    isStatic: false,
    "ViewPlanner");
// Reserves in scope: "view:{TypeStableId}:{InterfaceStableId}#instance"
```

#### GetFinalTypeName(TypeSymbol type, NamespaceArea area) -> string

Gets the final TS name for a type (SAFE API - use this). Automatically derives the correct namespace scope from the type.

**Returns**: Final TS identifier

**Throws**: `InvalidOperationException` if name was not reserved

**Example**:
```csharp
string tsName = renamer.GetFinalTypeName(typeSymbol);
// Returns: "MyClass" or "MyClass2" (if collision)
```

#### GetFinalMemberName(StableId, RenameScope) -> string

Gets the final TS name for a member. **M5 FIX**: Now scope-aware - different scopes (class vs view) return different names.

**CRITICAL**: Scope must be a SURFACE scope (with `#static` or `#instance` suffix). Use `ScopeFactory.ClassSurface/ViewSurface` for lookups.

**Returns**: Final TS identifier for this member in this scope

**Throws**: `InvalidOperationException` if name was not reserved in this scope

**Algorithm**:
1. Validate scope format (must be surface scope)
2. Look up decision by `(stableId, scope.ScopeKey)` tuple
3. Return `decision.Final`
4. If not found, list available scopes for diagnostics and throw

**Example**:
```csharp
// Class surface lookup
string className = renamer.GetFinalMemberName(
    memberStableId,
    ScopeFactory.ClassSurface(typeSymbol, isStatic: false));
// Returns: "toString" (if collision: "toString2")

// View surface lookup (different name possible)
string viewName = renamer.GetFinalMemberName(
    memberStableId,
    ScopeFactory.ViewSurface(typeSymbol, interfaceStableId, isStatic: false));
// Returns: "count_ICollection" (explicit interface impl name)
```

#### TryGetDecision(StableId, RenameScope, out RenameDecision?) -> bool

Tries to get the rename decision for a StableId in a specific scope. **M5 FIX**: Now requires scope parameter since members can be reserved in multiple scopes.

**CRITICAL**: Scope must be a SURFACE scope (with `#static` or `#instance` suffix).

**Returns**: True if decision found, false otherwise

#### PeekFinalMemberName(RenameScope, string requestedBase, bool isStatic) -> string

Peeks at what final name would be assigned in a scope without committing. Used for collision detection before reservation. Applies member style transform and sanitization, then finds next available suffix if needed.

**Algorithm**:
1. Create effective scope (`#static` or `#instance`)
2. Apply member style transform
3. Sanitize for reserved words
4. If scope doesn't exist yet, return sanitized name
5. If base name available, return it
6. Otherwise, find next available suffix (2, 3, 4...) without mutating table
7. Return projected name

**Example**:
```csharp
string projectedName = renamer.PeekFinalMemberName(
    ScopeFactory.ViewBase(typeSymbol, interfaceStableId),
    "Count",
    isStatic: false);
// Returns: "count" or "count2" (without actually reserving)
```

#### IsNameTaken(RenameScope, string name, bool isStatic) -> bool

Checks if a name is already reserved in a specific scope. Used for collision detection when reserving view members.

**Returns**: True if name is taken, false if available

#### ListReservedNames(RenameScope, bool isStatic) -> HashSet<string>

Lists all reserved names in a scope. Returns the actual final names from the reservation table (after suffix resolution).

**Example**:
```csharp
var reserved = renamer.ListReservedNames(
    ScopeFactory.ClassBase(typeSymbol),
    isStatic: false);
// Returns: { "toString", "equals", "getHashCode" }
```

---

### Private Methods

#### ResolveNameWithConflicts

Core name resolution algorithm with collision handling.

**Algorithm**:
1. Check for explicit override → try to reserve
2. Apply style transform (type or member specific)
3. Sanitize TS reserved words (add trailing `_` if needed)
4. Try to reserve sanitized name → if success, return it
5. **Collision detected** → check if explicit interface implementation:
   - If member name contains `.` (e.g., "System.Collections.ICollection.Count")
   - Extract interface short name (e.g., "ICollection")
   - Try: `{sanitized}_{InterfaceName}` (e.g., "count_ICollection")
   - If still collides, apply numeric suffix to interface-suffixed name
6. **Not explicit interface impl** → apply standard numeric suffix:
   - Allocate next suffix from table (2, 3, 4...)
   - Try to reserve `{base}{suffix}`
   - Keep trying until successful (safety limit: 1000 attempts)
7. Return final resolved name

**Examples**:
```csharp
// Standard collision
"Compare" → "compare" (first)
"Compare" → "compare2" (second)
"Compare" → "compare3" (third)

// Explicit interface implementation
"System.Collections.ICollection.Count" → "count_ICollection"
"System.Collections.ICollection.Count" (different type) → "count_ICollection2"

// Reserved word
"switch" → "switch_"
"switch" (collision) → "switch_2"
```

---

## File: RenameScope.cs

### Purpose

Represents a naming scope where identifiers must be unique. Scopes prevent unrelated symbols from colliding.

### Record: RenameScope (abstract)

Base type for all scope types.

**Properties**:
- `ScopeKey` - `string` (required) - Human-readable scope identifier for debugging and dictionary keys

### Record: NamespaceScope

Scope for top-level types in a namespace.

**Properties**:
- `ScopeKey` - Inherited from `RenameScope`
- `Namespace` - `string` (required) - Full namespace name
- `IsInternal` - `bool` (required) - True for internal scope, false for facade scope

**Purpose**: Internal and facade are treated as separate scopes to allow clean facade names without collisions from internal names.

**Example ScopeKey**:
- `"ns:System.Collections.Generic:internal"`
- `"ns:System.Collections.Generic:public"`
- `"ns:(global):internal"`

### Record: TypeScope

Scope for members within a type. Static and instance members use separate sub-scopes.

**Properties**:
- `ScopeKey` - Inherited from `RenameScope`
- `TypeFullName` - `string` (required) - Full CLR type name
- `IsStatic` - `bool` (required) - True for static member sub-scope, false for instance

**Purpose**: Separating static/instance prevents false collision detection. TS allows same name for static and instance members.

**Example ScopeKey**:
- `"type:System.String#instance"` - Instance members of System.String
- `"type:System.String#static"` - Static members of System.String
- `"view:{TypeStableId}:{InterfaceStableId}#instance"` - Explicit interface impl view

---

## File: ScopeFactory.cs

### Purpose

Centralized scope construction for `SymbolRenamer`. **NO MANUAL SCOPE STRINGS** - all scopes must be created through these helpers.

### CANONICAL SCOPE FORMATS (Authoritative)

**DO NOT DEVIATE FROM THESE FORMATS**:

| Scope Type | Format | Example |
|------------|--------|---------|
| Namespace (public) | `ns:{Namespace}:public` | `"ns:System.Collections:public"` |
| Namespace (internal) | `ns:{Namespace}:internal` | `"ns:System.Collections:internal"` |
| Class members (instance) | `type:{TypeFullName}#instance` | `"type:System.String#instance"` |
| Class members (static) | `type:{TypeFullName}#static` | `"type:System.String#static"` |
| View members (instance) | `view:{TypeStableId}:{InterfaceStableId}#instance` | `"view:mscorlib:System.String:mscorlib:System.IComparable#instance"` |
| View members (static) | `view:{TypeStableId}:{InterfaceStableId}#static` | `"view:mscorlib:System.String:mscorlib:System.IComparable#static"` |

### USAGE PATTERN

**Reservations**: Use BASE scopes (no `#instance`/`#static` suffix) - `ReserveMemberName` adds it

**Lookups**: Use SURFACE scopes (with `#instance`/`#static` suffix) - use `ClassSurface`/`ViewSurface`

**M5 CRITICAL**: View members MUST be looked up with `ViewSurface`, not `ClassSurface`.

---

### Factory Methods

#### Namespace(string? ns, NamespaceArea area) -> NamespaceScope

Creates namespace scope for type name resolution.

**Format**: `"ns:{Namespace}:public"` or `"ns:{Namespace}:internal"`

**Example**:
```csharp
var scope = ScopeFactory.Namespace("System.Collections.Generic", NamespaceArea.Internal);
// scope.ScopeKey = "ns:System.Collections.Generic:internal"

var globalScope = ScopeFactory.Namespace(null, NamespaceArea.Internal);
// globalScope.ScopeKey = "ns:(global):internal"
```

#### ClassBase(TypeSymbol) -> TypeScope

Creates BASE class scope for member reservations (no side suffix).

**Format**: `"type:{TypeFullName}"` (ReserveMemberName will add `#instance`/`#static`)

**Use for**: `ReserveMemberName` calls

**Example**:
```csharp
var scope = ScopeFactory.ClassBase(typeSymbol);
// scope.ScopeKey = "type:System.String"

renamer.ReserveMemberName(memberStableId, "ToString", scope, "...", isStatic: false, "...");
// Reserves in: "type:System.String#instance"
```

#### ClassSurface(TypeSymbol, bool isStatic) -> TypeScope

Creates FULL class scope based on member's `isStatic` flag.

**Format**: `"type:{TypeFullName}#instance"` or `"#static"`

**Use for**: `GetFinalMemberName`, `TryGetDecision` calls when `isStatic` is dynamic

**Preferred over manual ternary** - cleaner call-sites.

**Example**:
```csharp
var scope = ScopeFactory.ClassSurface(typeSymbol, member.IsStatic);
string finalName = renamer.GetFinalMemberName(memberStableId, scope);
```

#### ViewBase(TypeSymbol, string interfaceStableId) -> TypeScope

Creates BASE view scope for member reservations (no side suffix).

**Format**: `"view:{TypeStableId}:{InterfaceStableId}"` (ReserveMemberName will add `#instance`/`#static`)

**Use for**: `ReserveMemberName` calls for ViewOnly members

**Example**:
```csharp
var scope = ScopeFactory.ViewBase(typeSymbol, interfaceStableId);
// scope.ScopeKey = "view:mscorlib:System.String:mscorlib:System.IComparable"

renamer.ReserveMemberName(memberStableId, "CompareTo", scope, "...", isStatic: false, "...");
// Reserves in: "view:mscorlib:System.String:mscorlib:System.IComparable#instance"
```

#### ViewSurface(TypeSymbol, string interfaceStableId, bool isStatic) -> TypeScope

Creates FULL view scope for explicit interface view member lookups.

**Format**: `"view:{TypeStableId}:{InterfaceStableId}#instance"` or `"#static"`

**Use for**: `GetFinalMemberName`, `TryGetDecision` calls for ViewOnly members

**M5 FIX**: This is what emitters were missing - they were using `ClassInstance`/`ClassStatic` for view members, causing PG_NAME_004 collisions.

**Example**:
```csharp
var scope = ScopeFactory.ViewSurface(typeSymbol, interfaceStableId, isStatic: false);
// scope.ScopeKey = "view:mscorlib:System.String:mscorlib:System.IComparable#instance"

string finalName = renamer.GetFinalMemberName(memberStableId, scope);
```

---

## Key Algorithms

### Dual-Scope Algorithm

The Renamer uses a sophisticated dual-scope algorithm that allows the same member to have different names in different contexts:

**Class Surface Scope**:
- Members emitted on class declaration
- Scope key: `type:{TypeFullName}#instance` or `type:{TypeFullName}#static`
- Collision resolution: Numeric suffixes (`toString`, `toString2`, `toString3`)

**View Surface Scope**:
- Members emitted on interface views
- Scope key: `view:{TypeStableId}:{InterfaceStableId}#instance` or `#static`
- Separate scope per interface (allows different implementations)
- Collision resolution: Interface suffix first (`count_ICollection`), then numeric

**Why Dual-Scope?**

A class can implement the same interface member differently through explicit interface implementation:

```csharp
// C#
class MyClass : IComparable, IComparable<MyClass>
{
    public int CompareTo(object obj) { ... }           // Class surface
    int IComparable<MyClass>.CompareTo(MyClass other) { ... }  // ViewOnly
}

// TypeScript
class MyClass {
    // Class surface (ClassSurface scope)
    compareTo(obj: any): int;

    // View for IComparable<MyClass> (View scope)
    readonly asIComparable_1: {
        compareTo$view(other: MyClass): int;  // $view suffix
    };
}
```

### Collision Resolution Strategy

**1. First Try**: Base name after style transform + sanitization
```csharp
"ToString" → "toString"  // First reservation
```

**2. Standard Collision**: Numeric suffix
```csharp
"ToString" → "toString2"  // Second reservation
"ToString" → "toString3"  // Third reservation
```

**3. Explicit Interface Implementation**: Interface name suffix
```csharp
"System.Collections.ICollection.Count" → "count_ICollection"  // First
"System.Collections.ICollection.Count" → "count_ICollection2"  // Second
```

**4. Reserved Word**: Trailing underscore
```csharp
"switch" → "switch_"  // First
"switch" → "switch_2"  // Second (with underscore)
```

### Static vs Instance Separation

TypeScript allows same name for static and instance members:

```typescript
class Example {
    static foo: string;  // Static scope
    foo: number;         // Instance scope - NO COLLISION!
}
```

Therefore, Renamer uses separate sub-scopes:
- `type:Example#static` - for static members
- `type:Example#instance` - for instance members

### Reservation vs Lookup

**Reservation Phase** (during NameReservation):
- Use BASE scopes: `ClassBase(type)`, `ViewBase(type, ifaceId)`
- `ReserveMemberName` adds `#instance` or `#static` suffix internally

**Lookup Phase** (during Emit):
- Use SURFACE scopes: `ClassSurface(type, isStatic)`, `ViewSurface(type, ifaceId, isStatic)`
- `GetFinalMemberName` requires exact scope with suffix

---

## M5 Critical Fix Summary

**Problem**: Before M5, view members were reserved in view scopes but looked up in class scopes, causing PG_NAME_004 errors (view member names shadowing class surface).

**Solution**: Changed `_decisions` dictionary to key by `(StableId, ScopeKey)` tuple instead of just `StableId`, allowing:
1. Same member reserved in multiple scopes (class + view)
2. Different final names per scope
3. Correct lookup via scope-aware `GetFinalMemberName`

**Impact**: Eliminated 100+ PG_NAME_004 collisions, enabled proper dual-scope naming.

---

## Summary

The Renaming system provides:

1. **Centralized Naming**: All TS identifiers flow through SymbolRenamer
2. **Dual-Scope Support**: Same member can have different names in class vs view scopes
3. **Static/Instance Separation**: Prevents false collisions
4. **Deterministic Collision Resolution**: Numeric suffixes, interface suffixes
5. **Scope Safety**: Type-safe scope construction via ScopeFactory
6. **Full Provenance**: Every rename decision recorded with reason

**Key Design Principles**:
- No manual scope strings (use ScopeFactory)
- No name guessing (reserve first, look up later)
- Scope-aware lookups (class vs view)
- Style transforms applied consistently
- Reserved words sanitized automatically

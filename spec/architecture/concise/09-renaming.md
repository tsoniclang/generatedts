# Renaming: Central Naming System

## Overview

**SymbolRenamer** is the central naming authority for all TypeScript identifiers. Single source of truth for all rename decisions.

**Responsibilities:**
1. Reserve names in scopes (namespace, class, view)
2. Apply style transforms (PascalCase, camelCase)
3. Resolve conflicts via numeric suffixes
4. Sanitize reserved words (class → class_)
5. Track rename decisions with provenance

---

## SymbolRenamer.cs

### Core Data Structures

**NameReservationTable:**
```csharp
class NameReservationTable
{
    Dictionary<string, RenameDecision> _decisions;  // StableId+Scope → Decision
    Dictionary<string, HashSet<string>> _scopeNames; // Scope → Names used
}
```

**RenameDecision:**
```csharp
record RenameDecision
{
    StableId Id;              // What was renamed
    string Requested;         // What was asked for
    string Final;             // What was decided
    string From;              // Original CLR name
    string Reason;            // Why this decision
    string DecisionSource;    // Which component decided
    string Strategy;          // "None", "NumericSuffix", "Sanitize"
    string ScopeKey;          // Which scope
    bool? IsStatic;           // Static vs instance
}
```

### Key Methods

**ReserveTypeName(StableId, string requested, NamespaceScope, string reason)**
```csharp
public void ReserveTypeName(
    StableId id,
    string requested,
    NamespaceScope scope,
    string reason)
```

**Algorithm:**
1. Check if already reserved → return (idempotent)
2. Apply transforms (PascalCase, etc.)
3. Sanitize reserved words
4. Check collision in scope
5. If collision: append numeric suffix (`Type2`, `Type3`, ...)
6. Record decision
7. Add to scope name set

**ReserveMemberName(StableId, string requested, RenameScope, string reason, bool isStatic)**
```csharp
public void ReserveMemberName(
    StableId id,
    string requested,
    RenameScope scope,
    string reason,
    bool isStatic)
```

**Algorithm:**
1. Check if already reserved → return
2. Apply transforms (camelCase for members)
3. Sanitize reserved words
4. Determine full scope key (includes `#instance` or `#static`)
5. Check collision
6. If collision: numeric suffix
7. Record decision
8. Add to scope name set

**GetFinalTypeName(TypeSymbol, NamespaceArea) → string**

Returns final TypeScript name for type.

**GetFinalMemberName(StableId, RenameScope) → string**

Returns final TypeScript name for member.

**HasFinalTypeName(StableId, NamespaceScope) → bool**

Checks if type has decision.

**HasFinalMemberName(StableId, RenameScope) → bool**

Checks if member has decision.

---

## RenameScope.cs

### Scope Types

**NamespaceScope:**
```csharp
record NamespaceScope(string Namespace, NamespaceArea Area)
```
- Format: `"ns:System.Collections.Generic:internal"`

**TypeScope:**
```csharp
record TypeScope(StableId TypeId, SurfaceKind Surface, bool IsStatic)
```
- Format: `"type:System.Decimal#instance"` or `"type:System.Decimal#static"`

**ViewScope:**
```csharp
record ViewScope(StableId TypeId, StableId InterfaceId, bool IsStatic)
```
- Format: `"view:CoreLib:System.Decimal:CoreLib:IConvertible#instance"`

**Enum SurfaceKind:** `ClassSurface`, `ViewSurface`

---

## ScopeFactory.cs

### Factory Methods

**Namespace(string ns, NamespaceArea area) → NamespaceScope**

Creates namespace scope.

**ClassSurface(TypeSymbol, bool isStatic) → TypeScope**

Creates class surface scope (instance or static).

**ViewSurface(TypeSymbol, TypeReference iface, bool isStatic) → ViewScope**

Creates view surface scope for interface.

**FromMember(MemberSymbol, TypeSymbol) → RenameScope**

Determines scope from member properties:
- If `EmitScope == ClassSurface` → `ClassSurface(type, member.IsStatic)`
- If `EmitScope == ViewOnly` → `ViewSurface(type, member.SourceInterface, member.IsStatic)`

---

## StableId.cs

### TypeStableId

```csharp
record struct TypeStableId(string AssemblyName, string ClrFullName)
```

**Format:** `"AssemblyName:ClrFullName"`
- Example: `"System.Private.CoreLib:System.Collections.Generic.List\`1"`

**Methods:**
- `ToString()` → formatted string
- `Equals()` → by assembly + full name
- `GetHashCode()` → combine both

### MemberStableId

```csharp
record struct MemberStableId(
    string AssemblyName,
    string DeclaringTypeClrFullName,
    string MemberName,
    string CanonicalSignature,
    int MetadataToken)
```

**Format:** `"AssemblyName:DeclaringType::MemberName(Signature):ReturnType"`

**Equality:** By assembly, declaring type, member name, and signature.
**Metadata token excluded from equality** (semantic identity only).

---

## TypeScriptReservedWords.cs

### Reserved Word Sanitization

**SanitizeIdentifier(string name) → string**

**Algorithm:**
1. Check if name is TS reserved word
2. If reserved: append `_` suffix
3. Return sanitized name

**Reserved Words:**
```
break, case, catch, class, const, continue, debugger, default, delete,
do, else, enum, export, extends, false, finally, for, function, if,
import, in, instanceof, new, null, return, super, switch, this, throw,
true, try, typeof, var, void, while, with, as, implements, interface,
let, package, private, protected, public, static, yield, any, boolean,
constructor, declare, get, module, require, number, set, string, symbol,
type, from, of, namespace
```

**SanitizeParameterName(string name) → string**

Handles parameter names that may be reserved:
```csharp
// "class" → "class_" in parameter list
method(class_: string)
```

---

## Dual-Scope Algorithm

### Class Surface vs View Surface

**Problem:** Members on class and in views may have same name.

**Solution:** Separate scopes.

**Example:**
```typescript
class Decimal {
    toString(): string;  // Scope: "type:System.Decimal#instance"

    As_IConvertible: {
        toString(): string;  // Scope: "view:CoreLib:Decimal:CoreLib:IConvertible#instance"
    };

    As_IFormattable: {
        toString(format: string): string;  // Scope: "view:CoreLib:Decimal:CoreLib:IFormattable#instance"
    };
}
```

**Benefits:**
- No artificial suffixes needed
- Each context has independent naming
- Collision detection works per scope

### Static vs Instance

**Problem:** TypeScript separates static and instance namespaces.

**Solution:** Separate scopes with `#static` and `#instance` suffixes.

**Example:**
```typescript
class Array {
    length: int;              // Scope: "type:System.Array#instance"
    static Length(a: Array): int;  // Scope: "type:System.Array#static"
    // NO COLLISION!
}
```

---

## Collision Resolution

### Numeric Suffix Strategy

**Problem:** Requested name already used in scope.

**Solution:** Append numeric suffix.

**Algorithm:**
1. Check `scopeNames.Contains(requested)`
2. If collision:
   - Try `requested2`, `requested3`, ...
   - Find first unused number
3. Record strategy: `"NumericSuffix"`

**Example:**
```
ToString → toString (first)
ToString → toString2 (second)
ToString → toString3 (third)
```

### Reserved Word Strategy

**Problem:** Name is TypeScript reserved word.

**Solution:** Append `_` suffix.

**Algorithm:**
1. Check `ReservedWords.Contains(name)`
2. If reserved: append `_`
3. Record strategy: `"Sanitize"`

**Example:**
```
class → class_
interface → interface_
typeof → typeof_
```

---

## Decision Recording

**Why record decisions?**
- Debugging (understand why name was chosen)
- Binding metadata (CLR ↔ TypeScript mappings)
- Reproducibility (same input → same output)

**Emitted to:** `bindings.json`

**Example Decision:**
```json
{
  "id": "System.Private.CoreLib:System.Decimal::ToString():String",
  "requested": "toString",
  "final": "toString",
  "from": "ToString",
  "reason": "StandardMemberRename",
  "strategy": "CamelCase",
  "scope": "type:System.Decimal#instance",
  "isStatic": false
}
```

---

## Integration with Pipeline

### Phase 3 (Shape)
- **HiddenMemberPlanner**: Pre-reserves `method_new` for C# 'new' keyword
- **IndexerPlanner**: Pre-reserves `get_Item`, `set_Item` for indexers

### Phase 3.5 (Normalize)
- **NameReservation**: Reserves all remaining names
  - Type names (namespace scope)
  - Class surface members (class scope)
  - View members (view scope, with `$view` collision detection)

### Phase 4.7 (PhaseGate)
- **Finalization checks**: Verify every emitted member has decision

### Phase 5 (Emit)
- **BindingEmitter**: Retrieves all decisions for bindings.json

---

## Summary

**SymbolRenamer responsibilities:**
1. Central naming authority (single source of truth)
2. Dual-scope algorithm (class vs view, static vs instance)
3. Collision resolution (numeric suffixes)
4. Reserved word sanitization (`_` suffix)
5. Decision recording (full provenance)

**Key design decisions:**
- Immutable decisions (idempotent reservation)
- Scope-based uniqueness (not global)
- Semantic StableIds (not metadata tokens)
- Separate static/instance scopes (TS semantics)
- Full decision provenance (debugging + bindings)

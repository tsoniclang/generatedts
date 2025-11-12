# Phase EMIT: File Generation

## Overview

Generates all output files from validated EmissionPlan.

**Input**: `EmissionPlan` (SymbolGraph + ImportPlan + EmitOrder)
**Output**: 6 file types per namespace
**Mutability**: Side effects only (file I/O)

**Critical**: Only runs if `ctx.Diagnostics.HasErrors() == false`

---

## Output File Types

### 1. _support/types.d.ts
Global marker types and utility types.

**Content:**
- Branded primitives: `type int = number & { __brand: "int" };`
- Utility types: `type Nullable<T> = T | null`
- Global declarations

**Emitted by:** SupportTypesEmitter.cs

### 2. {namespace}/internal/index.d.ts
Internal declarations (full detail, not tree-shakeable).

**Content:**
- All types (classes, interfaces, enums, delegates)
- All members (methods, properties, fields, events)
- Generic parameters and constraints
- View properties (As_IInterface)

**Emitted by:** InternalIndexEmitter.cs

### 3. {namespace}/index.d.ts (facade)
Public facade (tree-shakeable, references internal/).

**Content:**
- Export statements: `export { ClassName, InterfaceName } from './internal'`
- Type aliases for brevity

**Emitted by:** FacadeEmitter.cs

### 4. {namespace}/metadata.json
CLR-specific metadata for Tsonic compiler.

**Content:**
- Virtual/override markers
- Static members
- Ref/out parameters
- Generic constraints
- Intentional omissions (indexers, generic static members)

**Emitted by:** MetadataEmitter.cs

### 5. {namespace}/bindings.json
CLR ↔ TypeScript name mappings.

**Content:**
- Type name mappings: `{ "List_1": "System.Collections.Generic.List`1" }`
- Member name mappings with scopes
- Rename decisions and strategies

**Emitted by:** BindingEmitter.cs

### 6. {namespace}/index.js
ES module stub (for import resolution).

**Content:**
```javascript
export * from './index.d.ts';
```

**Emitted by:** ModuleStubEmitter.cs

---

## Printer Architecture

### TypeRefPrinter.cs
Converts `TypeReference` → TypeScript type string.

**Methods:**
- `Print(TypeReference)` - Main entry
- `PrintNamed(NamedTypeReference)` - Classes, interfaces, enums
- `PrintGenericParameter(GenericParameterReference)` - Type parameters
- `PrintArray(ArrayTypeReference)` - Arrays
- `PrintPointer(PointerTypeReference)` - Pointers (unsafe)
- `PrintByRef(ByRefTypeReference)` - Ref parameters

**Special Cases:**
- `unknown` - For synthesized TS built-ins
- Branded primitives: `int`, `decimal`, `byte`, etc.
- Generic instantiations: `List<T>` vs `List_1<T>`

### ClassPrinter.cs
Emits class declarations.

**Key Methods:**
- `EmitClass()` - Main orchestrator
- `EmitTypeParameters()` - Generic parameters with constraints
- `EmitBaseClass()` - Extends clause
- `EmitInterfaces()` - Implements clause
- `EmitInstanceMembers()` - Instance methods/properties/fields
- `EmitStaticMembers()` - Static section
- `EmitViews()` - As_IInterface view properties

**Generic Lifting:**
For static members, lifts class generics to method level:
```typescript
// Before (invalid TS):
class List_1<T> {
    static DefaultValue: T;  // ❌ TS2302
}

// After (valid):
class List_1<T> {
    static DefaultValue<T>(): T;  // ✅ Method-level generic
}
```

**FIX D - Generic Parameter Substitution:**
When emitting base class members or interface members, substitutes generic parameters:
```typescript
// Before (orphaned generic):
class List_1<T> implements ICollection {
    Add(item: T1);  // ❌ T1 not declared
}

// After (substituted):
class List_1<T> implements ICollection {
    Add(item: T);  // ✅ Uses class's T
}
```

### InterfacePrinter.cs
Emits interface declarations.

**Methods:**
- `EmitInterface()` - Main orchestrator
- `EmitMembers()` - Methods and properties

**Note:** Interfaces are flattened (no extends), all members copied.

### EnumPrinter.cs
Emits enum declarations.

**Format:**
```typescript
export enum MyEnum {
    Value1 = 0,
    Value2 = 1,
    Value3 = 2
}
```

### DelegatePrinter.cs
Emits delegate as type alias to function signature.

**Format:**
```typescript
export type MyDelegate = (param1: string, param2: int) => void;
```

### MethodPrinter.cs
Emits method signatures.

**Overload Handling:**
```typescript
// Multiple signatures for overloads
method(x: int): void;
method(x: string): void;
method(x: int | string): void;  // Implementation signature
```

### PropertyPrinter.cs
Emits property declarations.

**Format:**
```typescript
readonly PropName: TypeName;  // Readonly
PropName: TypeName;            // Readwrite
```

### FieldPrinter.cs
Emits field declarations (static and instance).

**Const Fields:**
```typescript
static readonly MaxValue: int;  // Const → readonly
```

### ViewPrinter.cs
Emits As_IInterface view properties.

**Format:**
```typescript
As_IConvertible: {
    toBoolean(provider: IFormatProvider | null): boolean;
    toInt32(provider: IFormatProvider | null): int;
};
```

---

## Key Algorithms

### Generic Lifting (TS2302 Fix)

**Problem:** TypeScript forbids static members using class-level generics.

**Solution:**
1. Detect static members with class generic references
2. Lift class generics to method level
3. Convert property → method returning value

**Example:**
```typescript
// Before:
class List_1<T> {
    static Empty: List_1<T>;  // ❌ TS2302
}

// After:
class List_1<T> {
    static Empty<T>(): List_1<T>;  // ✅ Method with generic
}
```

### Generic Parameter Substitution (FIX D)

**Problem:** Inherited interface/base class members use parent's generic parameters, not child's.

**Solution:**
1. When emitting inherited member:
   - Build substitution map: parent generic params → child type args
   - Recursively substitute in all TypeReferences
   - **Filter out method-level generics** (don't substitute method's own `<T>`)

**Example:**
```csharp
// C#:
interface ICollection<T> { void Add(T item); }
class List<T> : ICollection<T> { }

// TypeScript (WRONG without substitution):
class List_1<T> implements ICollection {
    Add(item: T1);  // ❌ T1 orphaned
}

// TypeScript (CORRECT with substitution):
class List_1<T> implements ICollection {
    Add(item: T);  // ✅ Substituted to class's T
}
```

### InterfaceStableId Stamping

**Optimization:** Precompute `"{assemblyName}:{fullName}"` at Load time.

**Benefits:**
- Eliminates repeated string concatenation
- Faster lookups in later phases
- Consistent format across codebase

---

## Integration Flow

```
EmissionPlan (validated)
  ↓
For each namespace (in emission order):
  ├─► InternalIndexEmitter.Emit()
  │     ├─► ClassPrinter.EmitClass() for each class
  │     ├─► InterfacePrinter.EmitInterface() for each interface
  │     ├─► EnumPrinter.EmitEnum() for each enum
  │     └─► DelegatePrinter.EmitDelegate() for each delegate
  │     → Write {ns}/internal/index.d.ts
  │
  ├─► FacadeEmitter.Emit()
  │     └─► Generate export statements
  │     → Write {ns}/index.d.ts
  │
  ├─► MetadataEmitter.Emit()
  │     └─► Collect CLR metadata
  │     → Write {ns}/metadata.json
  │
  ├─► BindingEmitter.Emit()
  │     └─► Extract rename decisions from Renamer
  │     → Write {ns}/bindings.json
  │
  └─► ModuleStubEmitter.Emit()
        └─► Generate ES module stub
        → Write {ns}/index.js

SupportTypesEmitter.Emit()
  → Write _support/types.d.ts
```

---

## Output Examples

### Class Declaration
```typescript
export class Decimal implements IComparable, IConvertible {
    constructor(value: int);

    // Instance members
    readonly Scale: int;
    ToString(): string;
    ToString(format: string): string;

    // Static members
    static Parse(s: string): Decimal;
    static readonly Zero: Decimal;

    // View properties
    As_IConvertible: {
        toBoolean(provider: IFormatProvider | null): boolean;
        toInt32(provider: IFormatProvider | null): int;
    };
}
```

### Interface Declaration
```typescript
export interface IEnumerable_1<T> {
    GetEnumerator(): IEnumerator_1<T>;
}
```

### Enum Declaration
```typescript
export enum FileMode {
    CreateNew = 1,
    Create = 2,
    Open = 3,
    OpenOrCreate = 4,
    Truncate = 5,
    Append = 6
}
```

### Delegate Declaration
```typescript
export type Action_1<T> = (obj: T) => void;
```

---

## Summary

**Emit phase responsibilities:**
1. Generate internal declarations (full detail)
2. Generate public facades (tree-shakeable)
3. Generate metadata sidecars (CLR info for Tsonic)
4. Generate binding mappings (name resolution)
5. Generate module stubs (import resolution)
6. Generate support types (global markers)

**Key design decisions:**
- Generic lifting for static members (TS2302 fix)
- Generic parameter substitution for inherited members (FIX D)
- InterfaceStableId optimization (precomputed at Load)
- Separate internal/ and facade/ for tree-shaking
- Printer architecture (modular, composable)
- TypeRefPrinter handles all type printing consistently

# Phase 3: Model - Data Structures

## Overview

**Model** defines immutable data structures representing the complete symbol graph from .NET assemblies. Created during Load phase, transformed during Shape phase.

**Provides**:
1. Type-safe representation of CLR types/members
2. Stable identities through transformations
3. Type references (generics, arrays, pointers, etc.)
4. Provenance tracking (member origins)
5. Emit scope control (placement decisions)

All structures are **immutable records** with structural equality, supporting pure functional transformations via `with` expressions.

---

## File: SymbolGraph.cs

### Purpose
Root container for entire symbol graph: all namespaces, types, members, plus lookup indices.

### Record: SymbolGraph

```csharp
public sealed record SymbolGraph
```

**Properties**:

- **`Namespaces: ImmutableArray<NamespaceSymbol>`** - All namespaces with types
- **`SourceAssemblies: ImmutableHashSet<string>`** - Contributing assembly paths
- **`NamespaceIndex: ImmutableDictionary<string, NamespaceSymbol>`** - O(1) namespace lookup
- **`TypeIndex: ImmutableDictionary<string, TypeSymbol>`** - O(1) type lookup by CLR full name

### Key Methods

**`WithIndices(): SymbolGraph`**
- Pure function returning new graph with populated indices
- **MUST be called after creating new graph** for efficient lookups

**`WithUpdatedType(string keyOrStableId, Func<TypeSymbol, TypeSymbol> transform): SymbolGraph`**
- Pure function updating single type
- **Key** can be CLR full name or StableId
- **Automatically rebuilds indices**
- Used extensively in Shape phase
- Example:
  ```csharp
  var updated = graph.WithUpdatedType("System.String", type =>
      type.WithAddedMethods(new[] { syntheticMethod }));
  ```

**`GetStatistics(): SymbolGraphStatistics`**
- Counts namespaces, types, members recursively

### Record: SymbolGraphStatistics

```csharp
public sealed record SymbolGraphStatistics(
    int NamespaceCount,
    int TypeCount,
    int MethodCount,
    int PropertyCount,
    int FieldCount,
    int EventCount,
    int TotalMembers)
```

---

## File: AssemblyKey.cs

### Purpose
Normalized assembly identity for disambiguation.

### Record Struct: AssemblyKey

```csharp
public readonly record struct AssemblyKey(
    string Name,           // Simple name (e.g., "System.Private.CoreLib")
    string PublicKeyToken, // Hex string or "null" if unsigned
    string Culture,        // Culture or "neutral"
    string Version)        // "Major.Minor.Build.Revision"
```

**Methods**:

- **`static From(AssemblyName asm): AssemblyKey`** - Create from System.Reflection.AssemblyName
- **`ToString(): string`** - GAC format: `"Name, PublicKeyToken=..., Culture=..., Version=..."`

---

## File: Symbols/NamespaceSymbol.cs

### Purpose
Represents namespace containing types. Multiple assemblies can contribute to same namespace.

### Record: NamespaceSymbol

```csharp
public sealed record NamespaceSymbol
```

**Properties**:

- **`Name: string`** - Namespace name (empty for root/global)
- **`Types: ImmutableArray<TypeSymbol>`** - All types in this namespace
- **`StableId: StableId`** - Stable identifier
- **`ContributingAssemblies: ImmutableHashSet<string>`** - Contributing assemblies

**Computed**:

- **`IsRoot: bool`** - True if root/global namespace
- **`SafeNameOrNull: string?`** - Name or null if root

---

## File: Symbols/TypeSymbol.cs

### Purpose
Core symbol: represents class, struct, interface, enum, delegate with metadata, members, relationships.

### Record: TypeSymbol

```csharp
public sealed record TypeSymbol
```

**Identity Properties**:

- **`StableId: TypeStableId`** - Stable identifier BEFORE name transformations
  - Format: `"AssemblyName:ClrFullName"`
  - Example: `"System.Private.CoreLib:System.Collections.Generic.List`1"`
  - Key for rename decisions and CLR bindings

- **`ClrFullName: string`** - Full CLR name with namespace
  - Example: `"System.Collections.Generic.List`1"`

- **`ClrName: string`** - Simple name without namespace
  - Example: `"List`1"`

- **`TsEmitName: string`** - TypeScript emit name (set by NameApplication)
  - Initially empty, populated during Shape phase
  - Example: `"List_1"` (underscore for arity), `"Console$Error"` (nested)

**Classification**:

- **`Namespace: string`** - Containing namespace
- **`Kind: TypeKind`** - Class, Struct, Interface, Enum, Delegate, StaticNamespace
- **`Accessibility: Accessibility`** - Public, Protected, Internal, etc.

**Generics**:

- **`Arity: int`** - Generic arity (0 for non-generic)
- **`GenericParameters: ImmutableArray<GenericParameterSymbol>`** - Generic parameters

**Type Hierarchy**:

- **`BaseType: TypeReference?`** - Base type (null for interfaces, Object, ValueType)
- **`Interfaces: ImmutableArray<TypeReference>`** - Implemented interfaces

**Members**:

- **`Members: TypeMembers`** - All members (methods, properties, fields, events, constructors)
- **`NestedTypes: ImmutableArray<TypeSymbol>`** - Nested types

**Characteristics**:

- **`IsValueType: bool`** - True for structs/enums
- **`IsAbstract: bool`** - True if abstract
- **`IsSealed: bool`** - True if sealed
- **`IsStatic: bool`** - True for C# static classes
- **`DeclaringType: TypeSymbol?`** - Declaring type (nested types)

**Shape Phase Properties**:

- **`ExplicitViews: ImmutableArray<Shape.ViewPlanner.ExplicitView>`** - Explicit interface views planned by ViewPlanner

### Wither Methods

**Member manipulation**:
- `WithMembers(TypeMembers members)`
- `WithAddedMethods(IEnumerable<MethodSymbol> methods)`
- `WithRemovedMethods(Func<MethodSymbol, bool> predicate)`
- `WithAddedProperties(IEnumerable<PropertySymbol> properties)`
- `WithRemovedProperties(Func<PropertySymbol, bool> predicate)`
- `WithAddedFields(IEnumerable<FieldSymbol> fields)`

**Shape phase updates**:
- `WithTsEmitName(string tsEmitName)` - Set TypeScript name
- `WithExplicitViews(ImmutableArray<ExplicitView> views)` - Set interface views

### Enum: TypeKind

```csharp
public enum TypeKind
{
    Class,           // Reference type
    Struct,          // Value type
    Interface,       // Interface
    Enum,            // Enumeration
    Delegate,        // Delegate type
    StaticNamespace  // C# static class (emits as namespace in TS)
}
```

### Record: GenericParameterSymbol

```csharp
public sealed record GenericParameterSymbol
```

**Properties**:

- **`Id: GenericParameterId`** - Unique identifier (declaring type + position)
- **`Name: string`** - Parameter name (e.g., "T", "TKey")
- **`Position: int`** - Zero-based position
- **`Constraints: ImmutableArray<TypeReference>`** - Type constraints (resolved by ConstraintCloser)
- **`RawConstraintTypes: System.Type[]?`** - Raw CLR constraints from reflection (null after resolution)
- **`Variance: Variance`** - None, Covariant (out T), Contravariant (in T)
- **`SpecialConstraints: GenericParameterConstraints`** - Flags: struct, class, new(), notnull

### Enum: Variance

```csharp
public enum Variance { None, Covariant, Contravariant }
```

### Enum: GenericParameterConstraints

```csharp
[Flags]
public enum GenericParameterConstraints
{
    None = 0,
    ReferenceType = 1,      // class
    ValueType = 2,          // struct
    DefaultConstructor = 4, // new()
    NotNullable = 8         // notnull
}
```

### Record: TypeMembers

```csharp
public sealed record TypeMembers
```

Container for all type members with clean separation by category.

**Properties**:

- **`Methods: ImmutableArray<MethodSymbol>`**
- **`Properties: ImmutableArray<PropertySymbol>`**
- **`Fields: ImmutableArray<FieldSymbol>`**
- **`Events: ImmutableArray<EventSymbol>`**
- **`Constructors: ImmutableArray<ConstructorSymbol>`**

**Static**: `Empty: TypeMembers` - Pre-constructed empty instance

### Enum: Accessibility

```csharp
public enum Accessibility
{
    Public, Protected, Internal, ProtectedInternal, Private, PrivateProtected
}
```

---

## File: Symbols/MemberSymbols/MethodSymbol.cs

### Purpose
Method member with signature, generic parameters, metadata.

### Record: MethodSymbol

```csharp
public sealed record MethodSymbol
```

**Identity**:

- **`StableId: MemberStableId`** - Stable identifier with canonical signature
  - Example: `"System.Private.CoreLib:System.String::Substring(System.Int32,System.Int32)->System.String"`
- **`ClrName: string`** - CLR method name
- **`TsEmitName: string`** - TypeScript emit name (set by NameApplication)

**Signature**:

- **`ReturnType: TypeReference`** - Return type (System.Void for void)
- **`Parameters: ImmutableArray<ParameterSymbol>`** - Parameters
- **`GenericParameters: ImmutableArray<GenericParameterSymbol>`** - Generic parameters (for generic methods)
- **`Arity: int`** - Generic parameter count

**Modifiers**:

- **`IsStatic: bool`**, **`IsAbstract: bool`**, **`IsVirtual: bool`**, **`IsOverride: bool`**, **`IsSealed: bool`**, **`IsNew: bool`**
- **`Visibility: Visibility`** - Public, Protected, etc.

**Provenance & Scope**:

- **`Provenance: MemberProvenance`** - Origin: Original, FromInterface, Synthesized, etc.
- **`EmitScope: EmitScope`** - Placement: ClassSurface, ViewOnly, Omitted, StaticSurface
  - **MUST be set during Shape phase** (PhaseGate error PG_FIN_001 if Unspecified)

**Interface Tracking**:

- **`SourceInterface: TypeReference?`** - Interface that contributed this member (null for original)

### Record: ParameterSymbol

```csharp
public sealed record ParameterSymbol
```

**Properties**:

- **`Name: string`** - Parameter name
- **`Type: TypeReference`** - Parameter type
- **`IsRef: bool`**, **`IsOut: bool`**, **`IsParams: bool`**
- **`HasDefaultValue: bool`**, **`DefaultValue: object?`**

### Enum: Visibility

```csharp
public enum Visibility { Public, Protected, Internal, ProtectedInternal, PrivateProtected, Private }
```

### Enum: MemberProvenance

Tracks member origin for emission decisions.

```csharp
public enum MemberProvenance
{
    Original,             // Declared in this type
    FromInterface,        // Copied from interface
    Synthesized,          // Created by shaper
    HiddenNew,            // Resolves C# 'new' hiding
    BaseOverload,         // Base class overload
    DiamondResolved,      // Diamond inheritance resolution
    IndexerNormalized,    // Normalized from indexer
    ExplicitView,         // Explicit interface view
    OverloadReturnConflict // ViewOnly due to return conflict
}
```

### Enum: EmitScope

Controls member placement in TypeScript output.

```csharp
public enum EmitScope
{
    Unspecified = 0,  // MUST be set during Shape (PhaseGate error if not)
    ClassSurface,     // Main class/interface surface
    StaticSurface,    // Static surface (static classes)
    ViewOnly,         // Explicit interface views only (As_IInterface)
    Omitted           // Omitted (unified or intentionally skipped)
}
```

**Decision Flow**:
```
Load → (all Unspecified)
  ↓
Shape → (set to ClassSurface/ViewOnly/Omitted/StaticSurface)
  ↓
Emit → (PhaseGate validates: no Unspecified)
```

---

## File: Symbols/MemberSymbols/PropertySymbol.cs

### Record: PropertySymbol

```csharp
public sealed record PropertySymbol
```

**Identity**: StableId, ClrName, TsEmitName

**Type**:

- **`PropertyType: TypeReference`** - Property type
- **`IndexParameters: ImmutableArray<ParameterSymbol>`** - Index parameters (for indexers)
- **`IsIndexer: bool`** - True if has index parameters

**Accessors**:

- **`HasGetter: bool`**, **`HasSetter: bool`**

**Modifiers**: IsStatic, IsVirtual, IsOverride, IsAbstract, Visibility

**Provenance & Scope**: Provenance, EmitScope (MUST be set during Shape), SourceInterface

---

## File: Symbols/MemberSymbols/FieldSymbol.cs

### Record: FieldSymbol

```csharp
public sealed record FieldSymbol
```

**Identity**: StableId, ClrName, TsEmitName

**Type**: `FieldType: TypeReference`

**Modifiers**: IsStatic, IsReadOnly, IsConst, ConstValue

**Provenance & Scope**: Visibility, Provenance, EmitScope (MUST be set)

---

## File: Symbols/MemberSymbols/EventSymbol.cs

### Record: EventSymbol

```csharp
public sealed record EventSymbol
```

**Identity**: StableId, ClrName, TsEmitName

**Type**: `EventHandlerType: TypeReference` - Delegate type

**Modifiers**: IsStatic, IsVirtual, IsOverride, Visibility

**Provenance & Scope**: Provenance, EmitScope (MUST be set), SourceInterface

---

## File: Symbols/MemberSymbols/ConstructorSymbol.cs

### Record: ConstructorSymbol

```csharp
public sealed record ConstructorSymbol
```

**Properties**:

- **`StableId: MemberStableId`**
- **`Parameters: ImmutableArray<ParameterSymbol>`**
- **`IsStatic: bool`** - True for type initializers
- **`Visibility: Visibility`**

**Note**: Constructors do NOT have Provenance/EmitScope (always emitted as-is).

---

## File: Types/TypeReference.cs

### Purpose
Immutable, structurally equal representation of type references. Recursive structures representing all CLR type forms.

### Abstract Record: TypeReference

```csharp
public abstract record TypeReference
{
    public abstract TypeReferenceKind Kind { get; }
}
```

### Enum: TypeReferenceKind

```csharp
public enum TypeReferenceKind
{
    Named,            // Class, struct, interface, enum, delegate
    GenericParameter, // T, TKey, TValue
    Array,            // T[], T[,]
    Pointer,          // T*, T**
    ByRef,            // ref T, out T
    Nested,           // Outer.Inner
    Placeholder       // Breaks recursion cycles
}
```

---

### Record: NamedTypeReference

Reference to named type.

```csharp
public sealed record NamedTypeReference : TypeReference
```

**Properties**:

- **`Kind`** → Named
- **`AssemblyName: string`** - Declaring assembly
- **`FullName: string`** - Full CLR name (e.g., `"System.Collections.Generic.List`1"`)
- **`Namespace: string`** - Namespace
- **`Name: string`** - Simple name
- **`Arity: int`** - Generic arity
- **`TypeArguments: IReadOnlyList<TypeReference>`** - Type arguments (closed generics)
- **`IsValueType: bool`** - True for structs/enums
- **`InterfaceStableId: string?`** - Pre-computed StableId for interfaces (fast lookups)

---

### Record: GenericParameterReference

Reference to generic type parameter.

```csharp
public sealed record GenericParameterReference : TypeReference
```

**Properties**:

- **`Kind`** → GenericParameter
- **`Id: GenericParameterId`** - Identifier (declaring type + position)
- **`Name: string`** - Parameter name (e.g., "T")
- **`Position: int`** - Position in parameter list
- **`Constraints: IReadOnlyList<TypeReference>`** - Type constraints

---

### Record: ArrayTypeReference

```csharp
public sealed record ArrayTypeReference : TypeReference
```

**Properties**:

- **`Kind`** → Array
- **`ElementType: TypeReference`** - Element type (recursive)
- **`Rank: int`** - Array rank (1 for T[], 2 for T[,])

---

### Record: PointerTypeReference

```csharp
public sealed record PointerTypeReference : TypeReference
```

**Properties**:

- **`Kind`** → Pointer
- **`PointeeType: TypeReference`** - Pointed type (recursive)
- **`Depth: int`** - Pointer depth (1 for T*, 2 for T**)

---

### Record: ByRefTypeReference

```csharp
public sealed record ByRefTypeReference : TypeReference
```

**Properties**:

- **`Kind`** → ByRef
- **`ReferencedType: TypeReference`** - Referenced type (recursive)

---

### Record: NestedTypeReference

```csharp
public sealed record NestedTypeReference : TypeReference
```

**Properties**:

- **`Kind`** → Nested
- **`DeclaringType: TypeReference`** - Outer type (recursive)
- **`NestedName: string`** - Nested type name
- **`FullReference: NamedTypeReference`** - Full reference including all nesting

---

### Record: PlaceholderTypeReference

Internal placeholder breaking recursion cycles.

```csharp
public sealed record PlaceholderTypeReference : TypeReference
```

**Properties**:

- **`Kind`** → Placeholder
- **`DebugName: string`** - Debug name for infinite recursion type

**Note**: Should NEVER appear in final output (printers emit `any` with warning).

---

## File: Types/GenericParameterId.cs

### Purpose
Uniquely identifies generic parameter (declaring type + position). Used for substitution in closed generic interfaces.

### Record: GenericParameterId

```csharp
public sealed record GenericParameterId
```

**Properties**:

- **`DeclaringTypeName: string`** - Full name of declaring type
  - Example: `"System.Collections.Generic.List`1"`, `"System.Linq.Enumerable.Select`2"`
- **`Position: int`** - Zero-based position
- **`IsMethodParameter: bool`** - True for method-level generics

**Methods**:

**`ToString(): string`**
- Format: `"DeclaringTypeName#Position"` (with "M" suffix if method parameter)
- Example: `"System.Collections.Generic.List`1#0"`, `"System.Linq.Enumerable.Select`2#1M"`

---

## File: Renaming/StableId.cs

### Purpose
Immutable identity for types/members BEFORE name transformations. Key for rename decisions and CLR bindings.

### Abstract Record: StableId

```csharp
public abstract record StableId
{
    public required string AssemblyName { get; init; }
}
```

---

### Record: TypeStableId

Stable identity for type.

```csharp
public sealed record TypeStableId : StableId
```

**Properties**:

- **`AssemblyName: string`** (inherited) - Assembly origin
- **`ClrFullName: string`** - Full CLR type name
  - Example: `"System.Collections.Generic.List`1"`

**Methods**:

**`ToString(): string`**
- Format: `"AssemblyName:ClrFullName"`
- Example: `"System.Private.CoreLib:System.Collections.Generic.List`1"`

---

### Record: MemberStableId

Stable identity for member. **Equality is semantic (excludes MetadataToken).**

```csharp
public sealed record MemberStableId : StableId
```

**Properties**:

- **`AssemblyName: string`** (inherited) - Assembly origin
- **`DeclaringClrFullName: string`** - Declaring type full name
- **`MemberName: string`** - Member name in CLR metadata
- **`CanonicalSignature: string`** - Unique signature among overloads
  - Methods: `"(ParamType1,ParamType2,...)->ReturnType"`
  - Properties: `"(IndexParam1,...)->PropertyType"`
  - Fields/Events: `"->FieldType"` or empty
- **`MetadataToken: int?`** - Optional token for debugging
  - **NOT included in equality/hash** (semantic identity only)

**Methods**:

**`ToString(): string`**
- Format: `"AssemblyName:DeclaringClrFullName::MemberName[CanonicalSignature]"`
- Example: `"System.Private.CoreLib:System.String::Substring(System.Int32,System.Int32)->System.String"`

**`Equals(MemberStableId? other): bool`** (overridden)
- Compares AssemblyName, DeclaringClrFullName, MemberName, CanonicalSignature
- **Excludes MetadataToken** (semantic equality)

**`GetHashCode(): int`** (overridden)
- Hash combines AssemblyName, DeclaringClrFullName, MemberName, CanonicalSignature
- **Excludes MetadataToken**

---

## Key Concepts

### StableId Format

**StableIds** provide stable, transformation-independent identity:

1. **TypeStableId**: `"AssemblyName:ClrFullName"`
   - Example: `"System.Private.CoreLib:System.String"`

2. **MemberStableId**: `"AssemblyName:DeclaringType::MemberName[Signature]"`
   - Example: `"System.Private.CoreLib:System.String::Substring(System.Int32,System.Int32)->System.String"`
   - **Semantic equality** (excludes MetadataToken)

**Properties**:
- Immutable (set once during Load)
- Stable across transformations
- Keys in rename dictionaries
- Used for CLR bindings

---

### Canonical Signatures

Uniquely identify members among overloads:

**Methods**: `"(ParamType1,ParamType2,...)->ReturnType"`
- Example: `"(System.Int32,System.Int32)->System.String"` for `string Substring(int, int)`

**Properties**: `"(IndexParam1,...)->PropertyType"`
- Indexers: `"(System.Int32)->System.Char"` for `char this[int index]`
- Normal: `"()->PropertyType"`

**Fields/Events**: `"->FieldType"` or empty

**Purpose**: Disambiguate overloads, enable stable identity, support signature lookups

---

### EmitScope Decision

**EmitScope** controls member placement:

1. **`Unspecified`** (0) - Default, MUST be set during Shape
   - PhaseGate error (PG_FIN_001) if reaches emission

2. **`ClassSurface`** - Main class/interface surface (normal members)

3. **`StaticSurface`** - Static surface (static classes, namespace scope)

4. **`ViewOnly`** - Explicit interface views only (As_IInterface properties)
   - Members that conflict on class surface but needed for interface contracts

5. **`Omitted`** - Omitted from emission (unified or intentionally skipped)

---

### Member Provenance

Tracks member origins:

- **Original** - Declared in type
- **FromInterface** - Copied from interface
- **Synthesized** - Created by shaper
- **HiddenNew** - Resolves C# 'new' hiding
- **BaseOverload** - Base class overload
- **DiamondResolved** - Diamond inheritance resolution
- **IndexerNormalized** - Normalized from indexer
- **ExplicitView** - Explicit interface view
- **OverloadReturnConflict** - ViewOnly due to return conflict

**Purpose**: Understand transformation history, make emit decisions, debug provenance

---

## Pipeline Usage

### Load Phase (Creates Model)

1. Reflection converts `System.Type` → `TypeSymbol`
2. TypeReference converts `System.Type` → immutable references
3. StableId created for every type/member
4. Members populated with `Provenance = Original`
5. EmitScope defaults to `Unspecified`
6. SymbolGraph assembled and indexed

### Shape Phase (Transforms Model)

1. Interface closure adds `Provenance = FromInterface` members
2. View planning sets `ExplicitViews` on types
3. Name application sets `TsEmitName` on all types/members
4. Constraint resolution resolves `GenericParameterSymbol.Constraints`
5. Emit scope assignment sets all `EmitScope` to non-Unspecified
6. Graph updates use `WithUpdatedType()` for pure transformations

### Emit Phase (Reads Model)

1. PhaseGate validates all `EmitScope != Unspecified`
2. TypeScript emit reads `TsEmitName` and `EmitScope`
3. Metadata emit uses `StableId` for CLR bindings
4. Bindings emit maps `TsEmitName` → `StableId`

---

## Summary

**Model** provides:

1. **Immutable data structures** for entire type system
2. **Stable identities** surviving all transformations
3. **Type-safe CLR representation**
4. **Provenance tracking** for member origins
5. **Emit scope control** for output placement
6. **Efficient lookups** via indices
7. **Pure transformations** via wither methods

Designed for:
- **Immutability** (records with init-only properties)
- **Structural equality** (record semantics)
- **Functional transformations** (wither methods, WithUpdatedType)
- **Type safety** (minimal nulls)
- **Performance** (immutable collections, pre-computed indices)

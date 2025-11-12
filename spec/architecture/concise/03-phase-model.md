# Phase MODEL: Data Structures

## Overview

Immutable data structures representing the complete symbol graph. Created during Load phase, transformed during Shape phase.

**Model provides:**
1. Type-safe representation of CLR types/members
2. Stable identities for tracking through transformations
3. Type references (generics, arrays, pointers)
4. Provenance tracking
5. Emit scope control

All structures are **immutable records** with structural equality and `with` expressions for transformations.

---

## SymbolGraph.cs

### Record: SymbolGraph
Root container for entire symbol graph.

**Properties:**
- `Namespaces: ImmutableArray<NamespaceSymbol>` - All namespaces with types
- `SourceAssemblies: ImmutableHashSet<string>` - Source assembly paths
- `NamespaceIndex: ImmutableDictionary<string, NamespaceSymbol>` - O(1) namespace lookup
- `TypeIndex: ImmutableDictionary<string, TypeSymbol>` - O(1) type lookup by CLR name

**Key Methods:**
- `WithIndices()` - Build indices for fast lookups (MUST call after creation)
- `TryGetNamespace(string name)` - Safe namespace lookup
- `TryGetType(string clrFullName)` - Safe type lookup (handles nested)
- `WithUpdatedType(string key, Func<TypeSymbol, TypeSymbol> transform)` - Update single type, return new graph
- `GetStatistics()` - Count namespaces, types, members

### Record: SymbolGraphStatistics
- `NamespaceCount`, `TypeCount`, `MethodCount`, `PropertyCount`, `FieldCount`, `EventCount`, `TotalMembers`

---

## AssemblyKey.cs

### Record Struct: AssemblyKey
Normalized assembly identity.

```csharp
record struct AssemblyKey(string Name, string PublicKeyToken, string Culture, string Version)
```

**Computed Properties:**
- `SimpleName` - Name without version/culture/token
- `IdentityString` - Full identity: `"Name, Version=X, Culture=Y, PublicKeyToken=Z"`

**Methods:**
- `FromAssemblyName(AssemblyName)` - Convert from System.Reflection type
- `ToAssemblyName()` - Convert to System.Reflection type
- `ToSortKey()` - For stable sort order

**Equality:** By all four components. **Hash:** Combines all components.

---

## NamespaceSymbol.cs

### Record: NamespaceSymbol
Container for types in a namespace.

**Properties:**
- `Name: string` - Namespace name (e.g., `"System.Collections.Generic"`)
- `Types: ImmutableArray<TypeSymbol>` - All types (classes, interfaces, enums, etc.)

---

## TypeSymbol.cs

### Record: TypeSymbol
Represents a CLR type (class, interface, struct, enum, delegate).

**Core Identity:**
- `StableId: TypeStableId` - Assembly-qualified identity
- `ClrFullName: string` - CLR name with backtick generics (`List\`1`)
- `ClrSimpleName: string` - Name without namespace
- `NamespaceName: string` - Containing namespace

**Type Classification:**
- `Kind: TypeKind` - Class/Interface/Struct/Enum/Delegate/StaticNamespace
- `Accessibility: Accessibility` - Public/Internal/Private

**TypeScript Naming:**
- `TsEmitName: string?` - Final TypeScript name (null until Phase 3.5)
- `TsNamespace: string?` - TypeScript namespace (if different from CLR)

**Type System:**
- `GenericParameters: ImmutableArray<GenericParameterSymbol>` - Type parameters
- `BaseClass: TypeReference?` - Base type
- `Interfaces: ImmutableArray<TypeReference>` - Implemented interfaces

**Members:**
- `Members: TypeMembers` - All members (methods, properties, fields, events, constructors)
- `NestedTypes: ImmutableArray<TypeSymbol>` - Nested types

**Emit Control:**
- `ExplicitViews: ImmutableArray<ExplicitView>` - As_IInterface view properties

**Metadata:**
- `IsAbstract`, `IsSealed`, `IsStatic`, `IsValueType`, `IsEnum`, `IsDelegate`, `IsNested`

**Wither Methods:**
- `WithAddedMethods()`, `WithAddedProperties()`, `WithAddedFields()`, `WithUpdatedMember()`
- `WithTsEmitName()`, `WithBaseClass()`, `WithInterfaces()`, `WithExplicitViews()`

---

## TypeMembers.cs

### Record: TypeMembers
Container for all members of a type.

**Properties:**
- `Methods: ImmutableArray<MethodSymbol>`
- `Properties: ImmutableArray<PropertySymbol>`
- `Fields: ImmutableArray<FieldSymbol>`
- `Events: ImmutableArray<EventSymbol>`
- `Constructors: ImmutableArray<ConstructorSymbol>`

**Methods:**
- `WithAddedMethods()`, `WithAddedProperties()`, etc.
- `GetAllMembers()` - Flat list of all members
- `GetMemberByStableId(MemberStableId)` - Lookup by identity

---

## MemberSymbols/

### Record: MethodSymbol

**Core Identity:**
- `StableId: MemberStableId` - Assembly-qualified member identity
- `ClrName: string` - CLR method name
- `TsEmitName: string?` - TypeScript name (null until Phase 3.5)

**Signature:**
- `Parameters: ImmutableArray<ParameterSymbol>`
- `ReturnType: TypeReference`
- `GenericParameters: ImmutableArray<GenericParameterSymbol>`

**Metadata:**
- `IsStatic`, `IsVirtual`, `IsOverride`, `IsAbstract`, `IsSealed`, `IsNew`
- `Visibility: Visibility` - Public/Protected/Internal/Private
- `Provenance: MemberProvenance` - Original/ExplicitView/BaseOverload/etc.
- `EmitScope: EmitScope` - ClassSurface/ViewOnly/StaticSurface/Omitted

**Optional:**
- `SourceInterface: TypeReference?` - For explicit impl ViewOnly members
- `ExplicitInterfaceImpl: string?` - Qualified name if explicit impl

**Methods:**
- `WithTsEmitName()`, `WithEmitScope()`, `WithProvenance()`, `WithReturnType()`

### Record: ParameterSymbol

**Properties:**
- `Name: string`
- `Type: TypeReference`
- `IsRef`, `IsOut`, `IsParams`, `IsOptional`
- `DefaultValue: object?`

### Record: PropertySymbol

**Core:**
- `StableId`, `ClrName`, `TsEmitName`, `Type`, `Visibility`, `Provenance`, `EmitScope`

**Metadata:**
- `IsStatic`, `IsReadOnly`, `IsIndexer`, `IsOverride`, `IsNew`
- `IndexParameters: ImmutableArray<ParameterSymbol>` - For indexers
- `SourceInterface: TypeReference?`

### Record: FieldSymbol

**Core:**
- `StableId`, `ClrName`, `TsEmitName`, `Type`, `Visibility`, `Provenance`, `EmitScope`

**Metadata:**
- `IsStatic`, `IsReadOnly`, `IsConst`
- `ConstValue: object?`

### Record: EventSymbol

**Core:**
- `StableId`, `ClrName`, `TsEmitName`, `Type` (handler delegate type), `Visibility`, `Provenance`, `EmitScope`

**Metadata:**
- `IsStatic`, `IsOverride`, `IsNew`
- `SourceInterface: TypeReference?`
- `ExplicitInterfaceImpl: string?`

### Record: ConstructorSymbol

**Core:**
- `StableId`, `ClrName`, `TsEmitName` (always null - not emitted in d.ts)
- `Parameters: ImmutableArray<ParameterSymbol>`
- `Visibility`, `Provenance`, `EmitScope`

**Metadata:**
- `IsStatic` (static constructor vs instance)

---

## TypeReference Hierarchy

### Abstract Record: TypeReference
Base for all type references.

**Kind: TypeReferenceKind** - Named/GenericParameter/Array/Pointer/ByRef/Placeholder

### Record: NamedTypeReference
Regular types (classes, interfaces, structs, enums).

**Properties:**
- `AssemblyName: string`
- `Namespace: string`
- `Name: string` - Simple name
- `TypeArguments: ImmutableArray<TypeReference>` - For generics
- `InterfaceStableId: string?` - Precomputed "{assembly}:{fullName}" for interfaces

**Methods:**
- `GetOpenGenericForm()` - Remove type arguments, keep arity
- `IsGeneric` - Has type arguments
- `Arity` - Type argument count

### Record: GenericParameterReference
Reference to type parameter (`T` in `List<T>`).

**Properties:**
- `Id: GenericParameterId` - Identifies parameter
- `Name: string` - Parameter name

**GenericParameterId:** `DeclaringTypeClrFullName + Position`

### Record: ArrayTypeReference
Array types.

**Properties:**
- `ElementType: TypeReference`
- `Rank: int` - 1 for T[], 2 for T[,], etc.

### Record: PointerTypeReference
Pointer types (unsafe).

**Properties:**
- `PointeeType: TypeReference`
- `PointerDepth: int` - 1 for T*, 2 for T**, etc.

### Record: ByRefTypeReference
Ref/out parameters.

**Properties:**
- `ReferencedType: TypeReference`

### Record: PlaceholderTypeReference
Cycle breaker for recursive constraints.

**Properties:** None (sentinel value)

---

## Enums

### Enum: TypeKind
`Class`, `Interface`, `Struct`, `Enum`, `Delegate`, `StaticNamespace` (abstract+sealed class)

### Enum: Accessibility
`Public`, `Internal`, `Protected`, `Private`, `ProtectedOrInternal`, `ProtectedAndInternal`

### Enum: Visibility
`Public`, `Protected`, `Internal`, `Private`, `ProtectedInternal`, `PrivateProtected`

### Enum: MemberProvenance
Tracks member origin:
- `Original` - From CLR reflection
- `ExplicitView` - Explicit interface impl (synthesized)
- `BaseOverload` - Base class overload (synthesized)
- `IndexerNormalized` - Indexer converted to methods
- `HiddenNew` - C# 'new' keyword hiding

### Enum: EmitScope
Placement control:
- `ClassSurface` - Direct on class/interface
- `StaticSurface` - Static section
- `ViewOnly` - As_IInterface view property
- `Omitted` - Not emitted (tracked in metadata)

### Enum: Variance
For generic parameters:
`None`, `Covariant` (out), `Contravariant` (in)

### Enum: GenericParameterConstraints (Flags)
- `None` - No special constraints
- `ReferenceType` - class constraint
- `ValueType` - struct constraint
- `DefaultConstructor` - new() constraint

### Enum: TypeReferenceKind
`Named`, `GenericParameter`, `Array`, `Pointer`, `ByRef`, `Placeholder`

---

## GenericParameterSymbol.cs

### Record: GenericParameterSymbol
Represents a generic type parameter.

**Properties:**
- `Name: string` - Parameter name (e.g., "T")
- `Position: int` - 0-based position
- `Variance: Variance` - None/Covariant/Contravariant
- `Constraints: ImmutableArray<TypeReference>` - Type constraints
- `SpecialConstraints: GenericParameterConstraints` - Flags (class/struct/new)

---

## StableId Types

### Record Struct: TypeStableId
Assembly-qualified type identity.

```csharp
record struct TypeStableId(string AssemblyName, string ClrFullName)
```

**Format:** `"AssemblyName:ClrFullName"`
- Example: `"System.Private.CoreLib:System.Collections.Generic.List\`1"`

### Record Struct: MemberStableId
Assembly-qualified member identity.

```csharp
record struct MemberStableId(
    string AssemblyName,
    string DeclaringTypeClrFullName,
    string MemberName,
    string CanonicalSignature,
    int MetadataToken)
```

**Format:** `"AssemblyName:DeclaringType::MemberName(Signature):ReturnType"`
- Example: `"System.Private.CoreLib:System.Decimal::ToString(IFormatProvider):String"`

**Equality:** By assembly, declaring type, member name, and signature. **Metadata token excluded from equality.**

---

## ExplicitView.cs

### Record: ExplicitView
Represents an As_IInterface view property.

**Properties:**
- `InterfaceReference: TypeReference` - Interface being viewed
- `ViewPropertyName: string` - TypeScript property name (e.g., `"As_IConvertible"`)
- `ViewMembers: ImmutableArray<MemberSymbol>` - Members in this view

---

## Summary

**Model architecture:**
- **Root:** SymbolGraph contains Namespaces
- **Hierarchy:** Graph → Namespaces → Types → Members
- **Identity:** StableIds provide stable cross-phase identity
- **Type System:** TypeReference hierarchy captures full CLR type system
- **Emit Control:** EmitScope + Provenance track member placement and origin
- **Immutability:** All pure functional transformations via `with` expressions

**Key principles:**
- Structural equality for all records
- Immutable collections (ImmutableArray, ImmutableHashSet, ImmutableDictionary)
- No null references except for optional/late-bound properties (TsEmitName, SourceInterface)
- Fast lookups via indices (NamespaceIndex, TypeIndex)
- Generic-aware type references (NamedTypeReference.TypeArguments)

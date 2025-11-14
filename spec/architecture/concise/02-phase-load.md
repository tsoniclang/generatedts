# Phase 2: Load (Reflection)

## Overview

**Load phase** performs reflection over .NET assemblies to extract pure CLR metadata. Operates entirely in CLR domain—no TypeScript concepts. Builds complete `SymbolGraph` with types, members, and relationships.

**Key responsibilities**:
- Load assemblies with transitive closure (BFS)
- Validate assembly identity (version consistency, PublicKeyToken)
- Extract all public types/members via reflection
- Build type references (named, generic, array, pointer, byref)
- Substitute type parameters in closed generic interfaces

**Key constraint**: Pure CLR data only—no `TsEmitName`, no TypeScript transformations (later phases).

---

## File: AssemblyLoader.cs

### Purpose
Creates `MetadataLoadContext` for loading assemblies in isolation. Handles reference pack resolution for .NET BCL. Implements transitive closure loading via BFS. Validates assembly identity consistency.

### Method: CreateLoadContext
```csharp
public MetadataLoadContext CreateLoadContext(IReadOnlyList<string> assemblyPaths)
```
Creates MetadataLoadContext for given assemblies. Uses `PathAssemblyResolver` to search:
1. Directory containing target assemblies
2. Reference assemblies directory (same as target for version consistency)

### Method: LoadClosure
```csharp
public LoadClosureResult LoadClosure(
    IReadOnlyList<string> seedPaths,
    IReadOnlyList<string> refPaths,
    bool strictVersions = false)
```

**Main entry point** for full BCL generation. Loads transitive closure via BFS.

**5 Phases**:
1. **BuildCandidateMap**: Scan reference directories → `AssemblyKey → List<string>` map
2. **ResolveClosure**: BFS over assembly references → `AssemblyKey → string` (resolved path)
3. **ValidateAssemblyIdentity**: Guard PG_LOAD_002/003/004 (PublicKeyToken, version drift, retargetable)
4. **FindCoreLibrary**: Locate System.Private.CoreLib (required for MetadataLoadContext)
5. **Create MetadataLoadContext**: Load all assemblies in dependency order

**Returns**: `LoadClosureResult` (MetadataLoadContext, assemblies, resolved paths)

### BFS Closure Algorithm (ResolveClosure)
1. Queue seed assemblies
2. Dequeue current assembly
3. Skip if visited
4. **Version policy**: If already resolved, keep highest version
5. Load metadata to read references (PEReader/MetadataReader - lightweight)
6. For each reference:
   - Look up in candidateMap
   - If not found: Skip silently (PhaseGate validates later)
   - If found: Pick highest version, enqueue
7. Continue until queue empty

**Key behaviors**:
- Version upgrades: v2.0 wins over v1.0
- Missing references: Silently skipped (validated by PhaseGate)
- Lightweight loading: PEReader only, no Assembly.Load

### Validation Guards (ValidateAssemblyIdentity)
- **PG_LOAD_002**: Mixed PublicKeyToken → ERROR
- **PG_LOAD_003**: Version drift → ERROR (strict mode) or WARNING
- **PG_LOAD_004**: Retargetable/ContentType → Placeholder

---

## File: ReflectionReader.cs

### Purpose
Reflects over loaded assemblies to build `SymbolGraph`. Extracts types, members, type references. Handles MetadataLoadContext specifics (external types, unresolved references).

### Method: ReadAssemblies
```csharp
public static SymbolGraph ReadAssemblies(
    BuildContext ctx,
    IReadOnlyList<Assembly> assemblies,
    MetadataLoadContext loadContext)
```

**Main entry point** for reflection. Returns SymbolGraph with all types/members.

**Algorithm**:
1. For each assembly:
   - Get all types via `assembly.GetTypes` (includes nested)
   - Filter to public types only
   - Group types by namespace
   - For each namespace:
     - Build NamespaceSymbol
     - For each type: Build TypeSymbol (via `BuildTypeSymbol`)
     - For each member: Build MemberSymbol (via `BuildMemberSymbol`)
2. Build SymbolGraph with all namespaces
3. Return SymbolGraph

### Method: BuildTypeSymbol
```csharp
private static TypeSymbol BuildTypeSymbol(Type type, BuildContext ctx)
```

Extracts type metadata:
- **Basic**: Name, FullName, Namespace, Accessibility, Modifiers (abstract, sealed, static)
- **Kind**: Class, Interface, Struct, Enum, Delegate
- **Generics**: Type parameters, constraints
- **Hierarchy**: Base type, interfaces
- **Members**: Methods, properties, fields, events, constructors
- **Nested types**: Recursively process nested types

**Key**: All type references built via `TypeReferenceFactory.Create(type, ctx)`

### Method: BuildMemberSymbol
```csharp
private static MemberSymbol BuildMemberSymbol(MemberInfo member, BuildContext ctx)
```

Dispatches to specialized builders based on member type:
- **MethodInfo** → `BuildMethodSymbol`
- **PropertyInfo** → `BuildPropertySymbol`
- **FieldInfo** → `BuildFieldSymbol`
- **EventInfo** → `BuildEventSymbol`
- **ConstructorInfo** → `BuildConstructorSymbol`

Each builder extracts:
- Name, CLR name, accessibility, modifiers
- Signature (parameters, return type)
- Metadata (virtual, override, static, abstract)
- Type references via TypeReferenceFactory

---

## File: TypeReferenceFactory.cs

### Purpose
Builds `TypeReference` objects from `System.Type` instances. Handles all type forms (named, generic, array, pointer, byref). Resolves external types (not in current assembly set).

### Method: Create
```csharp
public static TypeReference Create(Type type, BuildContext ctx)
```

**Main entry point**. Dispatches to specialized methods based on type characteristics.

**Algorithm**:
1. **IsGenericParameter**: `GenericParameterReference` (T, TKey, TValue)
2. **IsPointer**: `PointerTypeReference` (int*, byte*)
3. **IsByRef**: `ByRefTypeReference` (ref int, out string)
4. **IsArray**: `ArrayTypeReference` (int[], string[,])
5. **IsGenericType**:
   - If generic type definition: `NamedTypeReference` (open generic List`1)
   - If constructed generic: `GenericTypeReference` (closed List<int>)
6. **Default**: `NamedTypeReference` (simple types like int, string, Decimal)

### Named Type Creation
```csharp
private static NamedTypeReference CreateNamed(Type type, BuildContext ctx)
```

Builds NamedTypeReference for non-generic or open generic types.

**Fields**:
- `FullName`: CLR full name (e.g., "System.Collections.Generic.List`1")
- `AssemblyName`: Declaring assembly name
- `Namespace`: Namespace (e.g., "System.Collections.Generic")
- `SimpleName`: Name without namespace (e.g., "List`1")
- `IsExternal`: True if type not in current assembly set

**Nested type handling**: Composes full name from declaring types (A+B+C → "A+B+C")

### Generic Type Creation
```csharp
private static GenericTypeReference CreateGeneric(Type type, BuildContext ctx)
```

Builds GenericTypeReference for constructed generic types (List<int>, Dictionary<string, int>).

**Fields**:
- `TypeDefinition`: NamedTypeReference to open generic (List`1)
- `TypeArguments`: Array of TypeReferences (int, string, etc.)

**Algorithm**:
1. Get generic type definition via `type.GetGenericTypeDefinition`
2. Create NamedTypeReference for definition
3. Get type arguments via `type.GetGenericArguments`
4. Recursively create TypeReferences for each argument
5. Return GenericTypeReference

### Array/Pointer/ByRef Creation
- **ArrayTypeReference**: `ElementType + Rank` (int[] → ElementType=int, Rank=1)
- **PointerTypeReference**: `PointedType` (int* → PointedType=int)
- **ByRefTypeReference**: `ReferencedType` (ref int → ReferencedType=int)

---

## File: DeclaringAssemblyResolver.cs

### Purpose
Resolves declaring assembly for types across assembly boundaries. Handles cross-assembly dependencies and external type references.

### Method: ResolveDeclaringAssembly
```csharp
public static string ResolveDeclaringAssembly(
    Type type,
    IReadOnlyDictionary<AssemblyKey, string> resolvedPaths)
```

**What it does**:
- Takes a Type instance from MetadataLoadContext
- Returns the declaring assembly's file path
- Looks up path in resolvedPaths map (from AssemblyLoader.LoadClosure)

**Why needed**: External types referenced in signatures need assembly path for StableId computation.

**Algorithm**:
1. Get type's assembly via `type.Assembly`
2. Create AssemblyKey from assembly name
3. Look up in resolvedPaths map
4. Return path if found, null otherwise

**Usage**: Called by ReflectionReader when building TypeReferences for external types

---

## File: InterfaceMemberSubstitution.cs

### Purpose
Substitutes type parameters in closed generic interface members. Handles cases like `IEnumerable<int>.GetEnumerator` → `IEnumerator<int>` (not `IEnumerator<T>`).

### Why Needed
When reflecting over closed generic interfaces, member signatures contain open type parameters:
```csharp
// Given: class List<int> : IEnumerable<int>
// Without substitution:
interface IEnumerable<T> {
    GetEnumerator: IEnumerator<T>  // Wrong - returns IEnumerator<T> not IEnumerator<int>
}

// With substitution:
interface IEnumerable<int> {
    GetEnumerator: IEnumerator<int>  // Correct - substituted T → int
}
```

### Method: SubstituteClosedInterfaces
```csharp
public static SymbolGraph SubstituteClosedInterfaces(
    BuildContext ctx,
    SymbolGraph graph)
```

**Algorithm**:
1. For each type in graph:
2. For each interface type:
   - Check if generic (has type arguments)
   - Build substitution map: `{T → int, TKey → string, TValue → bool}`
3. For each interface member:
   - Substitute type parameters in return type
   - Substitute type parameters in parameter types
   - Create new MemberSymbol with substituted types
4. Return new SymbolGraph with substituted interfaces

**Example substitution map**:
- `IEnumerable<int>` → `{T → int}`
- `IDictionary<string, bool>` → `{TKey → string, TValue → bool}`

---

## Integration with Pipeline

### Input
- Assembly DLL paths (string[])

### Process
1. **AssemblyLoader.LoadClosure** → Load all assemblies
2. **ReflectionReader.ReadAssemblies** → Extract types/members
3. **InterfaceMemberSubstitution.SubstituteClosedInterfaces** → Fix generic interfaces

### Output
- **SymbolGraph** containing:
  - All namespaces
  - All public types (classes, interfaces, structs, enums, delegates)
  - All public members (methods, properties, fields, events, constructors)
  - All type references (base types, interfaces, signatures)
  - All nested types
  - All substituted generic interface members

**Data characteristics**:
- Pure CLR metadata (no TypeScript names)
- `TsEmitName = null` for all symbols
- `EmitScope` not yet determined
- Interface hierarchies not yet flattened
- No ViewOnly members yet
- No deduplication yet

### Next Phase
Phase 3 (Normalize) builds indices and begins TypeScript transformations.

---

## Key Concepts

### MetadataLoadContext
Isolated assembly loading context. Allows loading assemblies without executing code. Required for reflecting over BCL assemblies (can't load System.Private.CoreLib into runtime).

**Benefits**:
- No assembly version conflicts
- No runtime execution
- Can load assemblies for different .NET versions
- Lightweight (metadata only, no JIT)

### Assembly Identity Validation
Three guards ensure consistency:
1. **PG_LOAD_002**: No mixed PublicKeyToken for same assembly name
2. **PG_LOAD_003**: No major version drift (configurable: error or warning)
3. **PG_LOAD_004**: No retargetable/ContentType conflicts (future)

### Version Policy
When multiple versions of same assembly referenced: **highest version wins**

Example:
- Assembly A references System.Collections v1.0
- Assembly B references System.Collections v2.0
- Result: System.Collections v2.0 loaded

### Type Reference Forms
- **NamedTypeReference**: Simple types (int, string, List`1)
- **GenericTypeReference**: Closed generics (List<int>, Dictionary<string, bool>)
- **ArrayTypeReference**: Arrays (int[], string[,])
- **PointerTypeReference**: Pointers (int*, byte*)
- **ByRefTypeReference**: By-ref (ref int, out string)
- **GenericParameterReference**: Type parameters (T, TKey, TValue)

### External Types
Types not in current assembly set. Marked with `IsExternal = true`. Validated later by PhaseGate (PG_LOAD_001: all external types must be resolvable).

---

## Files Summary

| File | Purpose | Key Methods |
|------|---------|-------------|
| **AssemblyLoader.cs** | Assembly loading, closure resolution | LoadClosure, ResolveClosure, ValidateAssemblyIdentity |
| **ReflectionReader.cs** | Reflection over types/members | ReadAssemblies, BuildTypeSymbol, BuildMemberSymbol |
| **TypeReferenceFactory.cs** | Build TypeReference objects | Create, CreateNamed, CreateGeneric |
| **DeclaringAssemblyResolver.cs** | Cross-assembly type resolution | ResolveDeclaringAssembly |
| **InterfaceMemberSubstitution.cs** | Substitute generic interface members | SubstituteClosedInterfaces |

---

## Summary

**Load phase** is pure reflection over .NET assemblies:
1. Load assembly transitive closure (BFS)
2. Validate assembly identity (PublicKeyToken, version drift)
3. Reflect over all types/members
4. Build type references (all forms)
5. Substitute closed generic interface members
6. Return SymbolGraph (pure CLR data)

**Output**: Complete metadata graph ready for TypeScript transformation (starts in Phase 3: Normalize/Shape).

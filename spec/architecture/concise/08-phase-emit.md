# Phase 4: Emit - Output File Generation

## Overview

**Emit Phase** takes validated `EmissionPlan` and generates all output files:
1. TypeScript Declarations (.d.ts) - Public facade + internal implementation
2. Metadata Sidecars (.metadata.json) - CLR semantics for Tsonic compiler
3. Binding Metadata (.bindings.json) - TS name → CLR name mappings
4. Module Stubs (.js) - Runtime stubs (throw errors)
5. Support Types (_support/types.d.ts) - Shared marker types

**Key Principles**: Uses Renamer for all names, respects EmitScope, uses TypeNameResolver for type safety, deterministic output order

---

## Output Directory Structure

```
output/
  System/
    index.d.ts          # Public facade
    index.js            # Runtime stub
    bindings.json       # Name mappings
    internal/
      index.d.ts        # Actual declarations
      metadata.json     # CLR metadata
  _support/
    types.d.ts          # Shared marker types
```

**Special**: Root namespace uses `_root/` instead of `internal/`

---

## File: FacadeEmitter.cs

### Purpose
Generates public-facing `index.d.ts` for each namespace (entry points).

### Method: Emit()
Iterates `plan.EmissionOrder.Namespaces` in deterministic order, generates facade via `GenerateFacade()`, writes to `output/{namespace}/index.d.ts`.

### Method: GenerateFacade()
Generates facade content:
1. File header
2. Internal import: `import * as Internal from './internal/index';`
3. Dependency imports with aliases
4. Namespace re-export: `export import System = Internal.System;` (non-dotted only)
5. Individual type exports: `export type List_1 = Internal.System.Collections.Generic.List_1;`

---

## File: InternalIndexEmitter.cs

### Purpose
Generates `internal/index.d.ts` for each namespace (actual type declarations).

### Method: Emit()
Main entry point. Emits internal declarations for all namespaces.

**Output structure**:
- File header
- Imports from dependencies
- Namespace declaration with types
- Companion view interfaces (for types with explicit views)

### Key Features
- Emits types in EmitOrder
- Handles nested types
- Emits view interfaces after main namespace
- Uses ClassPrinter/InterfacePrinter for type emission

---

## File: Shared/NameUtilities.cs

### Purpose
Implements **CLR-name contract** - consistent naming policy across all emission surfaces. Ensures interfaces and classes emit matching member names to prevent TS2420 errors.

**Key Principle**: Use CLR names (PascalCase from reflection), sanitize reserved words, NEVER use numeric suffixes.

### What is the CLR-Name Contract?

Ensures consistent member names across:
- Class surface members (`EmitScope = ClassSurface`)
- Interface members
- Explicit interface implementations (`EmitScope = ViewOnly`)

**Policy rules**:
1. Start with CLR name (PascalCase from System.Reflection)
2. Sanitize reserved words (append `_`)
3. NEVER use numeric suffixes (no equals2, getHashCode3)

**Why it matters**:
```typescript
// BAD - Without CLR-name contract
interface IDisposable {
    Dispose(): void;  // CLR name
}
class FileStream implements IDisposable {
    dispose(): void;  // lowercase! TS2420 error
}

// GOOD - With CLR-name contract
interface IDisposable {
    Dispose(): void;
}
class FileStream implements IDisposable {
    Dispose(): void;  // Matching name ✓
}
```

**Impact**: Reduced TS2420 errors by 81% (579 → ~100 errors)

### Method: ApplyClrSurfaceNamePolicy()
```csharp
public static string ApplyClrSurfaceNamePolicy(string clrName)
```

**Main public API** for applying CLR-name contract.

**What it does**:
- Takes CLR name (e.g., "Dispose", "GetHashCode", "ToString")
- Returns sanitized identifier safe for TypeScript
- Used by PhaseGate validators to verify interface/class compatibility

**Examples**:
```csharp
ApplyClrSurfaceNamePolicy("Dispose")    → "Dispose"
ApplyClrSurfaceNamePolicy("default")    → "default_"  // Reserved word
ApplyClrSurfaceNamePolicy("class")      → "class_"    // Reserved word
```

**Usage in PhaseGate**:
```csharp
// From Plan/Validation/Names.cs:ValidateClrSurfaceNamePolicy()
var surfaceNames = new HashSet<string>();

// Build class surface set
foreach (var method in classMethods) {
    var surfaceName = NameUtilities.ApplyClrSurfaceNamePolicy(method.ClrName);
    surfaceNames.Add(surfaceName);
}

// Validate interfaces
foreach (var ifaceMethod in interfaceMethods) {
    var expectedName = NameUtilities.ApplyClrSurfaceNamePolicy(ifaceMethod.ClrName);
    if (!surfaceNames.Contains(expectedName)) {
        // ERROR: TBG8A1 (SurfaceNamePolicyMismatch)
    }
}
```

### Method: SanitizeIdentifier()
```csharp
private static string SanitizeIdentifier(string name)
```

Appends `_` to TypeScript/JavaScript reserved words. Uses `Renaming.TypeScriptReservedWords.Sanitize()`.

**Examples**:
- `"GetType"` → `"GetType"` (not reserved)
- `"default"` → `"default_"` (reserved)
- `"class"` → `"class_"` (reserved)

### Method: HasNumericSuffix()
```csharp
public static bool HasNumericSuffix(string name)
```

Detects if name ends with numeric digits (e.g., equals2, getHashCode3). Used by PhaseGate validator PG_NAME_SURF_002 (currently disabled).

**Algorithm**: Start from end, walk backward while digits, return true if found trailing digits.

**Examples**:
- `"Dispose"` → false
- `"ToInt32"` → true (legitimate CLR name!)
- `"equals2"` → true (renaming artifact - BAD)

**Why PG_NAME_SURF_002 is disabled**: Cannot distinguish legitimate CLR names with numbers (ToInt32, UTF8Encoding, MD5) from renaming artifacts (equals2, toString3). To re-enable, needs to compare against original CLR names.

### Method: IsNonNumericOverride()
```csharp
private static bool IsNonNumericOverride(string clrName, string renamedName)
```

Detects if renamer applied semantic override (not numeric suffix). Returns true if renamed differs from CLR name non-numerically. Currently unused, designed for future disambiguation logic.

### Integration with PhaseGate

**Validator: PG_NAME_SURF_001** (TBG8A1 - SurfaceNamePolicyMismatch)
- Uses `ApplyClrSurfaceNamePolicy()` to validate interface/class compatibility
- Ensures emit phase will produce matching names
- Prevents TS2420 errors at generation time

**Validator: PG_NAME_SURF_002** (TBG8A2 - NumericSuffixOnSurface)
- Uses `HasNumericSuffix()` to detect renaming artifacts
- Currently DISABLED due to legitimate CLR names with numbers

### Integration with Emit Phase

**Design Note**: NameUtilities is primarily a **validation-time API** (used by PhaseGate), not an emit-time API. The emit phase uses `BuildContext.Renamer` which respects the CLR-name contract through PhaseGate enforcement.

---

## File: Printers/ClassPrinter.cs

### Purpose
Prints TypeScript class declarations from TypeSymbol. Handles classes, structs, static classes, enums, delegates, interfaces.

### Method: Print()
```csharp
public static string Print(TypeSymbol type, TypeNameResolver resolver, BuildContext ctx, SymbolGraph graph)
```

**GUARD**: Never prints non-public types. Dispatches to specialized printer based on type.Kind.

**Parameters**: `graph` - SymbolGraph for type lookups (needed for TS2693 same-namespace view fix)

### Method: PrintClass()
```csharp
private static string PrintClass(TypeSymbol type, TypeNameResolver resolver, BuildContext ctx, SymbolGraph graph, bool instanceSuffix = false)
```

**Algorithm**:
1. Get final TypeScript name from Renamer
2. Add `$instance` suffix if requested
3. Emit class modifiers (`abstract` if abstract)
4. Emit generic parameters with constraints
5. **TS2693 FIX**: Apply same-namespace view handling to base class
6. **TS2863 FIX**: Filter `any`/`unknown` from extends clause
7. Emit base class (`extends BaseClass`, skip Object/ValueType/any/unknown)
8. **TS2693 FIX**: Apply same-namespace view handling to interface list
9. Emit interfaces (`implements IFoo, IBar`)
10. Call `EmitMembers()` to emit body

**Base class emission (with TS2693/TS2863 fixes)**:
```csharp
if (type.BaseType != null) {
    var baseTypeName = TypeRefPrinter.Print(type.BaseType, resolver, ctx);
    // TS2693 FIX: For same-namespace types with views, use instance class name
    baseTypeName = ApplyInstanceSuffixForSameNamespaceViews(baseTypeName, type.BaseType, type.Namespace, graph, ctx);

    // TS2863 FIX: Never emit "extends any" - TypeScript rejects it
    if (baseTypeName != "Object" &&
        baseTypeName != "ValueType" &&
        baseTypeName != "any" &&
        baseTypeName != "unknown") {
        sb.Append(" extends ");
        sb.Append(baseTypeName);
    }
}
```

**Interface emission (with TS2693 fix)**:
```csharp
if (type.Interfaces.Length > 0) {
    sb.Append(" implements ");
    var interfaceNames = type.Interfaces.Select(i => {
        var name = TypeRefPrinter.Print(i, resolver, ctx);
        // TS2693 FIX: For same-namespace types with views, use instance class name
        return ApplyInstanceSuffixForSameNamespaceViews(name, i, type.Namespace, graph, ctx);
    });
    sb.Append(string.Join(", ", interfaceNames));
}
```

**Why these fixes matter**:

1. **TS2693 (same-namespace views)**: Without fix, when class extends/implements type with views in same namespace, it references type alias name which isn't accessible inside namespace declarations:
```typescript
// BAD - Without fix
namespace System.Runtime.InteropServices {
    class SafeHandleZeroOrMinusOneIsInvalid extends SafeHandle { }
    // TS2693: 'SafeHandle' only refers to a type, but is being used as a value here.
}
```

2. **TS2863 (extends any)**: When TypeRefPrinter falls back to `any` for unmappable types, emitting "extends any" causes TypeScript errors:
```typescript
// BAD - Without fix
class Foo extends any { }
// TS2863: 'any' cannot be used as a base class or interface.
```

### Method: ApplyInstanceSuffixForSameNamespaceViews()
```csharp
private static string ApplyInstanceSuffixForSameNamespaceViews(
    string resolvedName,
    TypeReference typeRef,
    string currentNamespace,
    SymbolGraph graph,
    BuildContext ctx)
```

**TS2693 FIX**: Detects same-namespace types with views and returns instance class name (`TypeName$instance`).

**Algorithm**:
1. Check if typeRef is NamedTypeReference (not primitive, not generic parameter)
2. Look up type symbol in graph via ClrFullName
3. Check if type is in same namespace as current type
4. Check if type has explicit views (ViewOnly members)
5. If all conditions met, append `$instance` to name (before generic arguments if present)

**Implementation**:
```csharp
// Only applies to named types
if (typeRef is not NamedTypeReference named)
    return resolvedName;

// Look up type symbol
var clrFullName = named.FullName;
if (!graph.TypeIndex.TryGetValue(clrFullName, out var typeSymbol))
    return resolvedName; // External type

// Check if same namespace
if (typeSymbol.Namespace != currentNamespace)
    return resolvedName; // Cross-namespace (qualified names work)

// Check if type has views
if (typeSymbol.ExplicitViews.Length > 0 &&
    (typeSymbol.Kind == TypeKind.Class || typeSymbol.Kind == TypeKind.Struct)) {
    // Insert $instance BEFORE generic arguments
    var genericStart = resolvedName.IndexOf('<');
    if (genericStart >= 0) {
        // CORRECT: "Foo$instance<T>"
        return resolvedName.Substring(0, genericStart) + "$instance" + resolvedName.Substring(genericStart);
    } else {
        // No generic arguments
        return $"{resolvedName}$instance";
    }
}

return resolvedName;
```

**The Problem (TS2693)**:
Type aliases (`type Foo = ...`) are NOT accessible as values in heritage clauses inside namespace declarations. Only instance class (`Foo$instance`) is accessible as value.

**The Solution**:
Detect same-namespace references and use instance class name:
```typescript
namespace System.Runtime.InteropServices {
    type SafeHandle = SafeHandle$instance;
    class SafeHandle$instance { ... }

    // This WORKS:
    class SafeHandleZeroOrMinusOneIsInvalid extends SafeHandle$instance { }
}
```

**Why cross-namespace works without fix**:
Cross-namespace references use qualified imports at module level where type aliases ARE accessible.

**Generic type handling**:
For generics with views: Insert `$instance` before `<` to produce `Nullable$instance<int>` not `Nullable<int>$instance` (syntax error).

**Impact**: Commit 5880297 - "Fix same-namespace TS2693 by using $instance suffix in heritage clauses". Eliminated same-namespace TS2693 errors.

### Other Methods (Summary)

- **PrintStruct()**: Emits structs as classes (metadata notes value semantics)
- **PrintStaticClass()**: Emits as `abstract class` with static members only
- **PrintEnum()**: Emits `enum { Name = Value }`
- **PrintDelegate()**: Emits `type Delegate = (params) => ReturnType`
- **PrintInterface()**: Emits `interface { ... }`
- **EmitMembers()**: Emits instance members + calls EmitStaticMembers()
- **EmitStaticMembers()**: Emits static members with generic lifting (TS2302 prevention)
- **EmitInterfaceMembers()**: Emits interface members
- **PrintGenericParameter()**: Emits generic parameter with constraints

---

## File: Printers/MethodPrinter.cs

### Purpose
Prints method signatures. Handles parameters, return types, generic parameters, overloads.

### Key Methods
- **PrintMethod()**: Main entry point for method emission
- **PrintParameters()**: Emits parameter list with types
- **PrintReturnType()**: Emits return type via TypeRefPrinter

---

## File: Printers/TypeRefPrinter.cs

### Purpose
Prints TypeScript type references from TypeReference objects.

### Method: Print()
Dispatches based on TypeReference subtype:
- NamedTypeReference → Resolve via TypeNameResolver
- GenericTypeReference → `Foo<T, U>`
- ArrayTypeReference → `T[]` or `ReadonlyArray<T>`
- PointerTypeReference → `TSUnsafePointer<T>`
- ByRefTypeReference → `TSByRef<T>`
- GenericParameterReference → `T`

---

## File: MetadataEmitter.cs

### Purpose
Generates `metadata.json` with CLR semantics for Tsonic compiler.

### Method: Emit()
Emits metadata for each namespace:
- Type metadata (struct/class, static/abstract/sealed)
- Member metadata (virtual/override/static/abstract, ref/out parameters)
- Intentional omissions (indexers, generic static members)

---

## File: BindingEmitter.cs

### Purpose
Generates `bindings.json` mapping TypeScript names to CLR names.

### Method: Emit()
Emits bindings for each namespace:
- Type bindings: TS name → CLR full name
- Member bindings: TS member name → CLR member name + signature

---

## File: TypeNameResolver.cs

### Purpose
Single source of truth for resolving TypeScript identifiers from TypeReferences. Uses Renamer for all names.

### Method: For(TypeSymbol)
Returns final TypeScript identifier from Renamer.

### Method: For(NamedTypeReference)
**Algorithm**:
1. Try TypeMap FIRST (built-ins like System.Int32 → int)
2. Look up TypeSymbol in graph via StableId
3. If not in graph: External type, sanitize CLR name
4. Get final name from Renamer

---

## Integration with Pipeline

**Input**: EmissionPlan (validated SymbolGraph + imports + order)

**Process**:
1. SupportTypesEmit → _support/types.d.ts
2. For each namespace (in emission order):
   - InternalIndexEmitter → internal/index.d.ts
   - FacadeEmitter → index.d.ts
   - MetadataEmitter → metadata.json
   - BindingEmitter → bindings.json
   - ModuleStubEmitter → index.js

**Output**: Complete TypeScript declarations + metadata for Tsonic compiler

---

## Summary

**Emit phase** generates all output files using validated symbol graph:
- TypeScript declarations (.d.ts) for IDE/type checking
- Metadata (.metadata.json) for correct C# code generation
- Bindings (.bindings.json) for runtime binding
- Stubs (.js) to prevent execution

**Key Features**:
- **NameUtilities.cs**: CLR-name contract implementation (81% reduction in TS2420 errors)
- **TS2693 fix**: ApplyInstanceSuffixForSameNamespaceViews() for same-namespace view references
- **TS2863 fix**: Filter 'any'/'unknown' from extends clause

**Key components**: FacadeEmitter, InternalIndexEmitter, ClassPrinter (with TS2693/TS2863 fixes), NameUtilities (CLR-name contract), TypeNameResolver, MetadataEmitter, BindingEmitter

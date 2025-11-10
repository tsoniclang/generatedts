# Bindings JSON Schema

`<Namespace>/bindings.json` maps TypeScript names back to CLR names when naming transforms are active. This file is consumed by the Tsonic compiler for name resolution.

## Purpose

When naming transforms are applied (e.g., `--method-names camelCase`), TypeScript declarations use transformed names (`selectMany`) while CLR expects original names (`SelectMany`). The bindings file provides the mapping.

## Generated When

**Condition**: Any `--*-names camelCase` CLI option is used.

**Not generated**: If no naming transforms are active (all CLR names preserved in .d.ts).

## File Location

```
<output-dir>/
  System.Linq/
    internal/
      index.d.ts           # TypeScript declarations (transformed names)
    index.d.ts             # Facade
    metadata.json          # CLR semantics
    bindings.json          # ← Name mappings (this file)
```

## Root Schema

```json
{
  "SelectMany": {
    "Kind": "method",
    "Name": "SelectMany",
    "Alias": "selectMany",
    "FullName": "System.Linq.Enumerable.SelectMany"
  },
  "Enumerable": {
    "Kind": "class",
    "Name": "Enumerable",
    "Alias": "Enumerable",
    "FullName": "System.Linq.Enumerable"
  }
}
```

**Structure**: Flat dictionary keyed by CLR names.

| Field | Type | Description |
|-------|------|-------------|
| Key | string | CLR identifier (for fast lookup) |
| Value | BindingEntry | Mapping information |

## BindingEntry

```json
{
  "Kind": "method",
  "Name": "SelectMany",
  "Alias": "selectMany",
  "FullName": "System.Linq.Enumerable.SelectMany"
}
```

| Field | Type | Description |
|-------|------|-------------|
| `Kind` | string | Entity kind (see below) |
| `Name` | string | Original CLR identifier |
| `Alias` | string | TypeScript identifier (after transform) |
| `FullName` | string | Fully-qualified CLR name |

### Kind Values

| Value | Description |
|-------|-------------|
| `"namespace"` | Namespace |
| `"class"` | Class or struct |
| `"interface"` | Interface |
| `"enum"` | Enum type |
| `"delegate"` | Delegate type |
| `"method"` | Method |
| `"property"` | Property |
| `"field"` | Field (enum member) |
| `"enumMember"` | Enum member (alias for field) |
| `"parameter"` | Parameter (rare, only if param name transformed) |
| `"typeParameter"` | Generic type parameter (rare) |

## Complete Example

```json
{
  "System.Linq": {
    "Kind": "namespace",
    "Name": "System.Linq",
    "Alias": "systemLinq",
    "FullName": "System.Linq"
  },
  "Enumerable": {
    "Kind": "class",
    "Name": "Enumerable",
    "Alias": "Enumerable",
    "FullName": "System.Linq.Enumerable"
  },
  "SelectMany": {
    "Kind": "method",
    "Name": "SelectMany",
    "Alias": "selectMany",
    "FullName": "System.Linq.Enumerable.SelectMany"
  },
  "Where": {
    "Kind": "method",
    "Name": "Where",
    "Alias": "where",
    "FullName": "System.Linq.Enumerable.Where"
  },
  "Count": {
    "Kind": "property",
    "Name": "Count",
    "Alias": "count",
    "FullName": "System.Linq.Enumerable.Count"
  }
}
```

## Usage by Tsonic Compiler

### CLR → TypeScript Lookup
**Use case**: Validate that TypeScript code references correct transformed name.

```csharp
// Compiler has CLR name "SelectMany" from metadata
var binding = bindings["SelectMany"];
var tsName = binding.Alias; // "selectMany"
// Verify user wrote: list.selectMany(...) not list.SelectMany(...)
```

### TypeScript → CLR Lookup
**Use case**: Resolve TypeScript name back to CLR for runtime binding.

```csharp
// User wrote: list.selectMany(...)
// Find binding where Alias == "selectMany"
var binding = bindings.Values.First(b => b.Alias == "selectMany");
var clrName = binding.Name; // "SelectMany"
var fullName = binding.FullName; // "System.Linq.Enumerable.SelectMany"
// Emit C#: list.SelectMany(...)
```

### Missing Entries
**If name not found in bindings**: Assume no transform was applied (CLR name == TS name).

**Example**:
```csharp
// User wrote: list.Add(...)
// "Add" not in bindings → no transform
// Emit C#: list.Add(...)
```

## Transform Examples

### camelCase Transform

**Input**:
```bash
tsbindgen generate -a System.Linq.dll --method-names camelCase
```

**Generated .d.ts**:
```typescript
export namespace System.Linq {
  export class Enumerable {
    static selectMany<TSource, TResult>(...): ...;
    static where<TSource>(...): ...;
  }
}
```

**Generated bindings.json**:
```json
{
  "SelectMany": {
    "Kind": "method",
    "Name": "SelectMany",
    "Alias": "selectMany",
    "FullName": "System.Linq.Enumerable.SelectMany"
  },
  "Where": {
    "Kind": "method",
    "Name": "Where",
    "Alias": "where",
    "FullName": "System.Linq.Enumerable.Where"
  }
}
```

### No Transform (Default)

**Input**:
```bash
tsbindgen generate -a System.Linq.dll
```

**Result**: No `bindings.json` generated. TypeScript names == CLR names.

## Smart camelCase Rules

tsbindgen uses smart camelCase conversion:

| CLR Name | TypeScript Name | Explanation |
|----------|------------------|-------------|
| `SelectMany` | `selectMany` | Normal PascalCase → camelCase |
| `XMLParser` | `xmlParser` | Leading acronym → lowercase |
| `XML` | `xml` | All-caps → lowercase |
| `ToString` | `toString` | Lowercase first letter |
| `selectMany` | `selectMany` | Already camelCase → unchanged |

## Serialization Format

- **Encoding**: UTF-8
- **Formatting**: Indented (2 spaces)
- **Property naming**: PascalCase (matches C# conventions)
- **Null handling**: Null values not included
- **Empty bindings**: File omitted if no transforms applied

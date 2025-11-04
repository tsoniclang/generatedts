# Metadata Sidecar Generation

`generatedts` produces a `.metadata.json` file alongside the `.d.ts` for every
assembly.  The metadata describes runtime semantics that TypeScript cannot
express (virtual/override, abstract, accessibility, etc.) and is consumed by the
Tsonic emitter when producing C#.

## Processor

`Metadata/MetadataProcessor.ProcessTypeMetadata` drives metadata creation:

1. Identify the type kind (`class`, `struct`, `interface`, `enum`).
2. Capture modifiers (`isAbstract`, `isSealed`, `isStatic`).
3. Record base type (excluding `System.Object`, `System.ValueType`), plus
   implemented interfaces.
4. Walk public constructors, properties, and methods using
   `MetadataProcessor.ProcessConstructorMetadata`,
   `MetadataProcessor.ProcessPropertyMetadata`, and
   `MetadataProcessor.ProcessMethodMetadata` (implemented in `Emit` modules).

`SignatureFormatter` converts CLR signatures into C#-style string keys used in
the JSON object.

## JSON structure

Example outline:

```jsonc
{
  "assemblyName": "MyAssembly",
  "assemblyVersion": "1.2.3.4",
  "types": {
    "Namespace.MyClass": {
      "kind": "class",
      "isAbstract": false,
      "isSealed": false,
      "isStatic": false,
      "baseType": "Namespace.BaseClass",
      "interfaces": [ "Namespace.IMyInterface" ],
      "members": {
        "ctor(int,string)": { "kind": "constructor", "isStatic": false, ... },
        "Property": { "kind": "property", "isVirtual": true, ... },
        "Method(int)": { "kind": "method", "isOverride": true, ... }
      }
    }
  }
}
```

The member metadata records:

| Field | Meaning |
| --- | --- |
| `kind` | `constructor`, `property`, or `method` |
| `isVirtual` / `isAbstract` / `isSealed` / `isOverride` | Mirrors CLR modifiers |
| `isStatic` | Indicates static members |
| `accessibility` | `public`, `protected`, `internal`, etc. |
| `isIndexer` (properties) | Distinguishes indexers |

## Writer

`Metadata/MetadataWriter.WriteMetadataAsync` serialises the metadata model to
JSON using `System.Text.Json` with camelCase properties and indentation.  The
writer is invoked from `Cli/Program.GenerateDeclarationsAsync` after declaration
rendering.

This sidecar allows the downstream emitter to reproduce CLR dispatch behaviour
even though the `.d.ts` file only captures static types.

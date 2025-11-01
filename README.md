# generatedts

A .NET tool that generates TypeScript declaration files (`.d.ts`) from .NET assemblies for use with the Tsonic compiler.

## Overview

`generatedts` uses reflection to analyze .NET assemblies and produces TypeScript declarations that follow Tsonic's interop rules. This allows TypeScript code compiled with Tsonic to properly type-check when using .NET libraries.

## Installation

Build the tool from source:

```bash
dotnet build
```

## Usage

### Basic Usage

```bash
generatedts <assembly-path>
```

Example:

```bash
generatedts /usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.0/ref/net8.0/System.Text.Json.dll
# Creates: ./System.Text.Json.d.ts
```

### Command-Line Options

| Option | Short | Description | Default |
|--------|-------|-------------|---------|
| `--namespaces` | `-n` | Comma-separated list of namespaces to include | All namespaces |
| `--out-dir` | `-o` | Output directory for generated file | `.` (current directory) |
| `--log` | `-l` | Path to write JSON log file | None |
| `--config` | `-c` | Path to configuration JSON file | None |

### Examples

**Filter specific namespaces:**

```bash
generatedts System.Text.Json.dll --namespaces System.Text.Json.Serialization
```

**Specify output directory:**

```bash
generatedts System.Net.Http.dll --out-dir ./declarations
```

**Generate with logging:**

```bash
generatedts System.IO.dll --log build.log.json
```

## Generated Output

The tool generates TypeScript declarations with:

1. **Branded type aliases** for C# numeric types:
   ```typescript
   type int = number & { __brand: "int" };
   type decimal = number & { __brand: "decimal" };
   // ... etc
   ```

2. **Namespace declarations** matching .NET namespaces:
   ```typescript
   declare namespace System.Text.Json {
     class JsonSerializer {
       static Serialize<T>(value: T): string;
     }
   }
   ```

3. **Proper type mappings**:
   - `System.String` → `string`
   - `System.Int32` → `int`
   - `System.Boolean` → `boolean`
   - `Task<T>` → `Promise<T>`
   - `T[]` → `ReadonlyArray<T>`
   - `List<T>` → `List<T>`
   - `Nullable<T>` → `T | null`

## Configuration File

You can provide a JSON configuration file to customize behavior:

```json
{
  "skipNamespaces": ["System.Internal"],
  "typeRenames": {
    "System.OldType": "NewType"
  },
  "skipMembers": [
    "System.String::InternalMethod"
  ]
}
```

Usage:

```bash
generatedts Assembly.dll --config config.json
```

## Log Output

When using `--log`, a JSON file is generated with:

```json
{
  "timestamp": "2025-11-01T13:03:38Z",
  "namespaces": ["System.Text.Json"],
  "typeCounts": {
    "classes": 40,
    "interfaces": 5,
    "enums": 10,
    "total": 55
  },
  "warnings": []
}
```

## Type Mapping Rules

The tool follows Tsonic's type mapping specification:

- **Classes** → TypeScript classes
- **Interfaces** → TypeScript interfaces
- **Enums** → TypeScript enums
- **Structs** → TypeScript classes
- **Static methods** → `static` methods
- **Properties** → TypeScript properties (with `readonly` when appropriate)
- **Generic types** → TypeScript generics `<T>`
- **Optional parameters** → `param?: Type`
- **Params arrays** → `...values: ReadonlyArray<T>`

## Excluded Members

The tool automatically skips:

- Private and internal members
- Compiler-generated types
- Common Object methods (`Equals`, `GetHashCode`, `ToString`, `GetType`, `ReferenceEquals`)
- Special-name members (property accessors, backing fields)

## Development

### Project Structure

```
generatedts/
├── Src/
│   ├── Program.cs              # CLI entry point
│   ├── AssemblyProcessor.cs    # Reflection and type extraction
│   ├── TypeMapper.cs           # C# to TypeScript type mapping
│   ├── DeclarationRenderer.cs  # TypeScript output generation
│   ├── TypeInfo.cs             # Data structures
│   ├── GeneratorConfig.cs      # Configuration support
│   └── GenerationLogger.cs     # Logging functionality
└── README.md
```

### Building

```bash
dotnet build
```

### Running

```bash
dotnet run --project Src -- <assembly-path> [options]
```

## Related Documentation

- [Tsonic Type Mappings](../tsonic/spec/04-type-mappings.md)
- [.NET Interop](../tsonic/spec/08-dotnet-interop.md)
- [.NET Declarations](../tsonic/spec/14-dotnet-declarations.md)

## License

See LICENSE file for details.

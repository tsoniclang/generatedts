# Command-Line Interface Specification

## Command

```bash
tsbindgen generate [options]
```

## Assembly Input Options

### `--assembly`, `-a` (repeatable)
Path to a .NET assembly (`.dll`) to process.

**Usage**:
```bash
tsbindgen generate --assembly path/to/Assembly.dll
tsbindgen generate -a Assembly1.dll -a Assembly2.dll
```

### `--assembly-dir`, `-d`
Directory containing assemblies to process (non-recursive, `.dll` files only).

**Usage**:
```bash
tsbindgen generate --assembly-dir /path/to/assemblies
```

**Note**: Can be combined with `--assembly` to process both individual files and directories.

## Output Options

### `--out-dir`, `-o`
Output directory for generated files.

**Default**: `out/`

**Usage**:
```bash
tsbindgen generate --assembly Assembly.dll --out-dir ./types
```

## Filter Options

### `--namespaces`, `-n`
Comma-separated list of namespaces to include (whitelist filter).

**Usage**:
```bash
tsbindgen generate -a Assembly.dll --namespaces System.Linq,System.Collections
```

## Naming Transform Options

All naming transforms support `camelCase` (or `camel-case`, `camel`). If not specified, original CLR names are used.

### `--namespace-names`
Transform namespace names.

**Values**: `none` (default), `camelCase`

**Example**:
```bash
--namespace-names camelCase
# System.Linq → systemLinq
```

### `--class-names`
Transform class names.

**Values**: `none` (default), `camelCase`

**Example**:
```bash
--class-names camelCase
# Enumerable → enumerable
```

### `--interface-names`
Transform interface names.

**Values**: `none` (default), `camelCase`

### `--method-names`
Transform method names.

**Values**: `none` (default), `camelCase`

**Example**:
```bash
--method-names camelCase
# SelectMany → selectMany
```

### `--property-names`
Transform property names.

**Values**: `none` (default), `camelCase`

**Example**:
```bash
--property-names camelCase
# Length → length
```

### `--enum-member-names`
Transform enum member names.

**Values**: `none` (default), `camelCase`

**Example**:
```bash
--enum-member-names camelCase
# DayOfWeek.Monday → DayOfWeek.monday
```

**Note**: When any naming transform is active, a `*.bindings.json` file is generated containing CLR name → TypeScript name mappings.

## Logging & Debug Options

### `--verbose`, `-v`
Show detailed generation progress.

**Default**: `false`

**Usage**:
```bash
tsbindgen generate -a Assembly.dll --verbose
```

### `--logs`
Enable logging for specific categories (repeatable).

**Categories**: `ViewPlanner`, `PhaseGate`, `ShapePass`, etc.

**Usage**:
```bash
tsbindgen generate -a Assembly.dll --logs PhaseGate ViewPlanner
```

### `--debug-snapshot`
Write intermediate snapshots to disk for debugging.

**Default**: `false`

**Output**: `<out-dir>/assemblies/*.snapshot.json`, `<out-dir>/namespaces/*.snapshot.json`

**Usage**:
```bash
tsbindgen generate -a Assembly.dll --debug-snapshot
```

### `--debug-typelist`
Write TypeScript type lists for debugging/verification.

**Default**: `false`

**Output**: `<namespace>/typelist.json` for each namespace

**Usage**:
```bash
tsbindgen generate -a Assembly.dll --debug-typelist
```

## Pipeline Selection

### `--use-new-pipeline`
Use Single-Phase Architecture pipeline (experimental).

**Default**: `false` (uses two-phase pipeline)

**Usage**:
```bash
tsbindgen generate -a Assembly.dll --use-new-pipeline
```

**Note**: The Single-Phase pipeline is the current greenfield implementation. This flag will eventually become the default.

## External Package Resolution (Planned)

The following options are part of the upcoming external package resolution feature:

### `--ref-path` (repeatable)
Directory containing installed tsbindgen packages for dependency resolution.

**Planned usage**:
```bash
tsbindgen generate -a MyApp.dll \
  --ref-path ./node_modules \
  --ref-path ./vendor-libs \
  --out-dir ./types
```

### `--external-map`
JSON file mapping external types to module specifiers.

**Planned usage**:
```bash
tsbindgen generate -a MyApp.dll \
  --external-map external-map.json \
  --out-dir ./types
```

See [ref-path.md](ref-path.md) for complete specification.

## Examples

### Generate BCL Declarations (Bundle Mode)
```bash
tsbindgen generate \
  --assembly-dir /usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.0/ \
  --out-dir ./bcl-types \
  --use-new-pipeline
```

**Behavior**: Auto-detects BCL, loads transitive closure, emits all types.

### Generate User Assembly (Lean Mode, Planned)
```bash
tsbindgen generate \
  --assembly MyCompany.Feature.dll \
  --ref-path ./node_modules \
  --external-map external-map.json \
  --out-dir ./types \
  --use-new-pipeline
```

**Behavior**: Emits only `MyCompany.Feature.dll` types, imports BCL types from `@dotnet/bcl`.

### Generate with camelCase Transforms
```bash
tsbindgen generate \
  --assembly MyApp.dll \
  --method-names camelCase \
  --property-names camelCase \
  --out-dir ./types \
  --use-new-pipeline
```

**Output**: Methods and properties use camelCase, bindings file created.

### Generate with Verbose Logging
```bash
tsbindgen generate \
  --assembly Assembly.dll \
  --verbose \
  --logs PhaseGate ViewPlanner \
  --out-dir ./types \
  --use-new-pipeline
```

**Output**: Detailed progress + specific category logs.

### Generate with Debug Output
```bash
tsbindgen generate \
  --assembly Assembly.dll \
  --debug-snapshot \
  --debug-typelist \
  --out-dir ./types \
  --use-new-pipeline
```

**Output**: Includes intermediate snapshots and type lists for verification.

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Generation error (exception during processing) |
| 2 | No assemblies specified |
| 3 | Assembly directory not found |

## Environment Variables

None currently supported.

## Configuration Files

None currently supported. All configuration is via CLI options.

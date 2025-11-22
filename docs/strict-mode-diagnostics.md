# Strict Mode Diagnostic Policy

**Last Updated**: 2025-11-22
**Policy File**: `src/tsbindgen/Plan/Validation/StrictModePolicy.cs`

---

## Philosophy

Strict mode enforces **zero warnings** unless explicitly whitelisted with documented justification.

### Diagnostic Levels

| Level | Strict Mode Behavior | Description |
|-------|---------------------|-------------|
| **ERROR** | ❌ Always forbidden | Blocks emission - must be eliminated |
| **WARNING** | ⚠️ Forbidden unless whitelisted | Must be resolved or have documented exception |
| **INFO** | ✅ Always allowed | Informational only - doesn't count toward totals |

---

## Current Policy

### ❌ Forbidden (ERROR codes)

All ERROR codes are forbidden in strict mode - no exceptions.

| Code | Description | Status |
|------|-------------|--------|
| TBG900 | StaticFlatteningPlan invalid | ✅ Eliminated |
| TBG901 | StaticConflictPlan invalid | ✅ Eliminated |
| TBG902 | OverrideConflictPlan invalid | ✅ Eliminated |
| TBG903 | PropertyOverridePlan invalid | ✅ Eliminated |
| TBG904 | ExtensionMethodsPlan invalid | ✅ Eliminated |
| TBG905 | Extension method 'any' erasures | ✅ Eliminated |
| TBG906 | Extension bucket name invalid | ✅ Eliminated |
| TBG907 | Extension import unresolved | ✅ Eliminated |

---

### ⚠️ Whitelisted Warnings (Temporary Exceptions)

These warnings are **temporarily whitelisted** pending structural fixes in PRs B-D.

#### TBG120: Reserved Word Collisions (8 instances)

**Status**: ⏳ Whitelisted until PR D
**Count**: 8 types
**Justification**: Core BCL types (Enum, String, Type, Boolean, Void, Switch, Debugger, Module) collide with TypeScript reserved words but are always used in qualified contexts (e.g., `System.Type`).

**Whitelisted Because**:
- Fundamental BCL types with well-known names
- Sanitization would break compatibility
- Always emitted in qualified contexts
- TypeScript's module system isolates names

**Elimination Plan** (PR D):
- Add runtime guards to ensure qualification
- Convert to ERROR if unqualified usage detected
- Verify all emissions use qualified paths

**Affected Types**:
```
System.Enum
System.String
System.Type
System.Boolean
System.Void
Switch
Debugger
Module
```

---

#### TBG201: Circular Namespace Dependencies (~~267~~ 0 instances)

**Status**: ✅ **ELIMINATED in PR B**
**Count**: 0 (was 267)
**Resolution**: SCC bucketing filters intra-SCC cycles from validation

**How PR B Eliminated TBG201**:
1. Computed Strongly Connected Components (SCCs) in namespace dependency graph using Tarjan's algorithm
2. Found 63 SCCs: 2 multi-namespace (66 + 3 namespaces), 61 singletons
3. Filtered 267 intra-SCC circular dependencies from TBG201 warnings
4. Intra-SCC imports are valid (within same strongly connected component)

**SCC Structure** (BCL namespaces):
- **SCC 0 (66 namespaces)**: Main BCL cycle including System, Collections, IO, Reflection, etc.
- **SCC 34 (3 namespaces)**: Reflection.Metadata, Reflection.Metadata.Ecma335, Reflection.PortableExecutable
- **61 singleton SCCs**: Namespaces with no circular dependencies

**Technical Implementation**:
- `SCCCompute.cs`: Tarjan's algorithm for SCC detection
- `SCCPlan`: Maps namespaces to SCC buckets
- `ValidateImports`: Filters out intra-SCC cycles (only reports inter-SCC cycles, which should never occur)

---

#### TBG203: Interface Conformance Issues (~~87~~ 0 instances)

**Status**: ✅ **ELIMINATED in PR C**
**Count**: 0 (was 87)
**Resolution**: Honest emission filtering via HonestEmissionPlan

**How PR C Eliminated TBG203**:
1. **InterfaceConformanceAnalyzer** pre-analyzes all types before PhaseGate validation
2. **HonestEmissionPlanner** identifies interfaces that cannot be satisfied in TypeScript
3. **ClassPrinter** filters unsatisfiable interfaces from `implements` clause during emission
4. **MetadataEmitter** preserves full truth in metadata.json with `unsatisfiableInterfaces` field
5. **PhaseGate validation** suppresses TBG203 for types in HonestEmissionPlan

**Example - Honest Emission Result**:
```typescript
// TypeScript declaration (index.d.ts)
class Int32$instance implements
    IComparable,
    IEquatable_1<Int32>
    // IMinMaxValue_1<Int32> omitted - cannot satisfy (static abstract members)
{
    compareTo(value: Int32): int;
    equals(obj: any): boolean;
}
```

**Metadata (metadata.json)**:
```json
{
  "clrName": "System.Int32",
  "tsEmitName": "Int32",
  "unsatisfiableInterfaces": [
    {
      "interfaceClrName": "System.Numerics.IMinMaxValue`1",
      "reason": "MissingOrIncompatibleMembers",
      "issueCount": 2
    }
  ]
}
```

**Technical Implementation**:
- `InterfaceConformanceAnalyzer.cs`: Pre-validation conformance analysis
- `HonestEmissionPlanner.cs`: Plans which interfaces to omit
- `HonestEmissionPlan`: Tracks unsatisfiable interfaces by type
- `ClassPrinter.IsUnsatisfiableInterface()`: Filters during emission
- `MetadataEmitter`: Preserves truth in metadata

---

### ✅ Informational (INFO codes)

These codes are informational only and **never count toward warning totals**.

#### TBG310: Property Covariance (12 instances)

**Status**: ✅ Informational (allowed)
**Count**: 12 types
**Justification**: C# allows property covariance, TypeScript doesn't support property overloads. This is a fundamental TypeScript limitation.

**Why Informational**:
- Not actionable (TS language limitation)
- Properties still accessible
- PropertyOverridePlan unifies types where needed
- Documented in metadata

**Affected Types**:
```
System.Half (59 issues)
System.Runtime.InteropServices.NFloat (28 issues)
System.Numerics.BigInteger (4 issues)
System.Int128, System.UInt128
```

**Resolution**: Not required - working as designed

---

#### TBG410: Narrowed Generic Constraints (5 instances)

**Status**: ✅ Informational (allowed)
**Count**: 5 types
**Justification**: Derived types add generic constraints beyond base class. This is correct TypeScript behavior.

**Why Informational**:
- Valid TypeScript pattern
- Constraints correctly emitted
- Type safety preserved
- Documented in metadata

**Affected Types**:
```
System.Collections.Generic.GenericEqualityComparer<T>
System.Collections.Generic.NullableEqualityComparer<T>
System.Collections.Generic.EnumEqualityComparer<T>
System.Collections.Generic.GenericComparer<T>
System.Collections.Generic.NullableComparer<T>
```

**Resolution**: Not required - working correctly

---

## Roadmap to Zero Warnings

### Current State (Post PR C)

```
Errors:   0  ✅
Warnings: 8 ⚠️ (TBG120 only, whitelisted for PR D)
Info:     17  ✅
```

**Progress Summary**:
- ✅ **PR A**: Strict mode policy framework
- ✅ **PR B**: TBG201 eliminated (267 → 0) via SCC bucketing
- ✅ **PR C**: TBG203 eliminated (87 → 0) via honest emission
- ⏳ **PR D**: TBG120 elimination (8 → 0) via qualification validation

**Remaining Work** (PR D):
- Eliminate TBG120 (8 reserved word collisions)
- Verify all reserved words used in qualified contexts
- Convert TBG120 to ERROR if unqualified usage detected

**Final Target** (Post PR D):

```
Errors:   0  ✅
Warnings: 0  ✅ (strict mode zero tolerance)
Info:     ~20 ✅ (TBG310, TBG410, counters)
```

**Eliminated**: TBG120 (8 instances) via qualification guards
**Downgraded**: TBG310, TBG410 already INFO (no change needed)

---

## Strict Mode Enforcement

When `BuildContext.StrictMode == true`:

1. **Validation fails** if any WARNING exists that isn't whitelisted in `StrictModePolicy`
2. **INFO messages** are allowed and don't count toward totals
3. **ERROR messages** always fail validation (zero tolerance)

### Usage

```bash
# Enable strict mode (default in production builds)
dotnet run -- generate -a assembly.dll --strict

# Disable for development
dotnet run -- generate -a assembly.dll --no-strict
```

---

## Policy Maintenance

### Adding a New Diagnostic Code

1. Add to `StrictModePolicy.cs` with justification
2. Document in this file under appropriate section
3. If WARNING: provide elimination roadmap
4. If INFO: explain why it's informational only

### Removing a Whitelisted Warning

1. Implement structural fix (PR B/C/D pattern)
2. Verify warning count reaches zero
3. Remove from whitelist in `StrictModePolicy.cs`
4. Update this documentation

### Promoting WARNING to ERROR

If a whitelisted WARNING becomes critical:

1. Change `AllowedLevel.WhitelistedWarning` → `AllowedLevel.Forbidden` in policy
2. Update justification to explain why it's now forbidden
3. Ensure count is zero before merge

---

## References

- **Policy Implementation**: `src/tsbindgen/Plan/Validation/StrictModePolicy.cs`
- **Enforcement**: `src/tsbindgen/Plan/PhaseGate.cs` (strict mode validation)
- **Roadmap**: PRs B, C, D eliminate all whitelisted warnings

---

**Last Review**: 2025-11-22
**Next Review**: After each PR (B, C, D) completion

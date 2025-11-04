# Declaration Emission

Emitters convert analysed CLR types into the intermediate declaration model.
`DeclarationRenderer` then serialises that model into `.d.ts` text.

## Emitters

| File | Output | Notes |
| --- | --- | --- |
| `Emit/EnumEmitter.cs` | `EnumDeclaration` | Uses `GetFields` to fetch enum members; keeps CLR values. |
| `Emit/InterfaceEmitter.cs` | `InterfaceDeclaration` | Integrates diamond analysis and intersection aliases. |
| `Emit/ClassEmitter.cs` | `ClassDeclaration` | Handles constructors, instance/static members, explicit interface wrappers, and collects aliases. |
| `Emit/StaticNamespaceEmitter.cs` | `StaticNamespaceDeclaration` | For static-only types (abstract sealed). |
| `Emit/MethodEmitter.cs` | `TypeInfo.MethodInfo` + metadata | Converts reflection methods into the IR, skipping explicit interface impls. |
| `Emit/PropertyEmitter.cs` | `TypeInfo.PropertyInfo` + metadata | Applies covariance wrappers, hides redundant properties, enforces TS2302 rules. |
| `Emit/ConstructorEmitter.cs` | `TypeInfo.ConstructorInfo` | Simple parameter translation. |

Each emitter depends on the mapping layer (`TypeMapper.MapType`) and the analysis
helpers for variance and explicit interfaces.

## Declaration rendering

`Emit/DeclarationRenderer.RenderDeclarations` stages:

1. Header + intrinsics (`Emit/Writers/IntrinsicsWriter.cs`) – branded numeric
   types and the auto-generated comment.
2. Imports (`Emit/Writers/ImportWriter.cs`) – produces one `import type` per
   external assembly, using aliases from `DependencyTracker`.
3. Namespace/type bodies (`Emit/Writers/TypeWriter.cs`) – renders:
   - Classes (`Emit/Writers/MemberWriter.cs` handles methods/properties/constructors)
   - Interfaces, enums, static namespaces
   - Intersection aliases for diamonds

Formatting helpers (indentation, line breaks, generic parameter strings) live in
the writer modules so the emitter functions remain purely data-ish.

## Companion namespaces

Static members on static-only CLR types become TS namespaces containing exported
functions/properties.  For regular classes, static overloads are emitted on the
class body due to the analysis step (no companion namespace required).

## Output structure

The final `.d.ts` file is structured as:

1. Header comment + intrinsics
2. Imports (alphabetised by module alias)
3. Namespace blocks (alphabetised); each namespace contains the declarations and
   any `_Base`/alias types added during analysis

This consistently mirrors the CLR namespace layout, making the generated file
predictable for tooling and manual inspection.

using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Types;
using tsbindgen.SinglePhase.Plan;
using tsbindgen.SinglePhase.Renaming;

namespace tsbindgen.SinglePhase.Emit;

/// <summary>
/// Single source of truth for resolving TypeScript identifiers from TypeReferences.
/// Uses the Renamer to ensure imports and declarations use identical names.
/// TS2693 FIX: Also uses ImportPlan to qualify value-imported types with namespace alias.
/// TS2304 FIX (Facade): In facade mode, always qualifies cross-namespace types with namespace alias.
/// </summary>
public sealed class TypeNameResolver
{
    private readonly BuildContext _ctx;
    private readonly SymbolGraph _graph;
    private readonly ImportPlan? _importPlan;
    private readonly string? _currentNamespace;
    private readonly bool _facadeMode;

    public TypeNameResolver(BuildContext ctx, SymbolGraph graph, ImportPlan? importPlan = null, string? currentNamespace = null, bool facadeMode = false)
    {
        _ctx = ctx;
        _graph = graph;
        _importPlan = importPlan;
        _currentNamespace = currentNamespace;
        _facadeMode = facadeMode;
    }

    public bool IsFacadeMode => _facadeMode;

    /// <summary>
    /// Resolve the final TypeScript identifier for a TypeSymbol.
    /// This directly queries the Renamer - the single source of truth.
    /// </summary>
    public string For(Model.Symbols.TypeSymbol type)
    {
        return _ctx.Renamer.GetFinalTypeName(type);
    }

    /// <summary>
    /// Resolve the final TypeScript identifier for a NamedTypeReference.
    /// This is the ONLY way to get type names during emission - never use CLR names directly.
    /// </summary>
    public string ResolveTypeName(NamedTypeReference named)
    {
        return For(named);
    }

    /// <summary>
    /// Resolve the final TypeScript identifier for a NamedTypeReference.
    /// Wrapper for ResolveTypeName to provide consistent API.
    /// CRITICAL: Uses TypeMap to short-circuit built-in types BEFORE graph lookup.
    /// This prevents PG_LOAD_001 false positives for primitives.
    /// TS2693 FIX: Qualifies value-imported types with namespace alias.
    /// </summary>
    public string For(NamedTypeReference named)
    {
        // 1. Try TypeMap FIRST (short-circuit built-in types before graph lookup)
        if (TypeMap.TryMapBuiltin(named.FullName, out var builtinType))
        {
            return builtinType;
        }

        // 2. TS2693 FIX: Check if this type needs qualification with namespace alias
        //    This happens when the type is used as a value (base class/interface)
        if (_importPlan != null && _currentNamespace != null)
        {
            var clrFullName = named.FullName;
            if (_importPlan.ValueImportQualifiedNames.TryGetValue((_currentNamespace, clrFullName), out var qualifiedName))
            {
                // This type was imported as a value and needs qualification
                // Return qualified name: "System_Internal.Exception"
                return qualifiedName;
            }
        }

        // 3. Look up TypeSymbol in graph using StableId
        var stableId = $"{named.AssemblyName}:{named.FullName}";

        if (!_graph.TypeIndex.TryGetValue(stableId, out var typeSymbol))
        {
            _ctx.Log("TypeNameResolver", $"External type (not in graph): {stableId}");
            // Type not in graph - this is an EXTERNAL type from another assembly
            // Use the CLR name as-is (it will be in imports if needed)

            // Extract simple name from full name (e.g., "System.Collections.Generic.List`1" → "List`1")
            // IMPORTANT: Stop at first comma (assembly-qualified names have ", Version=...")
            var fullName = named.FullName;
            var commaIndex = fullName.IndexOf(',');
            if (commaIndex >= 0)
            {
                fullName = fullName.Substring(0, commaIndex).Trim();
            }

            // Extract namespace from full name (e.g., "System.Collections.Generic.List`1" → "System.Collections.Generic")
            var externalNamespace = fullName.Contains('.')
                ? fullName.Substring(0, fullName.LastIndexOf('.'))
                : "";

            var simpleName = fullName.Contains('.')
                ? fullName.Substring(fullName.LastIndexOf('.') + 1)
                : fullName;

            // Sanitize the name for TypeScript (handle generic arity, nested types, etc.)
            var sanitized = SanitizeClrName(simpleName);

            // CRITICAL: Check if sanitized name is a TypeScript reserved word
            // External types (not in current graph) still need reserved word handling
            // Example: System.Type referenced from another namespace → Type_
            var result = TypeScriptReservedWords.Sanitize(sanitized);
            var finalExternalName = result.Sanitized;

            // TS2304 FIX (Facade): Qualify external cross-namespace types in facade mode
            if (_facadeMode && _currentNamespace != null && externalNamespace != _currentNamespace && !string.IsNullOrEmpty(externalNamespace))
            {
                var namespaceAlias = GetNamespaceAlias(externalNamespace);
                return $"{namespaceAlias}.{finalExternalName}";
            }

            return finalExternalName;
        }

        // 4. Get final TypeScript name from Renamer (single source of truth)
        var finalName = _ctx.Renamer.GetFinalTypeName(typeSymbol);

        // 5. TS2304 FIX (Facade): In facade mode, qualify cross-namespace types with namespace alias
        //    This prevents "Cannot find name 'IEquatable_1'" errors in facade constraint clauses
        if (_facadeMode && _currentNamespace != null)
        {
            var targetNamespace = typeSymbol.Namespace;
            if (targetNamespace != _currentNamespace)
            {
                // Cross-namespace reference in facade - must qualify
                // Convert "System.Runtime.InteropServices" → "System_Runtime_InteropServices"
                var namespaceAlias = GetNamespaceAlias(targetNamespace);
                return $"{namespaceAlias}.{finalName}";
            }
        }

        return finalName;
    }

    /// <summary>
    /// Get the TypeScript import alias for a namespace.
    /// Converts "System.Collections.Generic" to "System_Collections_Generic".
    /// </summary>
    private static string GetNamespaceAlias(string namespaceName)
    {
        // Replace dots with underscores to make valid TS identifier
        return namespaceName.Replace('.', '_');
    }

    /// <summary>
    /// Sanitize CLR type name for TypeScript.
    /// Handles generic arity (`1 → _1) and special characters.
    /// </summary>
    private static string SanitizeClrName(string clrName)
    {
        // Replace generic arity backtick with underscore: List`1 → List_1
        var sanitized = clrName.Replace('`', '_');

        // Remove any remaining invalid TypeScript identifier characters
        sanitized = sanitized.Replace('+', '_'); // Nested type separator
        sanitized = sanitized.Replace('<', '_');
        sanitized = sanitized.Replace('>', '_');
        sanitized = sanitized.Replace('[', '_');
        sanitized = sanitized.Replace(']', '_');

        return sanitized;
    }

    /// <summary>
    /// Try to map a CLR primitive type to TypeScript built-in type.
    /// Returns null if not a primitive.
    /// </summary>
    public static string? TryMapPrimitive(string clrFullName)
    {
        TypeMap.TryMapBuiltin(clrFullName, out var tsType);
        return tsType;
    }

    /// <summary>
    /// Check if a type is a primitive that doesn't need imports.
    /// </summary>
    public static bool IsPrimitive(string clrFullName)
    {
        return TypeMap.TryMapBuiltin(clrFullName, out _);
    }
}

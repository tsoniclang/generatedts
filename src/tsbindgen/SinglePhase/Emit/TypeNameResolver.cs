using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Types;

namespace tsbindgen.SinglePhase.Emit;

/// <summary>
/// Single source of truth for resolving TypeScript identifiers from TypeReferences.
/// Uses the Renamer to ensure imports and declarations use identical names.
/// </summary>
public sealed class TypeNameResolver
{
    private readonly BuildContext _ctx;
    private readonly SymbolGraph _graph;

    public TypeNameResolver(BuildContext ctx, SymbolGraph graph)
    {
        _ctx = ctx;
        _graph = graph;
    }

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
    /// </summary>
    public string For(NamedTypeReference named)
    {
        // 1. Check if this is a primitive type (short-circuit)
        var primitiveType = TryMapPrimitive(named.FullName);
        if (primitiveType != null)
        {
            return primitiveType;
        }

        // 2. Look up TypeSymbol in graph using StableId
        var stableId = $"{named.AssemblyName}:{named.FullName}";

        if (!_graph.TypeIndex.TryGetValue(stableId, out var typeSymbol))
        {
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

            var simpleName = fullName.Contains('.')
                ? fullName.Substring(fullName.LastIndexOf('.') + 1)
                : fullName;

            // Sanitize the name for TypeScript (handle generic arity, nested types, etc.)
            return SanitizeClrName(simpleName);
        }

        // 3. Get final TypeScript name from Renamer (single source of truth)
        var finalName = _ctx.Renamer.GetFinalTypeName(typeSymbol);

        return finalName;
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
        return clrFullName switch
        {
            // Boolean
            "System.Boolean" => "boolean",

            // Numeric types → branded types (defined in each namespace)
            "System.SByte" => "sbyte",
            "System.Byte" => "byte",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Single" => "float",
            "System.Double" => "double",
            "System.Decimal" => "decimal",
            "System.IntPtr" => "nint",
            "System.UIntPtr" => "nuint",

            // String
            "System.String" => "string",

            // Void
            "System.Void" => "void",

            // Object
            "System.Object" => "any",

            _ => null
        };
    }

    /// <summary>
    /// Check if a type is a primitive that doesn't need imports.
    /// </summary>
    public static bool IsPrimitive(string clrFullName)
    {
        return TryMapPrimitive(clrFullName) != null;
    }
}

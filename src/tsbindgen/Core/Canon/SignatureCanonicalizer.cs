using System.Text;

namespace tsbindgen.Core.Canon;

/// <summary>
/// Creates stable, collision-free canonical signatures for methods and properties.
/// Used for:
/// - Overload deduplication
/// - Bindings/metadata correlation
/// - Interface surface matching
/// </summary>
public static class SignatureCanonicalizer
{
    /// <summary>
    /// Create a canonical signature for a method.
    /// Format: "MethodName(param1Type,param2Type,...):ReturnType"
    /// </summary>
    public static string CanonicalizeMethod(
        string methodName,
        IReadOnlyList<string> parameterTypes,
        string returnType)
    {
        var sb = new StringBuilder();
        sb.Append(methodName);
        sb.Append('(');

        for (int i = 0; i < parameterTypes.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(NormalizeTypeName(parameterTypes[i]));
        }

        sb.Append(')');
        sb.Append(':');
        sb.Append(NormalizeTypeName(returnType));

        return sb.ToString();
    }

    /// <summary>
    /// Create a canonical signature for a property.
    /// Format: "PropertyName[param1Type,param2Type,...]:PropertyType"
    /// For non-indexer properties, parameters are empty.
    /// </summary>
    public static string CanonicalizeProperty(
        string propertyName,
        IReadOnlyList<string> indexParameterTypes,
        string propertyType)
    {
        var sb = new StringBuilder();
        sb.Append(propertyName);

        if (indexParameterTypes.Count > 0)
        {
            sb.Append('[');
            for (int i = 0; i < indexParameterTypes.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(NormalizeTypeName(indexParameterTypes[i]));
            }
            sb.Append(']');
        }

        sb.Append(':');
        sb.Append(NormalizeTypeName(propertyType));

        return sb.ToString();
    }

    /// <summary>
    /// Create a canonical signature for a field.
    /// Format: "FieldName:FieldType"
    /// </summary>
    public static string CanonicalizeField(string fieldName, string fieldType)
    {
        return $"{fieldName}:{NormalizeTypeName(fieldType)}";
    }

    /// <summary>
    /// Create a canonical signature for an event.
    /// Format: "EventName:DelegateType"
    /// </summary>
    public static string CanonicalizeEvent(string eventName, string delegateType)
    {
        return $"{eventName}:{NormalizeTypeName(delegateType)}";
    }

    /// <summary>
    /// Normalize a type name for signature matching.
    /// Handles generic arity, nested types, etc.
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        // Remove whitespace
        var normalized = typeName.Replace(" ", "");

        // Normalize generic backtick to underscore (List`1 -> List_1)
        normalized = normalized.Replace('`', '_');

        // TODO: More sophisticated normalization for:
        // - Array types (Int32[] -> Int32[])
        // - Nullable types (Int32? -> Nullable<Int32>)
        // - ByRef types (Int32& -> ByRef<Int32>)
        // - Pointer types (Int32* -> Pointer<Int32>)

        return normalized;
    }

    /// <summary>
    /// Extract method signature from a canonical signature.
    /// Useful for debugging and diagnostics.
    /// </summary>
    public static (string name, string[] parameters, string returnType) ParseMethodSignature(
        string canonicalSignature)
    {
        var parenIndex = canonicalSignature.IndexOf('(');
        var closeParenIndex = canonicalSignature.IndexOf(')');
        var colonIndex = canonicalSignature.IndexOf(':', closeParenIndex);

        var name = canonicalSignature[..parenIndex];
        var paramsStr = canonicalSignature[(parenIndex + 1)..closeParenIndex];
        var returnType = canonicalSignature[(colonIndex + 1)..];

        var parameters = string.IsNullOrEmpty(paramsStr)
            ? Array.Empty<string>()
            : paramsStr.Split(',');

        return (name, parameters, returnType);
    }
}

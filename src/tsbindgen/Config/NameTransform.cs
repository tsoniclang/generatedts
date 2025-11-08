namespace tsbindgen.Config;

/// <summary>
/// Utility for applying name transformations to CLR identifiers.
/// </summary>
public static class NameTransform
{
    /// <summary>
    /// TypeScript/JavaScript reserved keywords and special identifiers.
    /// </summary>
    private static readonly HashSet<string> TypeScriptReservedKeywords = new(StringComparer.Ordinal)
    {
        // Keywords
        "break", "case", "catch", "class", "const", "continue", "debugger",
        "default", "delete", "do", "else", "enum", "export", "extends",
        "false", "finally", "for", "function", "if", "import", "in",
        "instanceof", "new", "null", "return", "super", "switch", "this",
        "throw", "true", "try", "typeof", "var", "void", "while", "with",

        // Strict / future reserved
        "implements", "interface", "let", "package", "private", "protected",
        "public", "static", "yield", "async", "await",

        // Problematic identifiers
        "arguments", "eval", "constructor"
    };

    /// <summary>
    /// Apply the specified transformation to a CLR identifier.
    /// Also escapes JavaScript reserved words using $$name$$ format.
    /// </summary>
    public static string Apply(string identifier, NameTransformOption option)
    {
        var transformed = option switch
        {
            NameTransformOption.None => identifier,
            NameTransformOption.CamelCase => ToCamelCase(identifier),
            _ => identifier
        };

        // Escape reserved words with $$name$$ format
        return TypeScriptReservedKeywords.Contains(transformed)
            ? $"$${transformed}$$"
            : transformed;
    }

    /// <summary>
    /// Convert a PascalCase identifier to camelCase.
    /// Examples:
    ///   "SelectMany" → "selectMany"
    ///   "XMLParser" → "xmlParser"
    ///   "Count" → "count"
    ///   "A" → "a"
    /// </summary>
    private static string ToCamelCase(string identifier)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            return identifier;
        }

        // Already camelCase
        if (char.IsLower(identifier[0]))
        {
            return identifier;
        }

        // Single character
        if (identifier.Length == 1)
        {
            return char.ToLowerInvariant(identifier[0]).ToString();
        }

        // Handle leading acronyms: "XMLParser" → "xmlParser"
        var leadingUpper = 0;
        for (int i = 0; i < identifier.Length && char.IsUpper(identifier[i]); i++)
        {
            leadingUpper++;
        }

        // All uppercase: "XML" → "xml"
        if (leadingUpper == identifier.Length)
        {
            return identifier.ToLowerInvariant();
        }

        // Leading acronym followed by lowercase: "XMLParser" → "xmlParser"
        if (leadingUpper > 1)
        {
            // Keep all but last uppercase letter lowercase
            return identifier.Substring(0, leadingUpper - 1).ToLowerInvariant() +
                   identifier.Substring(leadingUpper - 1);
        }

        // Standard PascalCase: "SelectMany" → "selectMany"
        return char.ToLowerInvariant(identifier[0]) + identifier.Substring(1);
    }
}

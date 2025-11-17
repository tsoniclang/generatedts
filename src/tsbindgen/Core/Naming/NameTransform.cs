using System.Text;
using tsbindgen.Core.Policy;

namespace tsbindgen.Core.Naming;

/// <summary>
/// Applies name transformations (camelCase, PascalCase, etc.) to identifiers.
/// </summary>
public static class NameTransform
{
    /// <summary>
    /// Apply the configured transformation strategy to a name.
    /// </summary>
    public static string Apply(string name, NameTransformStrategy strategy)
    {
        return strategy switch
        {
            NameTransformStrategy.None => name,
            NameTransformStrategy.CamelCase => ToCamelCase(name),
            NameTransformStrategy.PascalCase => ToPascalCase(name),
            _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null)
        };
    }

    /// <summary>
    /// Convert name to camelCase.
    /// Examples: "GetValue" -> "getValue", "URL" -> "url", "HTTPSConnection" -> "httpsConnection"
    /// </summary>
    public static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        // Handle special cases
        if (name.Length == 1)
            return char.ToLowerInvariant(name[0]).ToString();

        // Check if it's all uppercase (acronym)
        if (IsAllUpperCase(name))
            return name.ToLowerInvariant();

        // Find the first lowercase letter or end of consecutive uppercase letters
        var sb = new StringBuilder(name.Length);
        var i = 0;

        // Lowercase consecutive uppercase letters at the start
        while (i < name.Length && char.IsUpper(name[i]))
        {
            // If this is the last character, or the next is lowercase, keep this one uppercase
            // (except if it's the first character)
            if (i > 0 && (i == name.Length - 1 || (i < name.Length - 1 && char.IsLower(name[i + 1]))))
            {
                break;
            }

            sb.Append(char.ToLowerInvariant(name[i]));
            i++;
        }

        // Append the rest
        sb.Append(name[i..]);

        return sb.ToString();
    }

    /// <summary>
    /// Convert name to PascalCase.
    /// Examples: "getValue" -> "GetValue", "url" -> "Url"
    /// </summary>
    public static string ToPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        if (name.Length == 1)
            return char.ToUpperInvariant(name[0]).ToString();

        return char.ToUpperInvariant(name[0]) + name[1..];
    }

    private static bool IsAllUpperCase(string name)
    {
        foreach (var ch in name)
        {
            if (char.IsLetter(ch) && !char.IsUpper(ch))
                return false;
        }
        return true;
    }
}

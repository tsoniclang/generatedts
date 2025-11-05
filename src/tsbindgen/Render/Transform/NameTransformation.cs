using tsbindgen.Config;

namespace tsbindgen.Render.Transform;

/// <summary>
/// Applies name transformations (e.g., camelCase) to identifiers.
/// </summary>
public static class NameTransformation
{
    public static string Apply(string name, NameTransformOption option)
    {
        return option switch
        {
            NameTransformOption.CamelCase => ToCamelCase(name),
            NameTransformOption.None => name,
            _ => name
        };
    }

    private static string ToCamelCase(string name)
    {
        if (string.IsNullOrEmpty(name) || char.IsLower(name[0]))
            return name;

        var chars = name.ToCharArray();
        chars[0] = char.ToLowerInvariant(chars[0]);
        return new string(chars);
    }
}

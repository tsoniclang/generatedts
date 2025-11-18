namespace tsbindgen.Plan;

/// <summary>
/// Plans module specifiers for TypeScript imports.
/// Generates relative paths based on source/target namespaces and emission area.
/// Handles root namespace (_root) and nested namespace directories.
/// </summary>
public static class PathPlanner
{
    /// <summary>
    /// Gets the module specifier for importing from targetNamespace into sourceNamespace.
    /// Returns a relative path string suitable for TypeScript import statements.
    /// </summary>
    /// <param name="sourceNamespace">The namespace doing the importing (empty string for root)</param>
    /// <param name="targetNamespace">The namespace being imported from (empty string for root)</param>
    /// <returns>Relative module path including .js extension (e.g., "../../System/internal/index.js")</returns>
    public static string GetSpecifier(string sourceNamespace, string targetNamespace)
    {
        var isSourceRoot = string.IsNullOrEmpty(sourceNamespace);
        var isTargetRoot = string.IsNullOrEmpty(targetNamespace);

        // All imports target internal/index.js (or _root/index.js for root namespace)
        // Calculate the relative path from the source namespace's internal file location
        return (isSourceRoot, isTargetRoot) switch
        {
            // _root/index.d.ts → ../{target}/internal/index.js
            (true, false) => $"../{targetNamespace}/internal/index.js",

            // _root/index.d.ts → ./index.js (self) - not normally used, but keep consistent
            (true, true) => "./index.js",

            // {Namespace}/internal/index.d.ts → ../../_root/index.js
            (false, true) => "../../_root/index.js",

            // {Namespace}/internal/index.d.ts → ../../{target}/internal/index.js
            (false, false) => $"../../{targetNamespace}/internal/index.js"
        };
    }

    /// <summary>
    /// Gets the directory name for a namespace (handles root namespace).
    /// </summary>
    public static string GetNamespaceDirectory(string namespaceName)
    {
        return string.IsNullOrEmpty(namespaceName) ? "_root" : namespaceName;
    }

    /// <summary>
    /// Gets the subdirectory name for internal declarations (handles root namespace).
    /// </summary>
    public static string GetInternalSubdirectory(string namespaceName)
    {
        return string.IsNullOrEmpty(namespaceName) ? "_root" : "internal";
    }
}

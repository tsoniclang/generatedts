using System.Collections.Immutable;
using System.Text.Json;

namespace tsbindgen.Library;

/// <summary>
/// Loads a LibraryContract from an existing tsbindgen package directory.
/// Reads all metadata.json files to extract type and member StableIds.
/// Reads bindings.json to extract binding StableIds.
/// </summary>
public static class LibraryContractLoader
{
    /// <summary>
    /// Load library contract from a package directory.
    /// </summary>
    /// <param name="packagePath">Path to existing tsbindgen package directory</param>
    /// <returns>Loaded contract</returns>
    /// <exception cref="DirectoryNotFoundException">Package directory not found</exception>
    /// <exception cref="FileNotFoundException">No metadata files or bindings.json found</exception>
    /// <exception cref="InvalidOperationException">Malformed JSON or missing required fields</exception>
    public static LibraryContract Load(string packagePath)
    {
        if (!Directory.Exists(packagePath))
        {
            throw new DirectoryNotFoundException($"Library package directory not found: {packagePath}");
        }

        var allowedTypes = new HashSet<string>();
        var allowedMembers = new HashSet<string>();
        var namespaceToTypes = new Dictionary<string, HashSet<string>>();

        // Find all metadata.json files
        var metadataFiles = Directory.GetFiles(packagePath, "metadata.json", SearchOption.AllDirectories);

        if (metadataFiles.Length == 0)
        {
            throw new FileNotFoundException($"No metadata.json files found in library package: {packagePath}");
        }

        // Parse each metadata file
        foreach (var metadataFile in metadataFiles)
        {
            ProcessMetadataFile(metadataFile, allowedTypes, allowedMembers, namespaceToTypes);
        }

        // Load all bindings.json files from namespace subdirectories
        var bindingsFiles = Directory.GetFiles(packagePath, "bindings.json", SearchOption.AllDirectories);

        if (bindingsFiles.Length == 0)
        {
            throw new FileNotFoundException($"No bindings.json files found in library package: {packagePath}");
        }

        var allowedBindings = new HashSet<string>();
        foreach (var bindingsFile in bindingsFiles)
        {
            ProcessBindingsFile(bindingsFile, allowedBindings);
        }

        return new LibraryContract
        {
            AllowedTypeStableIds = allowedTypes.ToImmutableHashSet(),
            AllowedMemberStableIds = allowedMembers.ToImmutableHashSet(),
            AllowedBindingStableIds = allowedBindings.ToImmutableHashSet(),
            NamespaceToTypes = namespaceToTypes.ToImmutableDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value.ToImmutableHashSet())
        };
    }

    private static void ProcessMetadataFile(
        string filePath,
        HashSet<string> allowedTypes,
        HashSet<string> allowedMembers,
        Dictionary<string, HashSet<string>> namespaceToTypes)
    {
        var json = File.ReadAllText(filePath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Get namespace name
        if (!root.TryGetProperty("namespace", out var nsElement))
        {
            throw new InvalidOperationException($"Missing 'namespace' field in metadata file: {filePath}");
        }
        var namespaceName = nsElement.GetString() ?? throw new InvalidOperationException($"Null namespace in {filePath}");

        // Get types array
        if (!root.TryGetProperty("types", out var typesElement) || typesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"Missing or invalid 'types' array in metadata file: {filePath}");
        }

        var namespaceTypes = new HashSet<string>();

        // Process each type
        foreach (var typeElement in typesElement.EnumerateArray())
        {
            // Extract type StableId
            if (!typeElement.TryGetProperty("stableId", out var stableIdElement))
            {
                throw new InvalidOperationException($"Missing 'stableId' field for type in metadata file: {filePath}");
            }
            var typeStableId = stableIdElement.GetString() ?? throw new InvalidOperationException($"Null stableId in {filePath}");

            allowedTypes.Add(typeStableId);
            namespaceTypes.Add(typeStableId);

            // Extract member StableIds from all member arrays
            ProcessMemberArray(typeElement, "methods", allowedMembers, filePath);
            ProcessMemberArray(typeElement, "properties", allowedMembers, filePath);
            ProcessMemberArray(typeElement, "fields", allowedMembers, filePath);
            ProcessMemberArray(typeElement, "events", allowedMembers, filePath);
        }

        namespaceToTypes[namespaceName] = namespaceTypes;
    }

    private static void ProcessMemberArray(JsonElement typeElement, string memberArrayName, HashSet<string> allowedMembers, string filePath)
    {
        if (!typeElement.TryGetProperty(memberArrayName, out var memberArray) || memberArray.ValueKind != JsonValueKind.Array)
        {
            return; // Member array may be missing or empty - that's okay
        }

        foreach (var member in memberArray.EnumerateArray())
        {
            if (!member.TryGetProperty("stableId", out var stableIdElement))
            {
                // Skip members without StableId (e.g., constructors which might not have it)
                continue;
            }

            var memberStableId = stableIdElement.GetString();
            if (memberStableId != null)
            {
                allowedMembers.Add(memberStableId);
            }
        }
    }

    private static void ProcessBindingsFile(string bindingsPath, HashSet<string> bindings)
    {
        var json = File.ReadAllText(bindingsPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // bindings.json structure: object with StableId keys
        // We extract all keys
        foreach (var property in root.EnumerateObject())
        {
            bindings.Add(property.Name);
        }
    }
}

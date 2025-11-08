using System.Text.Json;

namespace tsbindgen.Snapshot;

/// <summary>
/// Pure I/O functions for reading/writing snapshot files.
/// </summary>
public static class SnapshotIO
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Writes an assembly snapshot to a JSON file.
    /// </summary>
    public static async Task WriteAssemblySnapshot(AssemblySnapshot snapshot, string outputPath)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json);
    }

    /// <summary>
    /// Writes a namespace snapshot to a JSON file.
    /// </summary>
    public static async Task WriteNamespaceSnapshot(NamespaceSnapshot snapshot, string outputPath)
    {
        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(outputPath, json);
    }

    /// <summary>
    /// Reads an assembly snapshot from a JSON file.
    /// </summary>
    public static async Task<AssemblySnapshot> ReadAssemblySnapshot(string inputPath)
    {
        var json = await File.ReadAllTextAsync(inputPath);
        return JsonSerializer.Deserialize<AssemblySnapshot>(json, JsonOptions)
            ?? throw new InvalidDataException($"Failed to deserialize assembly snapshot from {inputPath}");
    }

    /// <summary>
    /// Reads a namespace snapshot from a JSON file.
    /// </summary>
    public static async Task<NamespaceSnapshot> ReadNamespaceSnapshot(string inputPath)
    {
        var json = await File.ReadAllTextAsync(inputPath);
        return JsonSerializer.Deserialize<NamespaceSnapshot>(json, JsonOptions)
            ?? throw new InvalidDataException($"Failed to deserialize namespace snapshot from {inputPath}");
    }
}

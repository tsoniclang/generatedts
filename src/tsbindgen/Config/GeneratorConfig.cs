using System.Text.Json;
using System.Text.Json.Serialization;

namespace tsbindgen.Config;

public sealed record GeneratorConfig
{
    [JsonPropertyName("skipNamespaces")]
    public List<string> SkipNamespaces { get; init; } = new();

    [JsonPropertyName("typeRenames")]
    public Dictionary<string, string> TypeRenames { get; init; } = new();

    [JsonPropertyName("skipMembers")]
    public List<string> SkipMembers { get; init; } = new();

    // Naming transform options (CLI-only, not from config file)
    [JsonIgnore]
    public NameTransformOption NamespaceNames { get; init; } = NameTransformOption.None;

    [JsonIgnore]
    public NameTransformOption ClassNames { get; init; } = NameTransformOption.None;

    [JsonIgnore]
    public NameTransformOption InterfaceNames { get; init; } = NameTransformOption.None;

    [JsonIgnore]
    public NameTransformOption MethodNames { get; init; } = NameTransformOption.None;

    [JsonIgnore]
    public NameTransformOption PropertyNames { get; init; } = NameTransformOption.None;

    [JsonIgnore]
    public NameTransformOption EnumMemberNames { get; init; } = NameTransformOption.None;

    [JsonIgnore]
    public NameTransformOption BindingNames { get; init; } = NameTransformOption.None;
}

public static class GeneratorConfigIO
{
    public static async Task<GeneratorConfig> LoadAsync(string path)
    {
        var json = await File.ReadAllTextAsync(path);
        var config = JsonSerializer.Deserialize<GeneratorConfig>(json);
        return config ?? new GeneratorConfig();
    }
}

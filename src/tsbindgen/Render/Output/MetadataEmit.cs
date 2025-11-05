using System.Text.Json;
using tsbindgen.Render;

namespace tsbindgen.Render.Output;

/// <summary>
/// Emits metadata.json files containing CLR metadata.
/// TODO: Implement proper metadata schema
/// </summary>
public static class MetadataEmit
{
    public static string Emit(NamespaceModel model)
    {
        var metadata = new
        {
            namespace_ = model.ClrName,
            types = model.Types.Select(t => new
            {
                name = t.ClrName,
                kind = t.Kind.ToString(),
                isStatic = t.IsStatic
            })
        };

        return JsonSerializer.Serialize(metadata, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }
}

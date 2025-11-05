using System.Text.Json;
using tsbindgen.Render;

namespace tsbindgen.Render.Output;

/// <summary>
/// Emits bindings.json files mapping TS names to CLR names.
/// Only generated if any names differ.
/// </summary>
public static class BindingEmit
{
    public static string? Emit(NamespaceModel model)
    {
        var hasBindings = model.ClrName != model.TsAlias ||
                         model.Types.Any(t => t.ClrName != t.TsAlias || HasMemberBindings(t));

        if (!hasBindings)
            return null;

        var bindings = new
        {
            namespace_ = new
            {
                name = model.ClrName,
                alias = model.TsAlias
            },
            types = model.Types
                .Where(t => t.ClrName != t.TsAlias || HasMemberBindings(t))
                .Select(t => new
                {
                    name = t.ClrName,
                    alias = t.TsAlias
                })
        };

        return JsonSerializer.Serialize(bindings, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    private static bool HasMemberBindings(TypeModel type)
    {
        return type.Members.Methods.Any(m => m.ClrName != m.TsAlias) ||
               type.Members.Properties.Any(p => p.ClrName != p.TsAlias) ||
               type.Members.Fields.Any(f => f.ClrName != f.TsAlias) ||
               type.Members.Events.Any(e => e.ClrName != e.TsAlias);
    }
}

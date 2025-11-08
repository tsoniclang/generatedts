using tsbindgen.Render;

namespace tsbindgen.Render.Output;

/// <summary>
/// Emits index.js stub files for ESM compatibility.
/// </summary>
public static class ModuleStubEmit
{
    public static string Emit(NamespaceModel model)
    {
        return $"// Module stub for {model.ClrName}\nexport * from './index.js';\n";
    }
}

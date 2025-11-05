using tsbindgen.Render;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Removes interfaces from Implements when members are explicit-only.
/// TODO: Full implementation
/// </summary>
public static class ExplicitInterfaceReview
{
    public static NamespaceModel Apply(NamespaceModel model)
    {
        // Stub: No changes for now
        return model;
    }
}

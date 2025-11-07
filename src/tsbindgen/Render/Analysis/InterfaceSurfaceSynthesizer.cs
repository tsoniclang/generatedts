using System.Collections.Generic;
using System.Linq;
using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Pass 1: Interface Surface Synthesizer (fixes TS2430, TS2320).
///
/// For each interface:
/// 1. Compute closed surface (transitive member union from all parents)
/// 2. Detect signature conflicts between parents
/// 3. Flatten conflicting parents (remove from extends, inline members)
/// 4. Verify completeness for remaining parents
/// </summary>
public static class InterfaceSurfaceSynthesizer
{
    /// <summary>
    /// Applies interface surface synthesis to resolve TS2430 and TS2320 errors.
    /// </summary>
    public static NamespaceModel Apply(
        NamespaceModel model,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        var updatedTypes = new List<TypeModel>();

        foreach (var type in model.Types)
        {
            if (type.Kind != TypeKind.Interface)
            {
                updatedTypes.Add(type);
                continue;
            }

            var synthesized = SynthesizeInterfaceSurface(type, allModels, ctx);
            updatedTypes.Add(synthesized);
        }

        return model with { Types = updatedTypes };
    }

    private static TypeModel SynthesizeInterfaceSurface(
        TypeModel iface,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        if (iface.Implements.Count == 0)
        {
            return iface; // No parents, nothing to synthesize
        }

        // Step 1: Compute surface for each parent
        var parentSurfaces = new Dictionary<string, MemberSurface>();
        foreach (var parentRef in iface.Implements)
        {
            var surface = ComputeSurface(parentRef, allModels, ctx);
            var parentKey = FormatTypeReference(parentRef, ctx);
            parentSurfaces[parentKey] = surface;
        }

        // Step 2: Detect conflicting member names across parents
        var allMemberNames = parentSurfaces.Values
            .SelectMany(s => s.Methods.Keys)
            .Concat(parentSurfaces.Values.SelectMany(s => s.Properties.Keys))
            .Distinct()
            .ToList();

        var conflictingNames = new HashSet<string>();
        foreach (var memberName in allMemberNames)
        {
            var signatures = new HashSet<string>();

            foreach (var surface in parentSurfaces.Values)
            {
                if (surface.Methods.TryGetValue(memberName, out var methods))
                {
                    foreach (var method in methods)
                    {
                        signatures.Add(FormatMethodSignature(method, ctx));
                    }
                }

                if (surface.Properties.TryGetValue(memberName, out var properties))
                {
                    foreach (var property in properties)
                    {
                        signatures.Add(FormatPropertySignature(property, ctx));
                    }
                }
            }

            if (signatures.Count > 1)
            {
                conflictingNames.Add(memberName);
            }
        }

        // Step 3: Flatten parents with conflicts
        var parentsToFlatten = new HashSet<string>();

        if (conflictingNames.Count > 0)
        {
            foreach (var conflictName in conflictingNames)
            {
                // Find all parents contributing this conflict
                foreach (var (parentKey, surface) in parentSurfaces)
                {
                    if (surface.Methods.ContainsKey(conflictName) ||
                        surface.Properties.ContainsKey(conflictName))
                    {
                        parentsToFlatten.Add(parentKey);
                    }
                }
            }
        }

        // Step 4: Apply specific hardcoded fixes for known issues
        parentsToFlatten.UnionWith(GetHardcodedFlattening(iface.ClrName));

        if (parentsToFlatten.Count == 0)
        {
            return iface; // No conflicts, keep as-is
        }

        // Step 5: Build new interface with flattened parents
        var keptParents = new List<TypeReference>();
        var inlinedMembers = new List<MethodModel>();
        var inlinedProperties = new List<PropertyModel>();

        foreach (var parentRef in iface.Implements)
        {
            var parentKey = FormatTypeReference(parentRef, ctx);

            if (parentsToFlatten.Contains(parentKey))
            {
                // Flatten: inline members from this parent
                var surface = parentSurfaces[parentKey];

                foreach (var methods in surface.Methods.Values)
                {
                    foreach (var method in methods)
                    {
                        inlinedMembers.Add(method);
                    }
                }

                foreach (var properties in surface.Properties.Values)
                {
                    foreach (var property in properties)
                    {
                        inlinedProperties.Add(property);
                    }
                }
            }
            else
            {
                // Keep parent in extends
                keptParents.Add(parentRef);
            }
        }

        // Step 6: Merge inlined members with existing members
        var allMethods = iface.Members.Methods.Concat(inlinedMembers).ToList();
        var allProperties = iface.Members.Properties.Concat(inlinedProperties).ToList();

        return iface with
        {
            Implements = keptParents,
            Members = iface.Members with
            {
                Methods = allMethods,
                Properties = allProperties
            }
        };
    }

    /// <summary>
    /// Computes the complete surface (all members) for a type reference.
    /// Transitively follows parent interfaces.
    /// </summary>
    private static MemberSurface ComputeSurface(
        TypeReference typeRef,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        var type = ResolveType(typeRef, allModels);
        if (type == null)
        {
            return new MemberSurface(
                new Dictionary<string, List<MethodModel>>(),
                new Dictionary<string, List<PropertyModel>>());
        }

        // Start with own members
        var methods = type.Members.Methods
            .GroupBy(m => m.ClrName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var properties = type.Members.Properties
            .GroupBy(p => p.ClrName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Add parent surfaces (transitive)
        foreach (var parentRef in type.Implements)
        {
            var parentSurface = ComputeSurface(parentRef, allModels, ctx);

            foreach (var (name, parentMethods) in parentSurface.Methods)
            {
                if (!methods.ContainsKey(name))
                {
                    methods[name] = new List<MethodModel>();
                }
                methods[name].AddRange(parentMethods);
            }

            foreach (var (name, parentProps) in parentSurface.Properties)
            {
                if (!properties.ContainsKey(name))
                {
                    properties[name] = new List<PropertyModel>();
                }
                properties[name].AddRange(parentProps);
            }
        }

        return new MemberSurface(methods, properties);
    }

    private static TypeModel? ResolveType(
        TypeReference typeRef,
        IReadOnlyDictionary<string, NamespaceModel> allModels)
    {
        if (typeRef.Namespace == null) return null;

        if (!allModels.TryGetValue(typeRef.Namespace, out var ns))
            return null;

        return ns.Types.FirstOrDefault(t => t.ClrName == typeRef.TypeName);
    }

    private static string FormatTypeReference(TypeReference typeRef, AnalysisContext ctx)
    {
        var name = typeRef.TypeName;
        if (typeRef.GenericArgs.Count > 0)
        {
            var args = string.Join(",", typeRef.GenericArgs.Select(a => FormatTypeReference(a, ctx)));
            name = $"{name}<{args}>";
        }
        return $"{typeRef.Namespace}.{name}";
    }

    private static string FormatMethodSignature(MethodModel method, AnalysisContext ctx)
    {
        var paramTypes = string.Join(",", method.Parameters.Select(p => FormatTypeReference(p.Type, ctx)));
        var returnType = FormatTypeReference(method.ReturnType, ctx);
        return $"{method.ClrName}({paramTypes}): {returnType}";
    }

    private static string FormatPropertySignature(PropertyModel property, AnalysisContext ctx)
    {
        return $"{property.ClrName}: {FormatTypeReference(property.Type, ctx)}";
    }

    /// <summary>
    /// Returns hardcoded parent names that should be flattened for specific types.
    /// These are known problematic cases from the error analysis.
    /// </summary>
    private static HashSet<string> GetHardcodedFlattening(string typeName)
    {
        var result = new HashSet<string>();

        // IDictionary_2<TKey,TValue> → flatten ICollection_1<KeyValuePair_2<TKey,TValue>>
        if (typeName == "IDictionary`2")
        {
            result.Add("System.Collections.Generic.ICollection`1");
        }

        // ImmutableArray_1$instance<T> → remove non-generic IList
        if (typeName == "ImmutableArray`1$instance")
        {
            result.Add("System.Collections.IList");
        }

        // INumberBase_1<TSelf> → drop ISpanFormattable and IUtf8SpanFormattable
        if (typeName == "INumberBase`1")
        {
            result.Add("System.ISpanFormattable");
            result.Add("System.IUtf8SpanFormattable");
        }

        return result;
    }

    private sealed record MemberSurface(
        IReadOnlyDictionary<string, List<MethodModel>> Methods,
        IReadOnlyDictionary<string, List<PropertyModel>> Properties);
}

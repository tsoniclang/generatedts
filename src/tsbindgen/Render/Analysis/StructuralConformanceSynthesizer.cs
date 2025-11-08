using System.Collections.Generic;
using System.Linq;
using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Pass 2: Structural Conformance Synthesizer (fixes TS2420).
///
/// For each class implementing interfaces:
/// 1. Compute Surface(Interface) - Surface(Class) = Missing members
/// 2. Synthesize missing members with stable naming for explicit implementations
/// </summary>
public static class StructuralConformanceSynthesizer
{
    /// <summary>
    /// Applies structural conformance synthesis to resolve TS2420 errors.
    /// </summary>
    public static NamespaceModel Apply(
        NamespaceModel model,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        var updatedTypes = new List<TypeModel>();

        foreach (var type in model.Types)
        {
            if (type.Kind != TypeKind.Class && type.Kind != TypeKind.Struct)
            {
                updatedTypes.Add(type);
                continue;
            }

            var synthesized = SynthesizeMissingMembers(type, allModels, ctx);
            updatedTypes.Add(synthesized);
        }

        return model with { Types = updatedTypes };
    }

    private static TypeModel SynthesizeMissingMembers(
        TypeModel type,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        // Check both Implements (conformant interfaces) and ExplicitViews (non-conformant)
        // ExplicitViews were moved there by the earlier StructuralConformance pass
        var allInterfaces = new List<TypeReference>(type.Implements);

        if (type.ExplicitViews != null)
        {
            foreach (var view in type.ExplicitViews)
            {
                allInterfaces.Add(view.Interface);
            }
        }

        if (allInterfaces.Count == 0)
        {
            return type; // No interfaces, nothing to synthesize
        }

        var synthesizedMethods = new List<MethodModel>();
        var synthesizedProperties = new List<PropertyModel>();

        // For each interface, compute missing members
        foreach (var ifaceRef in allInterfaces)
        {
            var ifaceType = ResolveType(ifaceRef, allModels);
            if (ifaceType == null) continue;

            var ifaceSurface = ComputeSurface(ifaceType, allModels, ctx);
            var classSurface = ComputeClassSurface(type, allModels, ctx);

            // Find missing methods
            foreach (var (methodName, ifaceMethods) in ifaceSurface.Methods)
            {
                if (!classSurface.Methods.ContainsKey(methodName))
                {
                    // Method completely missing - synthesize all overloads
                    foreach (var method in ifaceMethods)
                    {
                        var synthesized = SynthesizeMethod(method, ifaceType, ifaceRef, ctx);
                        synthesizedMethods.Add(synthesized);
                    }
                }
                else
                {
                    // Method exists, but check if all overloads are present
                    var classMethods = classSurface.Methods[methodName];
                    var classSignatures = new HashSet<string>(
                        classMethods.Select(m => FormatMethodSignature(m, ctx)));

                    foreach (var ifaceMethod in ifaceMethods)
                    {
                        var ifaceSig = FormatMethodSignature(ifaceMethod, ctx);
                        if (!classSignatures.Contains(ifaceSig))
                        {
                            // Missing overload - synthesize
                            var synthesized = SynthesizeMethod(ifaceMethod, ifaceType, ifaceRef, ctx);
                            synthesizedMethods.Add(synthesized);
                        }
                    }
                }
            }

            // Find missing properties
            foreach (var (propName, ifaceProps) in ifaceSurface.Properties)
            {
                if (!classSurface.Properties.ContainsKey(propName))
                {
                    // Property completely missing - synthesize
                    foreach (var prop in ifaceProps)
                    {
                        var synthesized = SynthesizeProperty(prop, ifaceType, ifaceRef, ctx);
                        synthesizedProperties.Add(synthesized);
                    }
                }
            }
        }

        if (synthesizedMethods.Count == 0 && synthesizedProperties.Count == 0)
        {
            return type; // No missing members
        }

        // Merge synthesized members
        var allMethods = type.Members.Methods.Concat(synthesizedMethods).ToList();
        var allProperties = type.Members.Properties.Concat(synthesizedProperties).ToList();

        return type with
        {
            Members = type.Members with
            {
                Methods = allMethods,
                Properties = allProperties
            }
        };
    }

    private static MethodModel SynthesizeMethod(
        MethodModel ifaceMethod,
        TypeModel ifaceType,
        TypeReference ifaceRef,
        AnalysisContext ctx)
    {
        // Generate stable name for explicit interface implementation
        var synthesizedName = GenerateExplicitMemberName(
            ifaceMethod.ClrName,
            ifaceType.ClrName,
            ifaceRef);

        return ifaceMethod with
        {
            ClrName = synthesizedName,
            // Keep all other properties (parameters, return type, etc.)
        };
    }

    private static PropertyModel SynthesizeProperty(
        PropertyModel ifaceProp,
        TypeModel ifaceType,
        TypeReference ifaceRef,
        AnalysisContext ctx)
    {
        // Generate stable name for explicit interface implementation
        var synthesizedName = GenerateExplicitMemberName(
            ifaceProp.ClrName,
            ifaceType.ClrName,
            ifaceRef);

        return ifaceProp with
        {
            ClrName = synthesizedName,
            // Keep all other properties (type, getter/setter, etc.)
        };
    }

    /// <summary>
    /// Generates stable naming for explicit interface members.
    /// Format: {MemberName}_{InterfaceName}[_Of_{GenericArg1}_{GenericArg2}...]
    /// </summary>
    private static string GenerateExplicitMemberName(
        string memberName,
        string interfaceClrName,
        TypeReference ifaceRef)
    {
        // Remove generic arity suffix (e.g., ICollection`1 → ICollection)
        var ifaceName = interfaceClrName;
        var backtickIndex = ifaceName.IndexOf('`');
        if (backtickIndex > 0)
        {
            ifaceName = ifaceName.Substring(0, backtickIndex);
        }

        var baseName = $"{memberName}_{ifaceName}";

        // Add generic arguments if present
        if (ifaceRef.GenericArgs.Count > 0)
        {
            var argNames = ifaceRef.GenericArgs
                .Select(arg => GetTypeArgumentName(arg))
                .Where(n => n != null);

            if (argNames.Any())
            {
                var argSuffix = string.Join("_", argNames);
                return $"{baseName}_Of_{argSuffix}";
            }
        }

        return baseName;
    }

    private static string? GetTypeArgumentName(TypeReference typeRef)
    {
        if (typeRef.Kind == TypeReferenceKind.GenericParameter)
        {
            return typeRef.TypeName; // e.g., "T", "TKey", "TValue"
        }

        // For closed types, use the type name
        var name = typeRef.TypeName;
        var backtickIndex = name.IndexOf('`');
        if (backtickIndex > 0)
        {
            name = name.Substring(0, backtickIndex);
        }
        return name;
    }

    private static MemberSurface ComputeSurface(
        TypeModel type,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        // Start with own members
        var methods = type.Members.Methods
            .GroupBy(m => m.ClrName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var properties = type.Members.Properties
            .GroupBy(p => p.ClrName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Add parent surfaces (transitive) for interfaces
        if (type.Kind == TypeKind.Interface)
        {
            foreach (var parentRef in type.Implements)
            {
                var parentType = ResolveType(parentRef, allModels);
                if (parentType == null) continue;

                var parentSurface = ComputeSurface(parentType, allModels, ctx);

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
        }

        return new MemberSurface(methods, properties);
    }

    private static MemberSurface ComputeClassSurface(
        TypeModel type,
        IReadOnlyDictionary<string, NamespaceModel> allModels,
        AnalysisContext ctx)
    {
        // For classes, include base class members too
        var methods = type.Members.Methods
            .GroupBy(m => m.ClrName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var properties = type.Members.Properties
            .GroupBy(p => p.ClrName)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Add base class members
        if (type.BaseType != null)
        {
            var baseType = ResolveType(type.BaseType, allModels);
            if (baseType != null)
            {
                var baseSurface = ComputeClassSurface(baseType, allModels, ctx);

                foreach (var (name, baseMethods) in baseSurface.Methods)
                {
                    if (!methods.ContainsKey(name))
                    {
                        methods[name] = new List<MethodModel>();
                    }
                    methods[name].AddRange(baseMethods);
                }

                foreach (var (name, baseProps) in baseSurface.Properties)
                {
                    if (!properties.ContainsKey(name))
                    {
                        properties[name] = new List<PropertyModel>();
                    }
                    properties[name].AddRange(baseProps);
                }
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

    private static string FormatMethodSignature(MethodModel method, AnalysisContext ctx)
    {
        var paramTypes = string.Join(",", method.Parameters.Select(p => FormatTypeReference(p.Type, ctx)));
        var returnType = FormatTypeReference(method.ReturnType, ctx);
        return $"{method.ClrName}({paramTypes}): {returnType}";
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

    private sealed record MemberSurface(
        IReadOnlyDictionary<string, List<MethodModel>> Methods,
        IReadOnlyDictionary<string, List<PropertyModel>> Properties);
}

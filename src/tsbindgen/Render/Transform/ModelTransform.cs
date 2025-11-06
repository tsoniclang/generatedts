using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Transform;

/// <summary>
/// Phase 3: Converts NamespaceBundle (from Phase 2) to NamespaceModel.
/// Applies name transformations (creates TsAlias via NameTransformation.Apply).
/// This is the CLR→TypeScript bridge.
/// </summary>
public static class ModelTransform
{
    public static NamespaceModel Build(
        NamespaceBundle bundle,
        GeneratorConfig config)
    {
        var tsAlias = NameTransformation.Apply(bundle.ClrName, config.NamespaceNames);

        var types = bundle.Types
            .Select(t => BuildType(t, config, bundle.ClrName))
            .ToList();

        var imports = bundle.Imports
            .ToDictionary(
                kvp => kvp.Key,
                kvp => (IReadOnlySet<string>)kvp.Value.ToHashSet());

        return new NamespaceModel(
            bundle.ClrName,
            tsAlias,
            types,
            imports,
            bundle.Diagnostics,
            bundle.SourceAssemblies.ToList());
    }

    private static TypeModel BuildType(TypeSnapshot snapshot, GeneratorConfig config, string currentNamespace)
    {
        // Build TsAlias using structured naming + CLI transformations
        var baseName = TsNaming.ForAnalysis(snapshot.Binding.Type);
        var tsAlias = snapshot.Kind switch
        {
            TypeKind.Interface => NameTransformation.Apply(baseName, config.InterfaceNames),
            TypeKind.Class => NameTransformation.Apply(baseName, config.ClassNames),
            _ => baseName
        };

        var genericParams = snapshot.GenericParameters
            .Select(gp => new GenericParameterModel(
                gp.Name,
                gp.Name, // Generic parameters don't get transformed
                gp.Constraints.ToList(),
                gp.Variance))
            .ToList();

        var members = BuildMembers(snapshot.Members, config, currentNamespace);

        return new TypeModel(
            snapshot.ClrName,
            tsAlias,
            snapshot.Kind,
            snapshot.IsStatic,
            snapshot.IsSealed,
            snapshot.IsAbstract,
            snapshot.Visibility,
            genericParams,
            snapshot.BaseType,
            snapshot.Implements.ToList(),
            members,
            snapshot.Binding,
            Array.Empty<Diagnostic>(), // Type-level diagnostics added by analysis passes
            Array.Empty<HelperDeclaration>(), // Helpers added by analysis passes
            snapshot.UnderlyingType,
            snapshot.EnumMembers,
            snapshot.DelegateParameters?.Select(p => BuildParameter(p)).ToList(),
            snapshot.DelegateReturnType);
    }

    private static MemberCollectionModel BuildMembers(
        MemberCollection members,
        GeneratorConfig config,
        string currentNamespace)
    {
        var constructors = members.Constructors
            .Select(c => new ConstructorModel(
                c.Visibility,
                c.Parameters.Select(p => BuildParameter(p)).ToList()))
            .ToList();

        var methods = members.Methods
            .Select(m => BuildMethod(m, config))
            .ToList();

        var properties = members.Properties
            .Select(p => BuildProperty(p, config))
            .ToList();

        var fields = members.Fields
            .Select(f => BuildField(f, config))
            .ToList();

        var events = members.Events
            .Select(e => BuildEvent(e, config))
            .ToList();

        return new MemberCollectionModel(constructors, methods, properties, fields, events);
    }

    private static MethodModel BuildMethod(MethodSnapshot snapshot, GeneratorConfig config)
    {
        var tsAlias = NameTransformation.Apply(snapshot.ClrName, config.MethodNames);

        var genericParams = snapshot.GenericParameters
            .Select(gp => new GenericParameterModel(
                gp.Name,
                gp.Name,
                gp.Constraints.ToList(),
                gp.Variance))
            .ToList();

        return new MethodModel(
            snapshot.ClrName,
            tsAlias,
            snapshot.IsStatic,
            snapshot.IsVirtual,
            snapshot.IsOverride,
            snapshot.IsAbstract,
            snapshot.Visibility,
            genericParams,
            snapshot.Parameters.Select(p => BuildParameter(p)).ToList(),
            snapshot.ReturnType,
            snapshot.Binding);
    }

    private static PropertyModel BuildProperty(PropertySnapshot snapshot, GeneratorConfig config)
    {
        var tsAlias = NameTransformation.Apply(snapshot.ClrName, config.PropertyNames);

        return new PropertyModel(
            snapshot.ClrName,
            tsAlias,
            snapshot.Type,
            snapshot.IsReadOnly,
            snapshot.IsStatic,
            snapshot.IsVirtual,
            snapshot.IsOverride,
            snapshot.Visibility,
            snapshot.Binding,
            snapshot.ContractType);
    }

    private static FieldModel BuildField(FieldSnapshot snapshot, GeneratorConfig config)
    {
        var tsAlias = NameTransformation.Apply(snapshot.ClrName, config.PropertyNames);

        return new FieldModel(
            snapshot.ClrName,
            tsAlias,
            snapshot.Type,
            snapshot.IsReadOnly,
            snapshot.IsStatic,
            snapshot.Visibility,
            snapshot.Binding);
    }

    private static EventModel BuildEvent(EventSnapshot snapshot, GeneratorConfig config)
    {
        var tsAlias = NameTransformation.Apply(snapshot.ClrName, config.PropertyNames);

        return new EventModel(
            snapshot.ClrName,
            tsAlias,
            snapshot.Type,
            snapshot.IsStatic,
            snapshot.Visibility,
            snapshot.Binding);
    }

    private static ParameterModel BuildParameter(ParameterSnapshot snapshot)
    {
        return new ParameterModel(
            snapshot.Name,
            snapshot.Type,
            snapshot.Kind,
            snapshot.IsOptional,
            snapshot.DefaultValue,
            snapshot.IsParams);
    }

}

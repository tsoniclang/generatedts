using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;
using tsbindgen.SinglePhase.Plan;

namespace tsbindgen.SinglePhase.Emit;

/// <summary>
/// Emits bindings.json files with CLR-to-TypeScript name mappings.
/// Provides correlation data for runtime binding and code generation.
/// </summary>
public static class BindingEmitter
{
    public static void Emit(BuildContext ctx, EmissionPlan plan, string outputDirectory)
    {
        ctx.Log("BindingEmitter: Generating bindings.json files...");

        var emittedCount = 0;

        // Process each namespace in order
        foreach (var nsOrder in plan.EmissionOrder.Namespaces)
        {
            var ns = nsOrder.Namespace;
            ctx.Log($"  Emitting bindings for: {ns.Name}");

            // Generate bindings
            var bindings = GenerateBindings(nsOrder);

            // Write to file: output/Namespace.Name/bindings.json
            var namespacePath = Path.Combine(outputDirectory, ns.Name);
            Directory.CreateDirectory(namespacePath);

            var outputFile = Path.Combine(namespacePath, "bindings.json");
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };
            var json = JsonSerializer.Serialize(bindings, jsonOptions);
            File.WriteAllText(outputFile, json);

            ctx.Log($"    → {outputFile}");
            emittedCount++;
        }

        ctx.Log($"BindingEmitter: Generated {emittedCount} binding files");
    }

    private static NamespaceBindings GenerateBindings(NamespaceEmitOrder nsOrder)
    {
        var typeBindings = new List<TypeBinding>();

        foreach (var typeOrder in nsOrder.OrderedTypes)
        {
            typeBindings.Add(GenerateTypeBinding(typeOrder.Type));
        }

        return new NamespaceBindings
        {
            Namespace = nsOrder.Namespace.Name,
            Types = typeBindings
        };
    }

    private static TypeBinding GenerateTypeBinding(TypeSymbol type)
    {
        return new TypeBinding
        {
            ClrName = type.ClrFullName,
            TsEmitName = type.TsEmitName,
            AssemblyName = type.StableId.AssemblyName,
            MetadataToken = 0, // Types don't have metadata tokens
            Methods = type.Members.Methods
                .Where(m => m.EmitScope != EmitScope.ViewOnly)
                .Select(GenerateMethodBinding)
                .ToList(),
            Properties = type.Members.Properties
                .Where(p => p.EmitScope != EmitScope.ViewOnly)
                .Select(GeneratePropertyBinding)
                .ToList(),
            Fields = type.Members.Fields.Select(GenerateFieldBinding).ToList(),
            Events = type.Members.Events.Select(GenerateEventBinding).ToList(),
            Constructors = type.Members.Constructors.Select(GenerateConstructorBinding).ToList()
        };
    }

    private static MethodBinding GenerateMethodBinding(MethodSymbol method)
    {
        return new MethodBinding
        {
            ClrName = method.ClrName,
            TsEmitName = method.TsEmitName,
            MetadataToken = method.StableId.MetadataToken ?? 0,
            CanonicalSignature = method.StableId.CanonicalSignature,
            EmitScope = method.EmitScope.ToString(),
            Arity = method.Arity,
            ParameterCount = method.Parameters.Count
        };
    }

    private static PropertyBinding GeneratePropertyBinding(PropertySymbol property)
    {
        return new PropertyBinding
        {
            ClrName = property.ClrName,
            TsEmitName = property.TsEmitName,
            MetadataToken = property.StableId.MetadataToken ?? 0,
            CanonicalSignature = property.StableId.CanonicalSignature,
            EmitScope = property.EmitScope.ToString(),
            IsIndexer = property.IsIndexer,
            HasGetter = property.HasGetter,
            HasSetter = property.HasSetter
        };
    }

    private static FieldBinding GenerateFieldBinding(FieldSymbol field)
    {
        return new FieldBinding
        {
            ClrName = field.ClrName,
            TsEmitName = field.TsEmitName,
            MetadataToken = field.StableId.MetadataToken ?? 0,
            IsStatic = field.IsStatic,
            IsReadOnly = field.IsReadOnly
        };
    }

    private static EventBinding GenerateEventBinding(EventSymbol evt)
    {
        return new EventBinding
        {
            ClrName = evt.ClrName,
            TsEmitName = evt.TsEmitName,
            MetadataToken = evt.StableId.MetadataToken ?? 0,
            IsStatic = evt.IsStatic
        };
    }

    private static ConstructorBinding GenerateConstructorBinding(ConstructorSymbol ctor)
    {
        return new ConstructorBinding
        {
            MetadataToken = ctor.StableId.MetadataToken ?? 0,
            CanonicalSignature = ctor.StableId.CanonicalSignature,
            IsStatic = ctor.IsStatic,
            ParameterCount = ctor.Parameters.Count
        };
    }
}

/// <summary>
/// Bindings for a namespace.
/// </summary>
public sealed record NamespaceBindings
{
    public required string Namespace { get; init; }
    public required List<TypeBinding> Types { get; init; }
}

/// <summary>
/// Binding for a type.
/// </summary>
public sealed record TypeBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required string AssemblyName { get; init; }
    public required int MetadataToken { get; init; }
    public required List<MethodBinding> Methods { get; init; }
    public required List<PropertyBinding> Properties { get; init; }
    public required List<FieldBinding> Fields { get; init; }
    public required List<EventBinding> Events { get; init; }
    public required List<ConstructorBinding> Constructors { get; init; }
}

/// <summary>
/// Binding for a method.
/// </summary>
public sealed record MethodBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required int MetadataToken { get; init; }
    public required string CanonicalSignature { get; init; }
    public required string EmitScope { get; init; }
    public required int Arity { get; init; }
    public required int ParameterCount { get; init; }
}

/// <summary>
/// Binding for a property.
/// </summary>
public sealed record PropertyBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required int MetadataToken { get; init; }
    public required string CanonicalSignature { get; init; }
    public required string EmitScope { get; init; }
    public required bool IsIndexer { get; init; }
    public required bool HasGetter { get; init; }
    public required bool HasSetter { get; init; }
}

/// <summary>
/// Binding for a field.
/// </summary>
public sealed record FieldBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required int MetadataToken { get; init; }
    public required bool IsStatic { get; init; }
    public required bool IsReadOnly { get; init; }
}

/// <summary>
/// Binding for an event.
/// </summary>
public sealed record EventBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required int MetadataToken { get; init; }
    public required bool IsStatic { get; init; }
}

/// <summary>
/// Binding for a constructor.
/// </summary>
public sealed record ConstructorBinding
{
    public required int MetadataToken { get; init; }
    public required string CanonicalSignature { get; init; }
    public required bool IsStatic { get; init; }
    public required int ParameterCount { get; init; }
}

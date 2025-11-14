using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using tsbindgen.SinglePhase.Renaming;
using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;
using tsbindgen.SinglePhase.Normalize;
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
        ctx.Log("BindingEmitter", "Generating bindings.json files...");

        var emittedCount = 0;

        // Process each namespace in order
        foreach (var nsOrder in plan.EmissionOrder.Namespaces)
        {
            var ns = nsOrder.Namespace;
            ctx.Log("BindingEmitter", $"  Emitting bindings for: {ns.Name}");

            // Generate bindings
            var bindings = GenerateBindings(ctx, nsOrder);

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

            ctx.Log("BindingEmitter", $"    → {outputFile}");
            emittedCount++;
        }

        ctx.Log("BindingEmitter", $"Generated {emittedCount} binding files");
    }

    private static NamespaceBindings GenerateBindings(BuildContext ctx, NamespaceEmitOrder nsOrder)
    {
        var typeBindings = new List<TypeBinding>();

        foreach (var typeOrder in nsOrder.OrderedTypes)
        {
            typeBindings.Add(GenerateTypeBinding(typeOrder.Type, ctx));
        }

        return new NamespaceBindings
        {
            Namespace = nsOrder.Namespace.Name,
            Types = typeBindings
        };
    }

    private static TypeBinding GenerateTypeBinding(TypeSymbol type, BuildContext ctx)
    {
        // Get final TypeScript name from Renamer
        var tsEmitName = ctx.Renamer.GetFinalTypeName(type);

        // V1: Generate definitions (what CLR declares on this type)
        var methodDefinitions = type.Members.Methods
            .Select(m => GenerateMethodBinding(m, type, ctx))
            .ToList();
        var propertyDefinitions = type.Members.Properties
            .Select(p => GeneratePropertyBinding(p, type, ctx))
            .ToList();
        var fieldDefinitions = type.Members.Fields
            .Select(f => GenerateFieldBinding(f, type, ctx))
            .ToList();
        var eventDefinitions = type.Members.Events
            .Select(e => GenerateEventBinding(e, type, ctx))
            .ToList();
        var constructorDefinitions = type.Members.Constructors
            .Select(c => GenerateConstructorBinding(c, type, ctx))
            .ToList();

        // V2: Generate exposures (what TS shows, and where it forwards)
        var exposedMethods = type.Members.Methods
            .Where(m => m.EmitScope != EmitScope.Omitted) // Only expose non-omitted members
            .Select(m => GenerateMethodExposure(m, type, ctx))
            .ToList();
        var exposedProperties = type.Members.Properties
            .Where(p => p.EmitScope != EmitScope.Omitted)
            .Select(p => GeneratePropertyExposure(p, type, ctx))
            .ToList();
        var exposedFields = type.Members.Fields
            .Select(f => GenerateFieldExposure(f, type, ctx))
            .ToList();
        var exposedEvents = type.Members.Events
            .Select(e => GenerateEventExposure(e, type, ctx))
            .ToList();
        var exposedConstructors = type.Members.Constructors
            .Select(c => GenerateConstructorExposure(c, type, ctx))
            .ToList();

        return new TypeBinding
        {
            ClrName = type.ClrFullName,
            TsEmitName = tsEmitName,
            AssemblyName = type.StableId.AssemblyName,
            MetadataToken = 0, // Types don't have metadata tokens

            // V1: Definitions
            Methods = methodDefinitions,
            Properties = propertyDefinitions,
            Fields = fieldDefinitions,
            Events = eventDefinitions,
            Constructors = constructorDefinitions,

            // V2: Exposures
            ExposedMethods = exposedMethods.Any() ? exposedMethods : null,
            ExposedProperties = exposedProperties.Any() ? exposedProperties : null,
            ExposedFields = exposedFields.Any() ? exposedFields : null,
            ExposedEvents = exposedEvents.Any() ? exposedEvents : null,
            ExposedConstructors = exposedConstructors.Any() ? exposedConstructors : null
        };
    }

    private static MethodBinding GenerateMethodBinding(MethodSymbol method, TypeSymbol declaringType, BuildContext ctx)
    {
        // M5 FIX: Use view scope for ViewOnly members, class scope for others
        string tsEmitName;
        if (method.EmitScope == EmitScope.ViewOnly && method.SourceInterface != null)
        {
            // ViewOnly member - use view scope
            var interfaceStableId = ScopeFactory.GetInterfaceStableId(method.SourceInterface);
            var viewScope = ScopeFactory.ViewSurface(declaringType, interfaceStableId, method.IsStatic);
            tsEmitName = ctx.Renamer.GetFinalMemberName(method.StableId, viewScope);
        }
        else
        {
            // Class surface member - use class scope
            var classScope = ScopeFactory.ClassSurface(declaringType, method.IsStatic);
            tsEmitName = ctx.Renamer.GetFinalMemberName(method.StableId, classScope);
        }

        // Generate normalized signature for universal matching
        var normalizedSignature = SignatureNormalization.NormalizeMethod(method);

        return new MethodBinding
        {
            ClrName = method.ClrName,
            TsEmitName = tsEmitName,
            MetadataToken = method.StableId.MetadataToken ?? 0,
            CanonicalSignature = method.StableId.CanonicalSignature,
            NormalizedSignature = normalizedSignature,
            EmitScope = method.EmitScope.ToString(),
            Arity = method.Arity,
            ParameterCount = method.Parameters.Length,
            // V2: Add declaring type information from StableId
            DeclaringClrType = method.StableId.DeclaringClrFullName,
            DeclaringAssemblyName = method.StableId.AssemblyName
        };
    }

    private static PropertyBinding GeneratePropertyBinding(PropertySymbol property, TypeSymbol declaringType, BuildContext ctx)
    {
        // M5 FIX: Use view scope for ViewOnly members, class scope for others
        string tsEmitName;
        if (property.EmitScope == EmitScope.ViewOnly && property.SourceInterface != null)
        {
            // ViewOnly member - use view scope
            var interfaceStableId = ScopeFactory.GetInterfaceStableId(property.SourceInterface);
            var viewScope = ScopeFactory.ViewSurface(declaringType, interfaceStableId, property.IsStatic);
            tsEmitName = ctx.Renamer.GetFinalMemberName(property.StableId, viewScope);
        }
        else
        {
            // Class surface member - use class scope
            var classScope = ScopeFactory.ClassSurface(declaringType, property.IsStatic);
            tsEmitName = ctx.Renamer.GetFinalMemberName(property.StableId, classScope);
        }

        // Generate normalized signature for universal matching
        var normalizedSignature = SignatureNormalization.NormalizeProperty(property);

        return new PropertyBinding
        {
            ClrName = property.ClrName,
            TsEmitName = tsEmitName,
            MetadataToken = property.StableId.MetadataToken ?? 0,
            CanonicalSignature = property.StableId.CanonicalSignature,
            NormalizedSignature = normalizedSignature,
            EmitScope = property.EmitScope.ToString(),
            IsIndexer = property.IsIndexer,
            HasGetter = property.HasGetter,
            HasSetter = property.HasSetter,
            // V2: Add declaring type information from StableId
            DeclaringClrType = property.StableId.DeclaringClrFullName,
            DeclaringAssemblyName = property.StableId.AssemblyName
        };
    }

    private static FieldBinding GenerateFieldBinding(FieldSymbol field, TypeSymbol declaringType, BuildContext ctx)
    {
        // Fields are always ClassSurface, use class scope
        var classScope = ScopeFactory.ClassSurface(declaringType, field.IsStatic);
        var tsEmitName = ctx.Renamer.GetFinalMemberName(field.StableId, classScope);

        // Generate normalized signature for universal matching
        var normalizedSignature = SignatureNormalization.NormalizeField(field);

        return new FieldBinding
        {
            ClrName = field.ClrName,
            TsEmitName = tsEmitName,
            MetadataToken = field.StableId.MetadataToken ?? 0,
            NormalizedSignature = normalizedSignature,
            IsStatic = field.IsStatic,
            IsReadOnly = field.IsReadOnly,
            // V2: Add declaring type information from StableId
            DeclaringClrType = field.StableId.DeclaringClrFullName,
            DeclaringAssemblyName = field.StableId.AssemblyName
        };
    }

    private static EventBinding GenerateEventBinding(EventSymbol evt, TypeSymbol declaringType, BuildContext ctx)
    {
        // Events are always ClassSurface, use class scope
        var classScope = ScopeFactory.ClassSurface(declaringType, evt.IsStatic);
        var tsEmitName = ctx.Renamer.GetFinalMemberName(evt.StableId, classScope);

        // Generate normalized signature for universal matching
        var normalizedSignature = SignatureNormalization.NormalizeEvent(evt);

        return new EventBinding
        {
            ClrName = evt.ClrName,
            TsEmitName = tsEmitName,
            MetadataToken = evt.StableId.MetadataToken ?? 0,
            NormalizedSignature = normalizedSignature,
            IsStatic = evt.IsStatic,
            // V2: Add declaring type information from StableId
            DeclaringClrType = evt.StableId.DeclaringClrFullName,
            DeclaringAssemblyName = evt.StableId.AssemblyName
        };
    }

    private static ConstructorBinding GenerateConstructorBinding(ConstructorSymbol ctor, TypeSymbol declaringType, BuildContext ctx)
    {
        // Constructors always have name "constructor" in TypeScript, but record it from Renamer for consistency
        // Generate normalized signature for universal matching
        var normalizedSignature = SignatureNormalization.NormalizeConstructor(ctor);

        return new ConstructorBinding
        {
            MetadataToken = ctor.StableId.MetadataToken ?? 0,
            CanonicalSignature = ctor.StableId.CanonicalSignature,
            NormalizedSignature = normalizedSignature,
            IsStatic = ctor.IsStatic,
            ParameterCount = ctor.Parameters.Length,
            // V2: Add declaring type information from StableId
            DeclaringClrType = ctor.StableId.DeclaringClrFullName,
            DeclaringAssemblyName = ctor.StableId.AssemblyName
        };
    }

    // ============================================================================
    // V2 EXPOSURE GENERATION
    // ============================================================================

    private static MethodExposure GenerateMethodExposure(MethodSymbol method, TypeSymbol ownerType, BuildContext ctx)
    {
        // Get TS name (same logic as definition)
        string tsName;
        if (method.EmitScope == EmitScope.ViewOnly && method.SourceInterface != null)
        {
            var interfaceStableId = ScopeFactory.GetInterfaceStableId(method.SourceInterface);
            var viewScope = ScopeFactory.ViewSurface(ownerType, interfaceStableId, method.IsStatic);
            tsName = ctx.Renamer.GetFinalMemberName(method.StableId, viewScope);
        }
        else
        {
            var classScope = ScopeFactory.ClassSurface(ownerType, method.IsStatic);
            tsName = ctx.Renamer.GetFinalMemberName(method.StableId, classScope);
        }

        // Use NormalizedSignature as TsSignatureId for overload disambiguation
        var tsSignatureId = SignatureNormalization.NormalizeMethod(method);

        return new MethodExposure
        {
            TsName = tsName,
            IsStatic = method.IsStatic,
            TsSignatureId = tsSignatureId,
            Target = new ExposureTarget
            {
                DeclaringClrType = method.StableId.DeclaringClrFullName,
                DeclaringAssemblyName = method.StableId.AssemblyName,
                MetadataToken = method.StableId.MetadataToken ?? 0
            }
        };
    }

    private static PropertyExposure GeneratePropertyExposure(PropertySymbol property, TypeSymbol ownerType, BuildContext ctx)
    {
        // Get TS name (same logic as definition)
        string tsName;
        if (property.EmitScope == EmitScope.ViewOnly && property.SourceInterface != null)
        {
            var interfaceStableId = ScopeFactory.GetInterfaceStableId(property.SourceInterface);
            var viewScope = ScopeFactory.ViewSurface(ownerType, interfaceStableId, property.IsStatic);
            tsName = ctx.Renamer.GetFinalMemberName(property.StableId, viewScope);
        }
        else
        {
            var classScope = ScopeFactory.ClassSurface(ownerType, property.IsStatic);
            tsName = ctx.Renamer.GetFinalMemberName(property.StableId, classScope);
        }

        var tsSignatureId = SignatureNormalization.NormalizeProperty(property);

        return new PropertyExposure
        {
            TsName = tsName,
            IsStatic = property.IsStatic,
            TsSignatureId = tsSignatureId,
            Target = new ExposureTarget
            {
                DeclaringClrType = property.StableId.DeclaringClrFullName,
                DeclaringAssemblyName = property.StableId.AssemblyName,
                MetadataToken = property.StableId.MetadataToken ?? 0
            }
        };
    }

    private static FieldExposure GenerateFieldExposure(FieldSymbol field, TypeSymbol ownerType, BuildContext ctx)
    {
        var classScope = ScopeFactory.ClassSurface(ownerType, field.IsStatic);
        var tsName = ctx.Renamer.GetFinalMemberName(field.StableId, classScope);
        var tsSignatureId = SignatureNormalization.NormalizeField(field);

        return new FieldExposure
        {
            TsName = tsName,
            IsStatic = field.IsStatic,
            TsSignatureId = tsSignatureId,
            Target = new ExposureTarget
            {
                DeclaringClrType = field.StableId.DeclaringClrFullName,
                DeclaringAssemblyName = field.StableId.AssemblyName,
                MetadataToken = field.StableId.MetadataToken ?? 0
            }
        };
    }

    private static EventExposure GenerateEventExposure(EventSymbol evt, TypeSymbol ownerType, BuildContext ctx)
    {
        var classScope = ScopeFactory.ClassSurface(ownerType, evt.IsStatic);
        var tsName = ctx.Renamer.GetFinalMemberName(evt.StableId, classScope);
        var tsSignatureId = SignatureNormalization.NormalizeEvent(evt);

        return new EventExposure
        {
            TsName = tsName,
            IsStatic = evt.IsStatic,
            TsSignatureId = tsSignatureId,
            Target = new ExposureTarget
            {
                DeclaringClrType = evt.StableId.DeclaringClrFullName,
                DeclaringAssemblyName = evt.StableId.AssemblyName,
                MetadataToken = evt.StableId.MetadataToken ?? 0
            }
        };
    }

    private static ConstructorExposure GenerateConstructorExposure(ConstructorSymbol ctor, TypeSymbol ownerType, BuildContext ctx)
    {
        var tsSignatureId = SignatureNormalization.NormalizeConstructor(ctor);

        return new ConstructorExposure
        {
            IsStatic = ctor.IsStatic,
            TsSignatureId = tsSignatureId,
            Target = new ExposureTarget
            {
                DeclaringClrType = ctor.StableId.DeclaringClrFullName,
                DeclaringAssemblyName = ctor.StableId.AssemblyName,
                MetadataToken = ctor.StableId.MetadataToken ?? 0
            }
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

    // V1: Definitions (what CLR declares on this type)
    public required List<MethodBinding> Methods { get; init; }
    public required List<PropertyBinding> Properties { get; init; }
    public required List<FieldBinding> Fields { get; init; }
    public required List<EventBinding> Events { get; init; }
    public required List<ConstructorBinding> Constructors { get; init; }

    // V2: Exposures (what TS shows, and where it forwards)
    public List<MethodExposure>? ExposedMethods { get; init; }
    public List<PropertyExposure>? ExposedProperties { get; init; }
    public List<FieldExposure>? ExposedFields { get; init; }
    public List<EventExposure>? ExposedEvents { get; init; }
    public List<ConstructorExposure>? ExposedConstructors { get; init; }
}

/// <summary>
/// Binding for a method (definition).
/// </summary>
public sealed record MethodBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required int MetadataToken { get; init; }
    public required string CanonicalSignature { get; init; }
    public required string NormalizedSignature { get; init; }
    public required string EmitScope { get; init; }
    public required int Arity { get; init; }
    public required int ParameterCount { get; init; }

    // V2: Declaring type information
    public string? DeclaringClrType { get; init; }
    public string? DeclaringAssemblyName { get; init; }
}

/// <summary>
/// Binding for a property (definition).
/// </summary>
public sealed record PropertyBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required int MetadataToken { get; init; }
    public required string CanonicalSignature { get; init; }
    public required string NormalizedSignature { get; init; }
    public required string EmitScope { get; init; }
    public required bool IsIndexer { get; init; }
    public required bool HasGetter { get; init; }
    public required bool HasSetter { get; init; }

    // V2: Declaring type information
    public string? DeclaringClrType { get; init; }
    public string? DeclaringAssemblyName { get; init; }
}

/// <summary>
/// Binding for a field (definition).
/// </summary>
public sealed record FieldBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required int MetadataToken { get; init; }
    public required string NormalizedSignature { get; init; }
    public required bool IsStatic { get; init; }
    public required bool IsReadOnly { get; init; }

    // V2: Declaring type information
    public string? DeclaringClrType { get; init; }
    public string? DeclaringAssemblyName { get; init; }
}

/// <summary>
/// Binding for an event (definition).
/// </summary>
public sealed record EventBinding
{
    public required string ClrName { get; init; }
    public required string TsEmitName { get; init; }
    public required int MetadataToken { get; init; }
    public required string NormalizedSignature { get; init; }
    public required bool IsStatic { get; init; }

    // V2: Declaring type information
    public string? DeclaringClrType { get; init; }
    public string? DeclaringAssemblyName { get; init; }
}

/// <summary>
/// Binding for a constructor (definition).
/// </summary>
public sealed record ConstructorBinding
{
    public required int MetadataToken { get; init; }
    public required string CanonicalSignature { get; init; }
    public required string NormalizedSignature { get; init; }
    public required bool IsStatic { get; init; }
    public required int ParameterCount { get; init; }

    // V2: Declaring type information
    public string? DeclaringClrType { get; init; }
    public string? DeclaringAssemblyName { get; init; }
}

// ============================================================================
// V2 EXPOSURE TYPES
// ============================================================================

/// <summary>
/// Target of an exposure - where the actual CLR implementation lives.
/// </summary>
public sealed record ExposureTarget
{
    public required string DeclaringClrType { get; init; }
    public required string DeclaringAssemblyName { get; init; }
    public required int MetadataToken { get; init; }
}

/// <summary>
/// Method exposure - a method visible on the TS surface that forwards to a CLR method.
/// </summary>
public sealed record MethodExposure
{
    public required string TsName { get; init; }
    public required bool IsStatic { get; init; }
    public required string TsSignatureId { get; init; }
    public required ExposureTarget Target { get; init; }
}

/// <summary>
/// Property exposure - a property visible on the TS surface that forwards to a CLR property.
/// </summary>
public sealed record PropertyExposure
{
    public required string TsName { get; init; }
    public required bool IsStatic { get; init; }
    public required string TsSignatureId { get; init; }
    public required ExposureTarget Target { get; init; }
}

/// <summary>
/// Field exposure - a field visible on the TS surface that forwards to a CLR field.
/// </summary>
public sealed record FieldExposure
{
    public required string TsName { get; init; }
    public required bool IsStatic { get; init; }
    public required string TsSignatureId { get; init; }
    public required ExposureTarget Target { get; init; }
}

/// <summary>
/// Event exposure - an event visible on the TS surface that forwards to a CLR event.
/// </summary>
public sealed record EventExposure
{
    public required string TsName { get; init; }
    public required bool IsStatic { get; init; }
    public required string TsSignatureId { get; init; }
    public required ExposureTarget Target { get; init; }
}

/// <summary>
/// Constructor exposure - a constructor visible on the TS surface that forwards to a CLR constructor.
/// </summary>
public sealed record ConstructorExposure
{
    public required bool IsStatic { get; init; }
    public required string TsSignatureId { get; init; }
    public required ExposureTarget Target { get; init; }
}

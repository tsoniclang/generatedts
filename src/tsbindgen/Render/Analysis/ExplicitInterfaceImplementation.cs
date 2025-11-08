using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Phase 3: Adds interface members that are missing from classes.
///
/// In C#, explicit interface implementation means interface members are only accessible
/// through the interface reference. TypeScript doesn't support this - all interface members
/// must be present on the implementing class.
///
/// This pass also handles generic type parameter substitution.
///
/// Example:
/// interface IEqualityComparer_1&lt;T&gt; { Equals(x: T, y: T): Boolean; }
/// class ByteEqualityComparer implements IEqualityComparer_1&lt;System.Byte&gt;
///
/// Without this pass: Missing Equals method → TS2420 error
/// With substitution: Adds Equals(x: System.Byte, y: System.Byte): Boolean
/// </summary>
public static class ExplicitInterfaceImplementation
{
    public static NamespaceModel Apply(NamespaceModel model, IReadOnlyDictionary<string, NamespaceModel> allModels, AnalysisContext ctx)
    {
        // Build global type lookup
        var globalTypeLookup = new Dictionary<string, TypeModel>();
        foreach (var ns in allModels.Values)
        {
            foreach (var type in ns.Types)
            {
                var key = GetTypeKey(type.Binding.Type);
                globalTypeLookup[key] = type;
            }
        }

        // Process each class/struct type
        var updatedTypes = model.Types.Select(type =>
            type.Kind == TypeKind.Class || type.Kind == TypeKind.Struct
                ? AddMissingInterfaceMembers(type, globalTypeLookup, ctx)
                : type
        ).ToList();

        return model with { Types = updatedTypes };
    }

    private static TypeModel AddMissingInterfaceMembers(TypeModel type, Dictionary<string, TypeModel> typeLookup, AnalysisContext ctx)
    {
        if (type.Implements.Count == 0)
            return type; // No interfaces

        // Collect existing member names
        var existingMembers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in type.Members.Methods)
            existingMembers.Add(ctx.GetMethodIdentifier(method));
        foreach (var prop in type.Members.Properties)
            existingMembers.Add(ctx.GetPropertyIdentifier(prop));
        foreach (var field in type.Members.Fields)
            existingMembers.Add(ctx.GetFieldIdentifier(field));
        foreach (var evt in type.Members.Events)
            existingMembers.Add(ctx.GetEventIdentifier(evt));

        var newMethods = new List<MethodModel>();
        var newProperties = new List<PropertyModel>();
        var newEvents = new List<EventModel>();

        // Check each implemented interface
        foreach (var interfaceRef in type.Implements)
        {
            var interfaceType = FindInterfaceType(interfaceRef, typeLookup);
            if (interfaceType == null) continue;

            // Build substitution map for generic parameters
            var substitutions = GenericSubstitution.BuildSubstitutionMap(interfaceRef, interfaceType.GenericParameters);

            // Check for missing properties
            foreach (var interfaceProp in interfaceType.Members.Properties)
            {
                if (existingMembers.Contains(ctx.GetPropertyIdentifier(interfaceProp)))
                    continue;

                // Substitute generic parameters
                var substitutedProp = GenericSubstitution.SubstituteProperty(interfaceProp, substitutions);

                // Check if property still references undefined type parameters
                if (ReferencesUndefinedTypeParams(substitutedProp, type.GenericParameters, ctx))
                    continue;

                // Add as synthetic property
                newProperties.Add(substitutedProp with
                {
                    SyntheticMember = true
                });

                existingMembers.Add(ctx.GetPropertyIdentifier(interfaceProp));
            }

            // Check for missing methods
            foreach (var interfaceMethod in interfaceType.Members.Methods)
            {
                if (existingMembers.Contains(ctx.GetMethodIdentifier(interfaceMethod)))
                    continue;

                // Substitute generic parameters
                var substitutedMethod = GenericSubstitution.SubstituteMethod(interfaceMethod, substitutions);

                // Check if method still references undefined type parameters
                if (ReferencesUndefinedTypeParams(substitutedMethod, type.GenericParameters, ctx))
                    continue;

                // Add as synthetic method
                newMethods.Add(substitutedMethod with
                {
                    SyntheticOverload = new SyntheticOverloadInfo(
                        type.Binding.Type.GetClrType(),
                        ctx.GetMethodIdentifier(interfaceMethod),
                        SyntheticOverloadReason.InterfaceSignatureMismatch
                    )
                });

                existingMembers.Add(ctx.GetMethodIdentifier(interfaceMethod));
            }

            // Check for missing events
            foreach (var interfaceEvent in interfaceType.Members.Events)
            {
                if (existingMembers.Contains(ctx.GetEventIdentifier(interfaceEvent)))
                    continue;

                // Substitute generic parameters
                var substitutedEvent = interfaceEvent with
                {
                    Type = GenericSubstitution.SubstituteType(interfaceEvent.Type, substitutions)
                };

                // Check if event still references undefined type parameters
                if (ReferencesUndefinedTypeParams(substitutedEvent.Type, type.GenericParameters, ctx))
                    continue;

                // Add as synthetic event
                newEvents.Add(substitutedEvent with
                {
                    SyntheticMember = true
                });

                existingMembers.Add(ctx.GetEventIdentifier(interfaceEvent));
            }
        }

        if (newMethods.Count == 0 && newProperties.Count == 0 && newEvents.Count == 0)
            return type; // No missing members

        // Add new members to type
        var updatedMethods = type.Members.Methods.Concat(newMethods).ToList();
        var updatedProperties = type.Members.Properties.Concat(newProperties).ToList();
        var updatedEvents = type.Members.Events.Concat(newEvents).ToList();

        var updatedMembers = type.Members with
        {
            Methods = updatedMethods,
            Properties = updatedProperties,
            Events = updatedEvents
        };

        return type with { Members = updatedMembers };
    }

    private static TypeModel? FindInterfaceType(TypeReference typeRef, Dictionary<string, TypeModel> typeLookup)
    {
        var key = GetTypeKey(typeRef);
        typeLookup.TryGetValue(key, out var type);
        return type;
    }

    private static bool ReferencesUndefinedTypeParams(MethodModel method, IReadOnlyList<GenericParameterModel> availableTypeParams, AnalysisContext ctx)
    {
        var availableNames = new HashSet<string>(availableTypeParams.Select(p => ctx.GetGenericParameterIdentifier(p)));

        // Add method-level type parameters
        foreach (var gp in method.GenericParameters)
            availableNames.Add(ctx.GetGenericParameterIdentifier(gp));

        // Check return type
        if (ReferencesUndefinedTypeParams(method.ReturnType, availableNames))
            return true;

        // Check parameters
        foreach (var param in method.Parameters)
        {
            if (ReferencesUndefinedTypeParams(param.Type, availableNames))
                return true;
        }

        return false;
    }

    private static bool ReferencesUndefinedTypeParams(PropertyModel property, IReadOnlyList<GenericParameterModel> availableTypeParams, AnalysisContext ctx)
    {
        var availableNames = new HashSet<string>(availableTypeParams.Select(p => ctx.GetGenericParameterIdentifier(p)));
        return ReferencesUndefinedTypeParams(property.Type, availableNames);
    }

    private static bool ReferencesUndefinedTypeParams(TypeReference type, IReadOnlyList<GenericParameterModel> availableTypeParams, AnalysisContext ctx)
    {
        var availableNames = new HashSet<string>(availableTypeParams.Select(p => ctx.GetGenericParameterIdentifier(p)));
        return ReferencesUndefinedTypeParams(type, availableNames);
    }

    private static bool ReferencesUndefinedTypeParams(TypeReference type, HashSet<string> availableNames)
    {
        // Check if this is an undefined type parameter
        if (type.Namespace == null && !availableNames.Contains(type.TypeName))
        {
            // Type parameter names are typically single letters or start with T
            if (type.TypeName.Length <= 2 || type.TypeName.StartsWith("T"))
                return true;
        }

        // Check generic arguments recursively
        foreach (var arg in type.GenericArgs)
        {
            if (ReferencesUndefinedTypeParams(arg, availableNames))
                return true;
        }

        return false;
    }

    private static string GetTypeKey(TypeReference typeRef)
    {
        var ns = typeRef.Namespace != null ? typeRef.Namespace + "." : "";
        return ns + typeRef.TypeName;
    }
}

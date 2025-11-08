using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render.Analysis;

/// <summary>
/// Phase 3: Unified covariance conflict detection for interfaces AND base classes.
///
/// Detects member signature conflicts between a concrete type and:
/// 1. Its implemented interfaces
/// 2. Its base class (including "new/hides" and overload incompatibilities)
///
/// When conflicts are found, produces structured data about which members conflict
/// and with what sources (base vs interfaces). This enables emission of:
/// - Explicit interface views (As_InterfaceName)
/// - Base class views (As_BaseName)
/// - Domain view interfaces ($DomainView) for suppressed derived members
///
/// Uses normalized TypeReference-based signature comparison (no heuristics).
/// </summary>
public static class CovarianceConflictPartitioner
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
                ? PartitionConflicts(type, globalTypeLookup, ctx)
                : type
        ).ToList();

        return model with { Types = updatedTypes };
    }

    private static TypeModel PartitionConflicts(TypeModel type, Dictionary<string, TypeModel> typeLookup, AnalysisContext ctx)
    {
        // Collect all required signatures from base and interfaces
        var baseSignatures = CollectBaseSignatures(type, typeLookup, ctx);
        var interfaceSignatures = CollectInterfaceSignatures(type, typeLookup, ctx);
        var concreteSignatures = CollectConcreteSignatures(type, ctx);

        // Detect conflicts by comparing normalized signatures
        var conflictingInterfaces = new List<TypeReference>();
        var hasBaseConflicts = false;
        var memberConflicts = new Dictionary<string, bool>(); // member name → has conflict

        // Check interface conflicts
        foreach (var (interfaceRef, signatures) in interfaceSignatures)
        {
            foreach (var (memberName, requiredSigs) in signatures)
            {
                if (!concreteSignatures.TryGetValue(memberName, out var concreteSigs))
                    continue; // Missing member (handled by ExplicitInterfaceImplementation)

                // Check if all required signatures are present in concrete
                if (!AllSignaturesMatch(requiredSigs, concreteSigs))
                {
                    if (!conflictingInterfaces.Contains(interfaceRef))
                        conflictingInterfaces.Add(interfaceRef);
                    memberConflicts[memberName] = true;
                }
            }
        }

        // Check base class conflicts
        if (type.BaseType != null && baseSignatures.Count > 0)
        {
            foreach (var (memberName, baseSigs) in baseSignatures)
            {
                if (!concreteSignatures.TryGetValue(memberName, out var concreteSigs))
                    continue; // Member only in base (inherited, no conflict)

                // Check if concrete signatures conflict with base
                if (!AllSignaturesMatch(baseSigs, concreteSigs))
                {
                    hasBaseConflicts = true;
                    memberConflicts[memberName] = true;
                }
            }
        }

        // Update type model if conflicts found
        if (conflictingInterfaces.Count > 0 || hasBaseConflicts)
        {
            return type with
            {
                ConflictingInterfaces = conflictingInterfaces,
                HasBaseClassConflicts = hasBaseConflicts,
                ConflictingMemberNames = memberConflicts.Keys.ToList()
            };
        }

        return type;
    }

    /// <summary>
    /// Collects normalized signatures from the base class hierarchy.
    /// Implicit bases (ValueType, Enum, MulticastDelegate) are now set in Phase 3 Transform.
    /// </summary>
    private static Dictionary<string, List<NormalizedSignature>> CollectBaseSignatures(
        TypeModel type,
        Dictionary<string, TypeModel> typeLookup,
        AnalysisContext ctx)
    {
        var result = new Dictionary<string, List<NormalizedSignature>>();

        if (type.BaseType == null)
            return result;

        var baseType = FindTypeModel(type.BaseType, typeLookup);
        if (baseType == null)
            return result;

        // Build substitution map for base generic parameters
        var substitutions = GenericSubstitution.BuildSubstitutionMap(type.BaseType, baseType.GenericParameters);

        // Collect property signatures
        foreach (var prop in baseType.Members.Properties)
        {
            var memberName = ctx.GetPropertyIdentifier(prop);
            if (!result.ContainsKey(memberName))
                result[memberName] = new List<NormalizedSignature>();

            // Getter signature
            var getterSig = NormalizePropertyGetter(prop, substitutions, ctx);
            result[memberName].Add(getterSig);

            // Setter signature (if not readonly)
            if (!prop.IsReadonly)
            {
                var setterSig = NormalizePropertySetter(prop, substitutions, ctx);
                result[memberName].Add(setterSig);
            }
        }

        // Collect method signatures
        foreach (var method in baseType.Members.Methods)
        {
            var memberName = ctx.GetMethodIdentifier(method);
            if (!result.ContainsKey(memberName))
                result[memberName] = new List<NormalizedSignature>();

            var sig = NormalizeMethod(method, substitutions, ctx);
            result[memberName].Add(sig);
        }

        return result;
    }

    /// <summary>
    /// Collects normalized signatures from all implemented interfaces.
    /// Returns: interface TypeReference → (member name → signatures)
    /// </summary>
    private static Dictionary<TypeReference, Dictionary<string, List<NormalizedSignature>>> CollectInterfaceSignatures(
        TypeModel type,
        Dictionary<string, TypeModel> typeLookup,
        AnalysisContext ctx)
    {
        var result = new Dictionary<TypeReference, Dictionary<string, List<NormalizedSignature>>>();

        foreach (var interfaceRef in type.Implements)
        {
            var interfaceType = FindTypeModel(interfaceRef, typeLookup);
            if (interfaceType == null)
                continue;

            var signatures = new Dictionary<string, List<NormalizedSignature>>();

            // Build substitution map
            var substitutions = GenericSubstitution.BuildSubstitutionMap(interfaceRef, interfaceType.GenericParameters);

            // Collect property signatures
            foreach (var prop in interfaceType.Members.Properties)
            {
                var memberName = ctx.GetPropertyIdentifier(prop);
                if (!signatures.ContainsKey(memberName))
                    signatures[memberName] = new List<NormalizedSignature>();

                var getterSig = NormalizePropertyGetter(prop, substitutions, ctx);
                signatures[memberName].Add(getterSig);

                if (!prop.IsReadonly)
                {
                    var setterSig = NormalizePropertySetter(prop, substitutions, ctx);
                    signatures[memberName].Add(setterSig);
                }
            }

            // Collect method signatures
            foreach (var method in interfaceType.Members.Methods)
            {
                var memberName = ctx.GetMethodIdentifier(method);
                if (!signatures.ContainsKey(memberName))
                    signatures[memberName] = new List<NormalizedSignature>();

                var sig = NormalizeMethod(method, substitutions, ctx);
                signatures[memberName].Add(sig);
            }

            result[interfaceRef] = signatures;
        }

        return result;
    }

    /// <summary>
    /// Collects normalized signatures from the concrete type itself.
    /// Includes properties, fields (which emit as methods), and methods.
    /// </summary>
    private static Dictionary<string, List<NormalizedSignature>> CollectConcreteSignatures(
        TypeModel type,
        AnalysisContext ctx)
    {
        var result = new Dictionary<string, List<NormalizedSignature>>();
        var noSubstitutions = new Dictionary<string, TypeReference>(); // Empty for concrete type

        // Properties
        foreach (var prop in type.Members.Properties)
        {
            var memberName = ctx.GetPropertyIdentifier(prop);
            if (!result.ContainsKey(memberName))
                result[memberName] = new List<NormalizedSignature>();

            result[memberName].Add(NormalizePropertyGetter(prop, noSubstitutions, ctx));
            if (!prop.IsReadonly)
                result[memberName].Add(NormalizePropertySetter(prop, noSubstitutions, ctx));
        }

        // Fields (emitted as getter/setter methods, treated like properties)
        foreach (var field in type.Members.Fields)
        {
            var memberName = ctx.GetFieldIdentifier(field);
            if (!result.ContainsKey(memberName))
                result[memberName] = new List<NormalizedSignature>();

            result[memberName].Add(NormalizeFieldGetter(field, noSubstitutions, ctx));
            if (!field.IsReadonly)
                result[memberName].Add(NormalizeFieldSetter(field, noSubstitutions, ctx));
        }

        // Methods
        foreach (var method in type.Members.Methods)
        {
            var memberName = ctx.GetMethodIdentifier(method);
            if (!result.ContainsKey(memberName))
                result[memberName] = new List<NormalizedSignature>();

            result[memberName].Add(NormalizeMethod(method, noSubstitutions, ctx));
        }

        return result;
    }

    /// <summary>
    /// Checks if all required signatures are present (exactly) in the concrete signatures.
    /// </summary>
    private static bool AllSignaturesMatch(List<NormalizedSignature> required, List<NormalizedSignature> concrete)
    {
        foreach (var reqSig in required)
        {
            // Check if any concrete signature matches exactly
            if (!concrete.Any(concSig => SignaturesEqual(reqSig, concSig)))
                return false;
        }
        return true;
    }

    private static bool SignaturesEqual(NormalizedSignature a, NormalizedSignature b)
    {
        if (a.MemberName != b.MemberName)
            return false;

        if (a.ParameterTypes.Count != b.ParameterTypes.Count)
            return false;

        for (int i = 0; i < a.ParameterTypes.Count; i++)
        {
            if (a.ParameterTypes[i] != b.ParameterTypes[i])
                return false;
        }

        return a.ReturnType == b.ReturnType;
    }

    private static NormalizedSignature NormalizePropertyGetter(
        PropertyModel prop,
        Dictionary<string, TypeReference> substitutions,
        AnalysisContext ctx)
    {
        var returnType = GenericSubstitution.SubstituteType(prop.Type, substitutions);
        return new NormalizedSignature(
            ctx.GetPropertyIdentifier(prop),
            new List<string>(), // Getter has no parameters
            NormalizeTypeReference(returnType)
        );
    }

    private static NormalizedSignature NormalizePropertySetter(
        PropertyModel prop,
        Dictionary<string, TypeReference> substitutions,
        AnalysisContext ctx)
    {
        var paramType = GenericSubstitution.SubstituteType(prop.Type, substitutions);
        return new NormalizedSignature(
            ctx.GetPropertyIdentifier(prop),
            new List<string> { NormalizeTypeReference(paramType) },
            "System.Void" // Setter returns void
        );
    }

    private static NormalizedSignature NormalizeFieldGetter(
        FieldModel field,
        Dictionary<string, TypeReference> substitutions,
        AnalysisContext ctx)
    {
        var returnType = GenericSubstitution.SubstituteType(field.Type, substitutions);
        return new NormalizedSignature(
            ctx.GetFieldIdentifier(field),
            new List<string>(), // Getter has no parameters
            NormalizeTypeReference(returnType)
        );
    }

    private static NormalizedSignature NormalizeFieldSetter(
        FieldModel field,
        Dictionary<string, TypeReference> substitutions,
        AnalysisContext ctx)
    {
        var paramType = GenericSubstitution.SubstituteType(field.Type, substitutions);
        return new NormalizedSignature(
            ctx.GetFieldIdentifier(field),
            new List<string> { NormalizeTypeReference(paramType) },
            "System.Void" // Setter returns void
        );
    }

    private static NormalizedSignature NormalizeMethod(
        MethodModel method,
        Dictionary<string, TypeReference> substitutions,
        AnalysisContext ctx)
    {
        var returnType = GenericSubstitution.SubstituteType(method.ReturnType, substitutions);
        var paramTypes = method.Parameters
            .Select(p => GenericSubstitution.SubstituteType(p.Type, substitutions))
            .Select(NormalizeTypeReference)
            .ToList();

        return new NormalizedSignature(
            ctx.GetMethodIdentifier(method),
            paramTypes,
            NormalizeTypeReference(returnType)
        );
    }

    /// <summary>
    /// Normalizes a TypeReference to a stable string for comparison.
    /// Format: "Namespace.TypeName<GenericArg1, GenericArg2>"
    /// </summary>
    private static string NormalizeTypeReference(TypeReference typeRef)
    {
        var ns = typeRef.Namespace ?? "";
        var name = typeRef.TypeName;

        if (typeRef.GenericArgs.Count > 0)
        {
            var genericArgs = string.Join(", ", typeRef.GenericArgs.Select(NormalizeTypeReference));
            return $"{ns}.{name}<{genericArgs}>";
        }

        return ns.Length > 0 ? $"{ns}.{name}" : name;
    }

    private static TypeModel? FindTypeModel(TypeReference typeRef, Dictionary<string, TypeModel> typeLookup)
    {
        var key = GetTypeKey(typeRef);
        typeLookup.TryGetValue(key, out var type);
        return type;
    }

    private static string GetTypeKey(TypeReference typeRef)
    {
        var ns = typeRef.Namespace != null ? typeRef.Namespace + "." : "";
        return ns + typeRef.TypeName;
    }
}

/// <summary>
/// Normalized method/property signature for comparison.
/// Uses TypeReference-based normalization (no string parsing).
/// </summary>
internal sealed record NormalizedSignature(
    string MemberName,
    IReadOnlyList<string> ParameterTypes,
    string ReturnType);

using System.Collections.Generic;
using System.Linq;
using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;
using tsbindgen.SinglePhase.Normalize;
using tsbindgen.SinglePhase.Renaming;

namespace tsbindgen.SinglePhase.Emit;

/// <summary>
/// Provides on-demand access to V2 binding exposures for types.
/// Generates and caches ExposedMethods/ExposedProperties for use by emitters.
/// This allows emitters to access complete overload sets (own + inherited members).
/// </summary>
public sealed class BindingsProvider
{
    private readonly BuildContext _ctx;
    private readonly SymbolGraph _graph;
    private readonly Dictionary<string, TypeBindingCache> _cache = new();

    public BindingsProvider(BuildContext ctx, SymbolGraph graph)
    {
        _ctx = ctx;
        _graph = graph;
    }

    /// <summary>
    /// Get all exposed methods for a type (own + inherited).
    /// Returns null if type has no methods to expose.
    /// </summary>
    public List<MethodExposureInfo>? GetExposedMethods(TypeSymbol type)
    {
        var binding = GetOrCreateBinding(type);
        return binding.ExposedMethods;
    }

    /// <summary>
    /// Get all exposed properties for a type (own + inherited).
    /// Returns null if type has no properties to expose.
    /// </summary>
    public List<PropertyExposureInfo>? GetExposedProperties(TypeSymbol type)
    {
        var binding = GetOrCreateBinding(type);
        return binding.ExposedProperties;
    }

    private TypeBindingCache GetOrCreateBinding(TypeSymbol type)
    {
        if (_cache.TryGetValue(type.ClrFullName, out var cached))
            return cached;

        var binding = GenerateBinding(type);
        _cache[type.ClrFullName] = binding;
        return binding;
    }

    private TypeBindingCache GenerateBinding(TypeSymbol type)
    {
        // Collect instance methods (own + inherited)
        var exposedMethods = CollectExposedMethods(type);

        // Collect instance properties (own + inherited)
        var exposedProperties = CollectExposedProperties(type);

        return new TypeBindingCache
        {
            ExposedMethods = exposedMethods.Any() ? exposedMethods : null,
            ExposedProperties = exposedProperties.Any() ? exposedProperties : null
        };
    }

    private List<MethodExposureInfo> CollectExposedMethods(TypeSymbol type)
    {
        var exposures = new List<MethodExposureInfo>();

        // Add type's own methods (ONLY ClassSurface - ViewOnly methods are emitted separately)
        foreach (var method in type.Members.Methods.Where(m => m.EmitScope == EmitScope.ClassSurface && !m.IsStatic))
        {
            var tsName = GetMethodTsName(method, type);
            var tsSignatureId = SignatureNormalization.NormalizeMethod(method);

            exposures.Add(new MethodExposureInfo
            {
                Method = method,
                TsName = tsName,
                TsSignatureId = tsSignatureId,
                DeclaringType = type,
                IsInherited = false
            });
        }

        // Collect inherited methods from base classes
        CollectInheritedMethods(type, type, exposures);

        // Explicit override-wins deduplication
        // Group by (ClrName, TsSignatureId, IsStatic) and ensure only one exposure per signature
        var deduplicated = DeduplicateMethodExposures(type, exposures);

        return deduplicated;
    }

    /// <summary>
    /// Phase 1.1: Explicit override-wins deduplication for method exposures.
    /// Groups by (ClrName, TsSignatureId, IsStatic) and ensures only one exposure per signature.
    /// Derived type's version wins over inherited versions.
    /// </summary>
    private List<MethodExposureInfo> DeduplicateMethodExposures(TypeSymbol type, List<MethodExposureInfo> exposures)
    {
        var deduplicated = new List<MethodExposureInfo>();

        // Group by (ClrName, TsSignatureId, IsStatic)
        var groups = exposures
            .GroupBy(e => new
            {
                ClrName = e.Method.ClrName,
                e.TsSignatureId,
                IsStatic = e.Method.IsStatic
            });

        foreach (var group in groups)
        {
            var candidates = group.ToList();

            // PHASEGATE: Assert at most one non-inherited (own) method per signature
            var ownMethods = candidates.Where(e => !e.IsInherited).ToList();
            if (ownMethods.Count > 1)
            {
                var details = new System.Text.StringBuilder();
                details.AppendLine($"PHASEGATE VIOLATION: Type {type.ClrFullName} has multiple own methods with same signature:");
                details.AppendLine($"  CLR Name: {group.Key.ClrName}");
                details.AppendLine($"  Normalized Signature: {group.Key.TsSignatureId}");
                details.AppendLine($"  Count: {ownMethods.Count}");
                details.AppendLine($"  Method Details:");
                for (int i = 0; i < ownMethods.Count; i++)
                {
                    var m = ownMethods[i].Method;
                    details.AppendLine($"    Method {i + 1}:");
                    details.AppendLine($"      StableId: {m.StableId}");
                    details.AppendLine($"      EmitScope: {m.EmitScope}");
                    details.AppendLine($"      IsInherited (from exposure): {ownMethods[i].IsInherited}");
                    details.AppendLine($"      Parameters:");
                    for (int j = 0; j < m.Parameters.Length; j++)
                    {
                        var p = m.Parameters[j];
                        details.AppendLine($"        {j}: {p.Name}");
                        details.AppendLine($"           Type: {p.Type.GetType().Name}");
                        if (p.Type is Model.Types.NamedTypeReference ntr)
                        {
                            details.AppendLine($"           FullName: {ntr.FullName}");
                            details.AppendLine($"           Arity: {ntr.Arity}");
                            details.AppendLine($"           TypeArguments.Count: {ntr.TypeArguments.Count}");
                            for (int k = 0; k < ntr.TypeArguments.Count; k++)
                            {
                                var ta = ntr.TypeArguments[k];
                                details.AppendLine($"             TypeArg[{k}]: {ta.GetType().Name} - {ta.ToString()}");
                            }
                        }
                    }
                    details.AppendLine($"      Return Type: {m.ReturnType.ToString()}");
                    details.AppendLine($"      Generic Arity: {m.Arity}");
                }
                details.AppendLine($"  This indicates a bug in signature normalization or method collection.");
                throw new System.InvalidOperationException(details.ToString());
            }

            // Override-wins: Prefer own method over inherited
            MethodExposureInfo winner;
            if (ownMethods.Count == 1)
            {
                // Derived type's version wins
                winner = ownMethods[0];

                // If overriding abstract base method, use base's TsName
                // When Renamer adds numeric suffixes due to ViewOnly collisions (equals -> equals3),
                // we must use the base abstract method's TsName so TypeScript sees the implementation
                var inheritedMethods = candidates.Where(e => e.IsInherited).ToList();
                var abstractBase = inheritedMethods.FirstOrDefault(e => e.Method.IsAbstract);
                if (abstractBase != null)
                {
                    // Use base's TsName for the override (e.g., "equals" not "equals3")
                    winner = new MethodExposureInfo
                    {
                        Method = winner.Method,
                        TsName = abstractBase.TsName,  // ← Use base's name
                        TsSignatureId = winner.TsSignatureId,
                        DeclaringType = winner.DeclaringType,
                        IsInherited = winner.IsInherited
                    };
                }
            }
            else
            {
                // All inherited - take first one (from most derived base class)
                winner = candidates[0];
            }

            deduplicated.Add(winner);
        }

        return deduplicated;
    }

    private void CollectInheritedMethods(TypeSymbol derivedType, TypeSymbol currentType, List<MethodExposureInfo> exposures)
    {
        // Get base type
        if (currentType.BaseType == null) return;

        var baseTypeRef = currentType.BaseType as Model.Types.NamedTypeReference;
        if (baseTypeRef == null) return;

        // Resolve base type from graph
        if (!_graph.TryGetType(baseTypeRef.FullName, out var baseType) || baseType == null)
            return;

        // Cross-namespace inheritance now enabled
        // Import planning (Phase 3.2) will handle types from other namespaces

        // Add ALL inherited ClassSurface methods (deduplication happens in DeduplicateMethodExposures)
        // ViewOnly methods are emitted separately and shouldn't be part of exposures
        foreach (var baseMethod in baseType.Members.Methods.Where(m => m.EmitScope == EmitScope.ClassSurface && !m.IsStatic))
        {
            var baseSignature = SignatureNormalization.NormalizeMethod(baseMethod);

            // Use base type's scope for TS name (where method was declared and renamed)
            var tsName = GetMethodTsName(baseMethod, baseType);

            exposures.Add(new MethodExposureInfo
            {
                Method = baseMethod,
                TsName = tsName,
                TsSignatureId = baseSignature,
                DeclaringType = baseType,
                IsInherited = true
            });
        }

        // Recursively collect from base's base
        CollectInheritedMethods(derivedType, baseType, exposures);
    }

    private List<PropertyExposureInfo> CollectExposedProperties(TypeSymbol type)
    {
        var exposures = new List<PropertyExposureInfo>();

        // Add type's own properties (ONLY ClassSurface - ViewOnly properties are emitted separately)
        foreach (var property in type.Members.Properties.Where(p => p.EmitScope == EmitScope.ClassSurface && !p.IsStatic))
        {
            var tsName = GetPropertyTsName(property, type);
            var tsSignatureId = SignatureNormalization.NormalizeProperty(property);

            exposures.Add(new PropertyExposureInfo
            {
                Property = property,
                TsName = tsName,
                TsSignatureId = tsSignatureId,
                DeclaringType = type,
                IsInherited = false
            });
        }

        // Collect inherited properties from base classes
        CollectInheritedProperties(type, type, exposures);

        // Explicit override-wins deduplication
        // Group by (ClrName, TsSignatureId, IsStatic) and ensure only one exposure per signature
        var deduplicated = DeduplicatePropertyExposures(type, exposures);

        return deduplicated;
    }

    /// <summary>
    /// Phase 1.1: Explicit override-wins deduplication for property exposures.
    /// Groups by (ClrName, TsSignatureId, IsStatic) and ensures only one exposure per signature.
    /// Derived type's version wins over inherited versions.
    /// </summary>
    private List<PropertyExposureInfo> DeduplicatePropertyExposures(TypeSymbol type, List<PropertyExposureInfo> exposures)
    {
        var deduplicated = new List<PropertyExposureInfo>();

        // Group by (ClrName, TsSignatureId, IsStatic)
        var groups = exposures
            .GroupBy(e => new
            {
                ClrName = e.Property.ClrName,
                e.TsSignatureId,
                IsStatic = e.Property.IsStatic
            });

        foreach (var group in groups)
        {
            var candidates = group.ToList();

            // PHASEGATE: Assert at most one non-inherited (own) property per signature
            var ownProperties = candidates.Where(e => !e.IsInherited).ToList();
            if (ownProperties.Count > 1)
            {
                throw new System.InvalidOperationException(
                    $"PHASEGATE VIOLATION: Type {type.ClrFullName} has multiple own properties with same signature:\n" +
                    $"  CLR Name: {group.Key.ClrName}\n" +
                    $"  Signature: {group.Key.TsSignatureId}\n" +
                    $"  Count: {ownProperties.Count}\n" +
                    $"  This indicates a bug in signature normalization or property collection.");
            }

            // Override-wins: Prefer own property over inherited
            PropertyExposureInfo winner;
            if (ownProperties.Count == 1)
            {
                // Derived type's version wins
                winner = ownProperties[0];

                // If overriding abstract base property, use base's TsName
                // When Renamer adds numeric suffixes due to ViewOnly collisions,
                // we must use the base abstract property's TsName so TypeScript sees the implementation
                var inheritedProperties = candidates.Where(e => e.IsInherited).ToList();
                var abstractBase = inheritedProperties.FirstOrDefault(e => e.Property.IsAbstract);
                if (abstractBase != null)
                {
                    // Use base's TsName for the override
                    winner = new PropertyExposureInfo
                    {
                        Property = winner.Property,
                        TsName = abstractBase.TsName,  // ← Use base's name
                        TsSignatureId = winner.TsSignatureId,
                        DeclaringType = winner.DeclaringType,
                        IsInherited = winner.IsInherited
                    };
                }
            }
            else
            {
                // All inherited - take first one (from most derived base class)
                winner = candidates[0];
            }

            deduplicated.Add(winner);
        }

        return deduplicated;
    }

    private void CollectInheritedProperties(TypeSymbol derivedType, TypeSymbol currentType, List<PropertyExposureInfo> exposures)
    {
        // Get base type
        if (currentType.BaseType == null) return;

        var baseTypeRef = currentType.BaseType as Model.Types.NamedTypeReference;
        if (baseTypeRef == null) return;

        // Resolve base type from graph
        if (!_graph.TryGetType(baseTypeRef.FullName, out var baseType) || baseType == null)
            return;

        // Cross-namespace inheritance now enabled
        // Import planning (Phase 3.2) will handle types from other namespaces

        // Add ALL inherited ClassSurface properties (deduplication happens in DeduplicatePropertyExposures)
        // ViewOnly properties are emitted separately and shouldn't be part of exposures
        foreach (var baseProperty in baseType.Members.Properties.Where(p => p.EmitScope == EmitScope.ClassSurface && !p.IsStatic))
        {
            var baseSignature = SignatureNormalization.NormalizeProperty(baseProperty);

            // Use base type's scope for TS name (where property was declared and renamed)
            var tsName = GetPropertyTsName(baseProperty, baseType);

            exposures.Add(new PropertyExposureInfo
            {
                Property = baseProperty,
                TsName = tsName,
                TsSignatureId = baseSignature,
                DeclaringType = baseType,
                IsInherited = true
            });
        }

        // Recursively collect from base's base
        CollectInheritedProperties(derivedType, baseType, exposures);
    }

    private string GetMethodTsName(Model.Symbols.MemberSymbols.MethodSymbol method, TypeSymbol declaringType)
    {
        // Get TS name from declaring type's scope
        if (method.EmitScope == EmitScope.ViewOnly && method.SourceInterface != null)
        {
            var interfaceStableId = ScopeFactory.GetInterfaceStableId(method.SourceInterface);
            var viewScope = ScopeFactory.ViewSurface(declaringType, interfaceStableId, method.IsStatic);
            return _ctx.Renamer.GetFinalMemberName(method.StableId, viewScope);
        }
        else
        {
            var classScope = ScopeFactory.ClassSurface(declaringType, method.IsStatic);
            return _ctx.Renamer.GetFinalMemberName(method.StableId, classScope);
        }
    }

    private string GetPropertyTsName(Model.Symbols.MemberSymbols.PropertySymbol property, TypeSymbol declaringType)
    {
        // Get TS name from declaring type's scope
        if (property.EmitScope == EmitScope.ViewOnly && property.SourceInterface != null)
        {
            var interfaceStableId = ScopeFactory.GetInterfaceStableId(property.SourceInterface);
            var viewScope = ScopeFactory.ViewSurface(declaringType, interfaceStableId, property.IsStatic);
            return _ctx.Renamer.GetFinalMemberName(property.StableId, viewScope);
        }
        else
        {
            var classScope = ScopeFactory.ClassSurface(declaringType, property.IsStatic);
            return _ctx.Renamer.GetFinalMemberName(property.StableId, classScope);
        }
    }
}

/// <summary>
/// Cached binding information for a type.
/// </summary>
public sealed class TypeBindingCache
{
    public List<MethodExposureInfo>? ExposedMethods { get; init; }
    public List<PropertyExposureInfo>? ExposedProperties { get; init; }
}

/// <summary>
/// Information about an exposed method (own or inherited).
/// </summary>
public sealed class MethodExposureInfo
{
    public required Model.Symbols.MemberSymbols.MethodSymbol Method { get; init; }
    public required string TsName { get; init; }
    public required string TsSignatureId { get; init; }
    public required TypeSymbol DeclaringType { get; init; }
    public required bool IsInherited { get; init; }
}

/// <summary>
/// Information about an exposed property (own or inherited).
/// </summary>
public sealed class PropertyExposureInfo
{
    public required Model.Symbols.MemberSymbols.PropertySymbol Property { get; init; }
    public required string TsName { get; init; }
    public required string TsSignatureId { get; init; }
    public required TypeSymbol DeclaringType { get; init; }
    public required bool IsInherited { get; init; }
}

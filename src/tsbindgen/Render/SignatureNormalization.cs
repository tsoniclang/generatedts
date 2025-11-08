using tsbindgen.Config;
using tsbindgen.Render;
using tsbindgen.Snapshot;

namespace tsbindgen.Render;

/// <summary>
/// Centralized utility for generating normalized method and property signatures.
/// Used for bindings, metadata, and collision detection.
/// </summary>
public static class SignatureNormalization
{
    /// <summary>
    /// Gets a fully normalized method signature including return type, parameter kinds, and flags.
    /// Format: "methodName<T,U>(ref:string:req:noparams,out:int:opt:params)|ret:boolean|static=false"
    /// </summary>
    public static string GetNormalizedSignature(MethodModel method, AnalysisContext ctx)
    {
        // Build parameter signatures with full information
        var paramSignatures = string.Join(",", method.Parameters.Select(p =>
        {
            var typeStr = NormalizeTypeReference(p.Type);
            var kindStr = p.Kind.ToString().ToLowerInvariant();
            var optionalStr = p.IsOptional ? "opt" : "req";
            var paramsStr = p.IsParams ? "params" : "noparams";
            return $"{kindStr}:{typeStr}:{optionalStr}:{paramsStr}";
        }));

        // Include generic arity in the signature
        var genericArity = method.GenericParameters.Count > 0
            ? $"<{string.Join(",", method.GenericParameters.Select(g => g.Name))}>"
            : "";

        // Include return type
        var returnType = NormalizeTypeReference(method.ReturnType);

        // Include static flag
        var staticFlag = method.IsStatic ? "|static=true" : "|static=false";

        // Get the TypeScript-safe method name
        var methodName = ctx.GetMethodIdentifier(method);

        return $"{methodName}{genericArity}({paramSignatures})|ret:{returnType}{staticFlag}";
    }

    /// <summary>
    /// Gets a normalized signature for a property getter.
    /// Format: "get_PropertyName()|ret:string|static=false"
    /// </summary>
    public static string GetPropertyGetterSignature(PropertyModel property, AnalysisContext ctx)
    {
        var returnType = NormalizeTypeReference(property.Type);
        var staticFlag = property.IsStatic ? "|static=true" : "|static=false";
        var propertyName = ctx.GetPropertyIdentifier(property);

        return $"get_{propertyName}()|ret:{returnType}{staticFlag}";
    }

    /// <summary>
    /// Gets a normalized signature for a property setter.
    /// Format: "set_PropertyName(value:string:req:noparams)|ret:void|static=false"
    /// </summary>
    public static string GetPropertySetterSignature(PropertyModel property, AnalysisContext ctx)
    {
        var paramType = NormalizeTypeReference(property.Type);
        var staticFlag = property.IsStatic ? "|static=true" : "|static=false";
        var propertyName = ctx.GetPropertyIdentifier(property);

        return $"set_{propertyName}(value:{paramType}:req:noparams)|ret:void{staticFlag}";
    }

    /// <summary>
    /// Normalizes a TypeReference to a canonical string for comparison.
    /// Includes namespace, type name, generic arguments, array rank, and pointer depth.
    /// </summary>
    public static string NormalizeTypeReference(TypeReference typeRef)
    {
        var genericArgs = typeRef.GenericArgs.Count > 0
            ? $"<{string.Join(",", typeRef.GenericArgs.Select(NormalizeTypeReference))}>"
            : "";

        var array = typeRef.ArrayRank > 0 ? string.Concat(Enumerable.Repeat("[]", typeRef.ArrayRank)) : "";
        var pointer = typeRef.PointerDepth > 0 ? new string('*', typeRef.PointerDepth) : "";

        var namespacePart = string.IsNullOrEmpty(typeRef.Namespace) ? "" : $"{typeRef.Namespace}.";
        return $"{namespacePart}{typeRef.TypeName}{genericArgs}{array}{pointer}";
    }
}
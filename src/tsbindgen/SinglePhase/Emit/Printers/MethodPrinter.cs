using System.Text;
using tsbindgen.SinglePhase.Model;
using tsbindgen.SinglePhase.Model.Symbols;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;
using tsbindgen.SinglePhase.Renaming;

namespace tsbindgen.SinglePhase.Emit.Printers;

/// <summary>
/// Prints TypeScript method signatures from MethodSymbol.
/// Handles generic methods, parameters, return types, and modifiers.
/// </summary>
public static class MethodPrinter
{
    /// <summary>
    /// Print a method signature to TypeScript.
    /// </summary>
    public static string Print(MethodSymbol method, TypeSymbol declaringType, TypeNameResolver resolver, BuildContext ctx)
    {
        var sb = new StringBuilder();

        // Get the final TS name from Renamer using correct scope
        var scope = ScopeFactory.ClassSurface(declaringType, method.IsStatic);
        var finalName = ctx.Renamer.GetFinalMemberName(method.StableId, scope);

        // Modifiers
        // IMPORTANT: Don't emit static/abstract modifiers for interface members
        // - TypeScript interfaces don't support static members (C# 11 feature)
        // - TypeScript interface methods are implicitly abstract
        var isInterface = declaringType.Kind == TypeKind.Interface;

        if (method.IsStatic && !isInterface)
            sb.Append("static ");

        if (method.IsAbstract && !isInterface)
            sb.Append("abstract ");

        // Method name
        sb.Append(finalName);

        // Generic parameters: <T, U>
        if (method.GenericParameters.Length > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", method.GenericParameters.Select(gp => PrintGenericParameter(gp, resolver, ctx))));
            sb.Append('>');
        }

        // Parameters: (a: int, b: string)
        sb.Append('(');
        sb.Append(string.Join(", ", method.Parameters.Select(p => PrintParameter(p, resolver, ctx))));
        sb.Append(')');

        // Return type: : int
        sb.Append(": ");
        sb.Append(TypeRefPrinter.Print(method.ReturnType, resolver, ctx));

        return sb.ToString();
    }

    private static string PrintGenericParameter(GenericParameterSymbol gp, TypeNameResolver resolver, BuildContext ctx)
    {
        var sb = new StringBuilder();
        sb.Append(gp.Name);

        // Print constraints from the IReadOnlyList<TypeReference>
        if (gp.Constraints.Length > 0)
        {
            sb.Append(" extends ");

            // If multiple constraints, use intersection type
            if (gp.Constraints.Length == 1)
            {
                sb.Append(TypeRefPrinter.Print(gp.Constraints[0], resolver, ctx));
            }
            else
            {
                // Multiple constraints: T extends IFoo & IBar
                var constraints = gp.Constraints.Select(c => TypeRefPrinter.Print(c, resolver, ctx));
                sb.Append(string.Join(" & ", constraints));
            }
        }

        return sb.ToString();
    }

    private static string PrintParameter(ParameterSymbol param, TypeNameResolver resolver, BuildContext ctx)
    {
        var sb = new StringBuilder();

        // Parameter name
        sb.Append(param.Name);

        // Optional parameter: name?
        if (param.HasDefaultValue)
            sb.Append('?');

        // Parameter type: name: int
        sb.Append(": ");

        // Handle ref/out parameters
        if (param.IsOut || param.IsRef)
        {
            // TypeScript has no ref/out
            // Map to { value: T } wrapper (metadata tracks original semantics)
            var innerType = TypeRefPrinter.Print(param.Type, resolver, ctx);
            sb.Append($"{{ value: {innerType} }}");
        }
        else if (param.IsParams)
        {
            // params T[] → ...args: T[]
            // Note: params keyword handled by caller (adds ... to parameter name)
            sb.Append(TypeRefPrinter.Print(param.Type, resolver, ctx));
        }
        else
        {
            sb.Append(TypeRefPrinter.Print(param.Type, resolver, ctx));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Print method with params array handling.
    /// Converts params T[] parameter to ...name: T[]
    /// </summary>
    public static string PrintWithParamsExpansion(MethodSymbol method, TypeSymbol declaringType, TypeNameResolver resolver, BuildContext ctx)
    {
        // Check if last parameter is params
        var hasParams = method.Parameters.Length > 0 && method.Parameters[^1].IsParams;

        if (!hasParams)
            return Print(method, declaringType, resolver, ctx);

        // Build method signature with params expansion
        var sb = new StringBuilder();

        // Get final name using correct scope
        var scope = ScopeFactory.ClassSurface(declaringType, method.IsStatic);
        var finalName = ctx.Renamer.GetFinalMemberName(method.StableId, scope);

        // Modifiers
        // IMPORTANT: Don't emit static/abstract modifiers for interface members
        var isInterface = declaringType.Kind == TypeKind.Interface;

        if (method.IsStatic && !isInterface)
            sb.Append("static ");

        if (method.IsAbstract && !isInterface)
            sb.Append("abstract ");

        // Method name
        sb.Append(finalName);

        // Generic parameters
        if (method.GenericParameters.Length > 0)
        {
            sb.Append('<');
            sb.Append(string.Join(", ", method.GenericParameters.Select(gp => PrintGenericParameter(gp, resolver, ctx))));
            sb.Append('>');
        }

        // Parameters with params expansion
        sb.Append('(');

        // Regular parameters
        if (method.Parameters.Length > 1)
        {
            var regularParams = method.Parameters.Take(method.Parameters.Length - 1);
            sb.Append(string.Join(", ", regularParams.Select(p => PrintParameter(p, resolver, ctx))));
            sb.Append(", ");
        }

        // Params parameter with ... prefix
        var paramsParam = method.Parameters[^1];
        sb.Append("...");
        sb.Append(paramsParam.Name);
        sb.Append(": ");
        sb.Append(TypeRefPrinter.Print(paramsParam.Type, resolver, ctx));

        sb.Append(')');

        // Return type
        sb.Append(": ");
        sb.Append(TypeRefPrinter.Print(method.ReturnType, resolver, ctx));

        return sb.ToString();
    }

    /// <summary>
    /// Print multiple method overloads.
    /// Used for methods with same name but different signatures.
    /// </summary>
    public static IEnumerable<string> PrintOverloads(IEnumerable<MethodSymbol> overloads, TypeSymbol declaringType, TypeNameResolver resolver, BuildContext ctx)
    {
        foreach (var method in overloads)
        {
            yield return Print(method, declaringType, resolver, ctx);
        }
    }

    /// <summary>
    /// Print method as a property getter/setter.
    /// Used for property accessors in interfaces.
    /// </summary>
    public static string PrintAsPropertyAccessor(MethodSymbol method, bool isGetter, TypeNameResolver resolver, BuildContext ctx)
    {
        var sb = new StringBuilder();

        // Get property name from method name (get_Foo → Foo)
        var propertyName = method.ClrName;
        if (propertyName.StartsWith("get_") || propertyName.StartsWith("set_"))
            propertyName = propertyName.Substring(4);

        // Modifiers
        if (method.IsStatic)
            sb.Append("static ");

        // Property name
        sb.Append(propertyName);

        // Type
        sb.Append(": ");

        if (isGetter)
        {
            // Getter returns the property type
            sb.Append(TypeRefPrinter.Print(method.ReturnType, resolver, ctx));
        }
        else
        {
            // Setter takes property type as parameter
            if (method.Parameters.Length > 0)
                sb.Append(TypeRefPrinter.Print(method.Parameters[0].Type, resolver, ctx));
            else
                sb.Append("any"); // Fallback
        }

        return sb.ToString();
    }
}

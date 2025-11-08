using System.Text;
using tsbindgen.SinglePhase.Model.Symbols.MemberSymbols;

namespace tsbindgen.SinglePhase.Normalize;

/// <summary>
/// Creates normalized, canonical signatures for complete member matching.
/// Includes: name, arity, parameter kinds, optionality, static flag, accessor type.
///
/// This is the SINGLE canonical format used across:
/// - BindingEmitter (bindings.json)
/// - MetadataEmitter (metadata.json)
/// - StructuralConformance (interface matching)
/// - ViewPlanner (member filtering)
/// </summary>
public static class SignatureNormalization
{
    /// <summary>
    /// Normalize a method signature to canonical form.
    /// Format: "MethodName|arity=N|(param1:kind,param2:kind)|->ReturnType|static=bool"
    /// Example: "CompareTo|arity=0|(T:in)|->int|static=false"
    /// </summary>
    public static string NormalizeMethod(MethodSymbol method)
    {
        var sb = new StringBuilder();

        // Method name
        sb.Append(method.ClrName);

        // Generic arity
        sb.Append("|arity=");
        sb.Append(method.Arity);

        // Parameters with kinds
        sb.Append("|(");
        for (int i = 0; i < method.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(',');

            var param = method.Parameters[i];
            sb.Append(NormalizeTypeName(param.Type.ToString() ?? "unknown"));
            sb.Append(':');

            // Parameter kind
            if (param.IsOut)
                sb.Append("out");
            else if (param.IsRef)
                sb.Append("ref");
            else if (param.IsParams)
                sb.Append("params");
            else
                sb.Append("in");

            // Optional flag
            if (param.HasDefaultValue)
                sb.Append("?");
        }
        sb.Append(')');

        // Return type
        sb.Append("|->");
        sb.Append(NormalizeTypeName(method.ReturnType.ToString() ?? "void"));

        // Static flag
        sb.Append("|static=");
        sb.Append(method.IsStatic ? "true" : "false");

        return sb.ToString();
    }

    /// <summary>
    /// Normalize a property signature to canonical form.
    /// Format: "PropertyName|(indexParam1,indexParam2)|->PropertyType|static=bool|accessor=get/set/getset"
    /// Example: "Count|->int|static=false|accessor=get"
    /// Example: "Item|(int)|->T|static=false|accessor=getset"
    /// </summary>
    public static string NormalizeProperty(PropertySymbol property)
    {
        var sb = new StringBuilder();

        // Property name
        sb.Append(property.ClrName);

        // Index parameters (for indexers)
        sb.Append('|');
        if (property.IndexParameters.Count > 0)
        {
            sb.Append('(');
            for (int i = 0; i < property.IndexParameters.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(NormalizeTypeName(property.IndexParameters[i].Type.ToString() ?? "unknown"));
            }
            sb.Append(')');
        }

        // Property type
        sb.Append("|->");
        sb.Append(NormalizeTypeName(property.PropertyType.ToString() ?? "unknown"));

        // Static flag
        sb.Append("|static=");
        sb.Append(property.IsStatic ? "true" : "false");

        // Accessor type
        sb.Append("|accessor=");
        if (property.HasGetter && property.HasSetter)
            sb.Append("getset");
        else if (property.HasGetter)
            sb.Append("get");
        else if (property.HasSetter)
            sb.Append("set");
        else
            sb.Append("none");

        return sb.ToString();
    }

    /// <summary>
    /// Normalize a field signature to canonical form.
    /// Format: "FieldName|->FieldType|static=bool|const=bool"
    /// Example: "MaxValue|->int|static=true|const=true"
    /// </summary>
    public static string NormalizeField(FieldSymbol field)
    {
        var sb = new StringBuilder();

        // Field name
        sb.Append(field.ClrName);

        // Field type
        sb.Append("|->");
        sb.Append(NormalizeTypeName(field.FieldType.ToString() ?? "unknown"));

        // Static flag
        sb.Append("|static=");
        sb.Append(field.IsStatic ? "true" : "false");

        // Const flag
        sb.Append("|const=");
        sb.Append(field.IsConst ? "true" : "false");

        return sb.ToString();
    }

    /// <summary>
    /// Normalize an event signature to canonical form.
    /// Format: "EventName|->DelegateType|static=bool"
    /// Example: "Click|->EventHandler|static=false"
    /// </summary>
    public static string NormalizeEvent(EventSymbol evt)
    {
        var sb = new StringBuilder();

        // Event name
        sb.Append(evt.ClrName);

        // Delegate type
        sb.Append("|->");
        sb.Append(NormalizeTypeName(evt.EventHandlerType.ToString() ?? "unknown"));

        // Static flag
        sb.Append("|static=");
        sb.Append(evt.IsStatic ? "true" : "false");

        return sb.ToString();
    }

    /// <summary>
    /// Normalize a constructor signature to canonical form.
    /// Format: "constructor|(param1:kind,param2:kind)|static=bool"
    /// Example: "constructor|(int:in,string:in)|static=false"
    /// </summary>
    public static string NormalizeConstructor(ConstructorSymbol ctor)
    {
        var sb = new StringBuilder();

        // Constructor keyword
        sb.Append("constructor");

        // Parameters with kinds
        sb.Append("|(");
        for (int i = 0; i < ctor.Parameters.Count; i++)
        {
            if (i > 0) sb.Append(',');

            var param = ctor.Parameters[i];
            sb.Append(NormalizeTypeName(param.Type.ToString() ?? "unknown"));
            sb.Append(':');

            // Parameter kind
            if (param.IsOut)
                sb.Append("out");
            else if (param.IsRef)
                sb.Append("ref");
            else if (param.IsParams)
                sb.Append("params");
            else
                sb.Append("in");

            // Optional flag
            if (param.HasDefaultValue)
                sb.Append("?");
        }
        sb.Append(')');

        // Static flag (static constructors exist)
        sb.Append("|static=");
        sb.Append(ctor.IsStatic ? "true" : "false");

        return sb.ToString();
    }

    /// <summary>
    /// Normalize a type name for signature matching.
    /// Handles generics, arrays, nullability, etc.
    /// </summary>
    private static string NormalizeTypeName(string typeName)
    {
        // Remove whitespace
        var normalized = typeName.Replace(" ", "");

        // Normalize generic backtick to underscore (List`1 -> List_1)
        normalized = normalized.Replace('`', '_');

        return normalized;
    }
}

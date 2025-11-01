using System.Reflection;
using System.Text;

namespace GenerateDts;

public sealed class TypeMapper
{
    private readonly List<string> _warnings = new();

    public IReadOnlyList<string> Warnings => _warnings;

    public string MapType(Type type)
    {
        // Handle ref/out parameters (ByRef types)
        if (type.IsByRef)
        {
            // TypeScript doesn't have ref/out, so just map the underlying type
            return MapType(type.GetElementType()!);
        }

        // Handle nullable value types
        if (Nullable.GetUnderlyingType(type) is Type underlyingType)
        {
            return $"{MapType(underlyingType)} | null";
        }

        // Handle arrays
        if (type.IsArray)
        {
            var elementType = type.GetElementType()!;
            return $"ReadonlyArray<{MapType(elementType)}>";
        }

        // Handle generic types
        if (type.IsGenericType)
        {
            return MapGenericType(type);
        }

        // Handle primitive types
        if (type.IsPrimitive || type == typeof(string) || type == typeof(void))
        {
            return MapPrimitiveType(type);
        }

        // Handle special types
        if (type.Namespace?.StartsWith("System") == true)
        {
            var mapped = MapSystemType(type);
            if (mapped != null)
            {
                return mapped;
            }
        }

        // Default: use fully qualified name
        return GetFullTypeName(type);
    }

    private string MapPrimitiveType(Type type)
    {
        return type switch
        {
            _ when type == typeof(void) => "void",
            _ when type == typeof(string) => "string",
            _ when type == typeof(bool) => "boolean",
            _ when type == typeof(double) => "double",
            _ when type == typeof(float) => "float",
            _ when type == typeof(int) => "int",
            _ when type == typeof(uint) => "uint",
            _ when type == typeof(long) => "long",
            _ when type == typeof(ulong) => "ulong",
            _ when type == typeof(short) => "short",
            _ when type == typeof(ushort) => "ushort",
            _ when type == typeof(byte) => "byte",
            _ when type == typeof(sbyte) => "sbyte",
            _ when type == typeof(decimal) => "decimal",
            _ => "number"
        };
    }

    private string? MapSystemType(Type type)
    {
        var fullName = type.FullName ?? type.Name;

        return fullName switch
        {
            "System.String" => "string",
            "System.Boolean" => "boolean",
            "System.Void" => "void",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Decimal" => "decimal",
            "System.Object" => "any",
            _ => null
        };
    }

    private string MapGenericType(Type type)
    {
        var genericTypeDef = type.GetGenericTypeDefinition();
        var fullName = genericTypeDef.FullName ?? genericTypeDef.Name;

        // Handle Task and Task<T>
        if (fullName.StartsWith("System.Threading.Tasks.Task"))
        {
            if (type.GenericTypeArguments.Length == 0)
            {
                return "Promise<void>";
            }
            else
            {
                var resultType = MapType(type.GenericTypeArguments[0]);
                return $"Promise<{resultType}>";
            }
        }

        // Handle List<T>
        if (fullName.StartsWith("System.Collections.Generic.List"))
        {
            var elementType = MapType(type.GenericTypeArguments[0]);
            return $"List<{elementType}>";
        }

        // Handle Dictionary<K,V>
        if (fullName.StartsWith("System.Collections.Generic.Dictionary"))
        {
            var keyType = MapType(type.GenericTypeArguments[0]);
            var valueType = MapType(type.GenericTypeArguments[1]);
            return $"Dictionary<{keyType}, {valueType}>";
        }

        // Handle HashSet<T>
        if (fullName.StartsWith("System.Collections.Generic.HashSet"))
        {
            var elementType = MapType(type.GenericTypeArguments[0]);
            return $"HashSet<{elementType}>";
        }

        // Handle IEnumerable<T> and similar
        if (fullName.StartsWith("System.Collections.Generic.IEnumerable") ||
            fullName.StartsWith("System.Collections.Generic.IReadOnlyList") ||
            fullName.StartsWith("System.Collections.Generic.IReadOnlyCollection"))
        {
            var elementType = MapType(type.GenericTypeArguments[0]);
            return $"ReadonlyArray<{elementType}>";
        }

        // Generic type with parameters
        var sb = new StringBuilder();
        sb.Append(GetFullTypeName(genericTypeDef));
        sb.Append('<');

        for (int i = 0; i < type.GenericTypeArguments.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(MapType(type.GenericTypeArguments[i]));
        }

        sb.Append('>');
        return sb.ToString();
    }

    public string GetFullTypeName(Type type)
    {
        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        if (type.IsGenericType)
        {
            var name = type.Name;
            var backtickIndex = name.IndexOf('`');
            if (backtickIndex > 0)
            {
                name = name.Substring(0, backtickIndex);
            }
            return type.Namespace != null ? $"{type.Namespace}.{name}" : name;
        }

        // Replace + with . for nested types (C# uses + but TypeScript uses .)
        var fullName = type.FullName ?? type.Name;
        return fullName.Replace('+', '.');
    }

    public void AddWarning(string message)
    {
        _warnings.Add(message);
    }
}

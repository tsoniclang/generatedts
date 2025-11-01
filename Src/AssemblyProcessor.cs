using System.Reflection;

namespace GenerateDts;

public sealed class AssemblyProcessor
{
    private readonly GeneratorConfig _config;
    private readonly HashSet<string>? _namespaceWhitelist;
    private readonly TypeMapper _typeMapper = new();

    public AssemblyProcessor(GeneratorConfig config, string[] namespaces)
    {
        _config = config;
        _namespaceWhitelist = namespaces.Length > 0
            ? new HashSet<string>(namespaces)
            : null;
    }

    public ProcessedAssembly ProcessAssembly(Assembly assembly)
    {
        var types = assembly.GetExportedTypes()
            .Where(ShouldIncludeType)
            .OrderBy(t => t.Namespace)
            .ThenBy(t => t.Name)
            .ToList();

        var namespaceGroups = types
            .GroupBy(t => t.Namespace ?? "")
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .OrderBy(g => g.Key);

        var namespaces = new List<NamespaceInfo>();

        foreach (var group in namespaceGroups)
        {
            var typeDeclarations = new List<TypeDeclaration>();

            foreach (var type in group)
            {
                try
                {
                    var declaration = ProcessType(type);
                    if (declaration != null)
                    {
                        typeDeclarations.Add(declaration);
                    }
                }
                catch (Exception ex)
                {
                    _typeMapper.AddWarning($"Failed to process type {type.FullName}: {ex.Message}");
                }
            }

            if (typeDeclarations.Count > 0)
            {
                namespaces.Add(new NamespaceInfo(group.Key, typeDeclarations));
            }
        }

        return new ProcessedAssembly(namespaces, _typeMapper.Warnings.ToList());
    }

    private bool ShouldIncludeType(Type type)
    {
        // Skip if not public
        if (!type.IsPublic && !type.IsNestedPublic)
        {
            return false;
        }

        // Skip compiler-generated types
        if (type.Name.Contains('<') || type.Name.Contains('>'))
        {
            return false;
        }

        // Skip if namespace is in skip list
        if (_config.SkipNamespaces.Contains(type.Namespace ?? ""))
        {
            return false;
        }

        // Apply whitelist if provided
        if (_namespaceWhitelist != null)
        {
            if (type.Namespace == null)
            {
                return false;
            }

            // Check if namespace or any parent namespace is in whitelist
            var ns = type.Namespace;
            while (!string.IsNullOrEmpty(ns))
            {
                if (_namespaceWhitelist.Contains(ns))
                {
                    return true;
                }

                var lastDot = ns.LastIndexOf('.');
                if (lastDot < 0) break;
                ns = ns.Substring(0, lastDot);
            }

            return false;
        }

        return true;
    }

    private TypeDeclaration? ProcessType(Type type)
    {
        if (type.IsEnum)
        {
            return ProcessEnum(type);
        }
        else if (type.IsInterface)
        {
            return ProcessInterface(type);
        }
        else if (type.IsClass || type.IsValueType)
        {
            return ProcessClass(type);
        }

        return null;
    }

    private EnumDeclaration ProcessEnum(Type type)
    {
        var members = Enum.GetValues(type)
            .Cast<object>()
            .Select(v => new EnumMember(
                Enum.GetName(type, v)!,
                Convert.ToInt64(v)))
            .ToList();

        return new EnumDeclaration(
            type.Name,
            type.FullName!,
            false,
            Array.Empty<string>(),
            members);
    }

    private InterfaceDeclaration ProcessInterface(Type type)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(ShouldIncludeMember)
            .Select(ProcessProperty)
            .ToList();

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(ShouldIncludeMember)
            .Where(m => !m.IsSpecialName)
            .Select(ProcessMethod)
            .ToList();

        var extends = type.GetInterfaces()
            .Where(i => i != typeof(IDisposable)) // Often not needed in TS
            .Select(i => _typeMapper.MapType(i))
            .ToList();

        var genericParams = type.IsGenericType
            ? type.GetGenericArguments().Select(t => t.Name).ToList()
            : new List<string>();

        return new InterfaceDeclaration(
            GetTypeName(type),
            type.FullName!,
            type.IsGenericType,
            genericParams,
            extends,
            properties,
            methods);
    }

    private ClassDeclaration ProcessClass(Type type)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Select(ProcessConstructor)
            .ToList();

        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(ShouldIncludeMember)
            .Select(ProcessProperty)
            .ToList();

        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(ShouldIncludeMember)
            .Where(m => !m.IsSpecialName)
            .Select(ProcessMethod)
            .ToList();

        var baseType = type.BaseType != null && type.BaseType != typeof(object) && type.BaseType != typeof(ValueType)
            ? _typeMapper.MapType(type.BaseType)
            : null;

        var interfaces = type.GetInterfaces()
            .Where(i => i.IsPublic)
            .Select(i => _typeMapper.MapType(i))
            .ToList();

        var genericParams = type.IsGenericType
            ? type.GetGenericArguments().Select(t => t.Name).ToList()
            : new List<string>();

        return new ClassDeclaration(
            GetTypeName(type),
            type.FullName!,
            type.IsGenericType,
            genericParams,
            baseType,
            interfaces,
            constructors,
            properties,
            methods,
            type.IsAbstract && type.IsSealed); // Static class
    }

    private TypeInfo.ConstructorInfo ProcessConstructor(System.Reflection.ConstructorInfo ctor)
    {
        var parameters = ctor.GetParameters()
            .Select(ProcessParameter)
            .ToList();

        return new TypeInfo.ConstructorInfo(parameters);
    }

    private TypeInfo.PropertyInfo ProcessProperty(System.Reflection.PropertyInfo prop)
    {
        return new TypeInfo.PropertyInfo(
            prop.Name,
            _typeMapper.MapType(prop.PropertyType),
            !prop.CanWrite,
            prop.GetMethod?.IsStatic ?? prop.SetMethod?.IsStatic ?? false);
    }

    private TypeInfo.MethodInfo ProcessMethod(System.Reflection.MethodInfo method)
    {
        var parameters = method.GetParameters()
            .Select(ProcessParameter)
            .ToList();

        var genericParams = method.IsGenericMethod
            ? method.GetGenericArguments().Select(t => t.Name).ToList()
            : new List<string>();

        return new TypeInfo.MethodInfo(
            method.Name,
            _typeMapper.MapType(method.ReturnType),
            parameters,
            method.IsStatic,
            method.IsGenericMethod,
            genericParams);
    }

    private TypeInfo.ParameterInfo ProcessParameter(System.Reflection.ParameterInfo param)
    {
        var isParams = param.GetCustomAttribute<ParamArrayAttribute>() != null;
        var paramType = isParams && param.ParameterType.IsArray
            ? param.ParameterType.GetElementType()!
            : param.ParameterType;

        return new TypeInfo.ParameterInfo(
            param.Name ?? $"arg{param.Position}",
            _typeMapper.MapType(paramType),
            param.IsOptional || param.HasDefaultValue,
            isParams);
    }

    private bool ShouldIncludeMember(MemberInfo member)
    {
        var fullMemberName = $"{member.DeclaringType?.FullName}::{member.Name}";

        if (_config.SkipMembers.Contains(fullMemberName))
        {
            return false;
        }

        // Skip common Object methods unless explicitly needed
        if (member.Name is "Equals" or "GetHashCode" or "GetType" or "ToString" or "ReferenceEquals")
        {
            return false;
        }

        return true;
    }

    private string GetTypeName(Type type)
    {
        if (!type.IsGenericType)
        {
            return type.Name;
        }

        var name = type.Name;
        var backtickIndex = name.IndexOf('`');
        return backtickIndex > 0 ? name.Substring(0, backtickIndex) : name;
    }
}

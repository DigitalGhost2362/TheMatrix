using System.Reflection;
using Original = Microsoft.Extensions.Logging;

namespace Feeder.Logging.Tests;

// Feeder.Logging may offer less than the reference package, never anything different: same names, parameter names and overloads
public class ApiParityTests
{
    const BindingFlags Declared = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    static readonly string[] OriginalNamespaces = { "Microsoft.Extensions.Logging", "Microsoft.Extensions.Logging.Abstractions" };

    static readonly Type[] OriginalTypes = new[] { typeof(Original.ILogger).Assembly, typeof(Original.LoggerFactory).Assembly }
        .SelectMany(assembly => assembly.GetExportedTypes())
        .Where(type => OriginalNamespaces.Contains(type.Namespace))
        .ToArray();

    static readonly Type[] OwnTypes = typeof(Feeder.Logging.ILogger).Assembly
        .GetExportedTypes()
        .Where(type => type.Namespace == "Feeder.Logging")
        .ToArray();

    public static IEnumerable<object[]> OwnTypeNames() => OwnTypes.OrderBy(type => type.Name).Select(type => new object[] { type.Name });

    [Fact]
    public void The_library_exposes_the_types_the_package_uses()
    {
        var names = OwnTypes.Select(type => type.Name).ToHashSet();

        foreach (var expected in new[]
        {
            "ILogger", "ILogger`1", "ILoggerFactory", "ILoggerProvider", "ILoggingBuilder", "LoggerFactory", "Logger`1",
            "LogLevel", "EventId", "LoggerExtensions", "LoggerFactoryExtensions", "NullLogger", "NullLogger`1", "NullLoggerFactory",
        })
        {
            Assert.Contains(expected, names);
        }
    }

    [Theory]
    [MemberData(nameof(OwnTypeNames))]
    public void Every_public_member_exists_in_the_original_with_the_same_shape(string typeName)
    {
        var own = OwnTypes.Single(type => type.Name == typeName);
        var original = OriginalTypes.SingleOrDefault(type => type.Name == typeName);
        Assert.True(original != null, $"{typeName} has no counterpart in the reference package");

        Assert.Equal(Shape(original!), Shape(own));
        Assert.Equal(Base(original!), Base(own));
        Assert.Empty(Interfaces(own).Except(Interfaces(original!)));

        var known = Members(original!).ToHashSet();
        if (typeName == "ILoggingBuilder")
        {
            // the original offers these as extension methods of LoggingBuilderExtensions
            var extensions = OriginalTypes.Single(type => type.Name == "LoggingBuilderExtensions");
            foreach (var method in extensions.GetMethods(Declared).Where(method => method.IsDefined(typeof(System.Runtime.CompilerServices.ExtensionAttribute))))
                known.Add(Describe(method, skipFirstParameter: true));
        }

        var missing = Members(own).Where(member => !known.Contains(member)).ToList();
        Assert.True(missing.Count == 0, $"{typeName} has members the original does not: {string.Join("; ", missing)}");
    }

    [Fact]
    public void Enum_values_agree_with_the_original()
    {
        var own = typeof(Feeder.Logging.LogLevel);
        var original = typeof(Original.LogLevel);

        Assert.Equal(
            Enum.GetNames(original).Select(name => name + "=" + Convert.ToInt64(Enum.Parse(original, name))),
            Enum.GetNames(own).Select(name => name + "=" + Convert.ToInt64(Enum.Parse(own, name))));
    }

    [Fact]
    public void The_original_extension_methods_the_package_calls_are_all_present()
    {
        var own = Members(typeof(Feeder.Logging.LoggerExtensions));
        var original = Members(typeof(Original.LoggerExtensions));

        // everything except the string-format BeginScope helper, which no call site uses
        var absent = original.Except(own).ToList();

        Assert.Single(absent);
        Assert.Contains("BeginScope", absent[0]);
    }

    static string Shape(Type type)
        => string.Join(" ", new[]
        {
            type.IsInterface ? "interface" : type.IsEnum ? "enum" : type.IsValueType ? "struct" : "class",
            type.IsAbstract && type.IsSealed ? "static" : type.IsSealed ? "sealed" : type.IsAbstract ? "abstract" : "open",
            type.IsGenericTypeDefinition
                ? "<" + string.Join(",", type.GetGenericArguments().Select(argument => argument.GenericParameterAttributes & GenericParameterAttributes.VarianceMask)) + ">"
                : "",
            type.IsDefined(typeof(FlagsAttribute)) ? "flags" : "",
        });

    static string Base(Type type) => type.BaseType == null ? "" : TypeName(type.BaseType);

    static IEnumerable<string> Interfaces(Type type) => type.GetInterfaces().Where(i => i.IsPublic).Select(TypeName);

    static IEnumerable<string> Members(Type type)
        => type.GetConstructors(Declared).Select(constructor => Describe(constructor))
            .Concat(type.GetMethods(Declared).Select(method => Describe(method)))
            .Concat(type.GetProperties(Declared).Select(property => "property " + property.Name + " : " + TypeName(property.PropertyType) + " " + string.Join("", property.GetAccessors().Select(a => a.Name.Substring(0, 3) + (a.IsStatic ? "S" : "I")).OrderBy(x => x))))
            .Concat(type.GetFields(Declared).Select(field => "field " + field.Name + " : " + TypeName(field.FieldType) + (field.IsStatic ? " static" : "") + (field.IsInitOnly ? " readonly" : "")));

    static string Describe(MethodBase method, bool skipFirstParameter = false)
    {
        // nobody can name the parameter of an operator
        var anonymous = method.IsSpecialName && method.Name.StartsWith("op_");
        var parameters = method.GetParameters().Skip(skipFirstParameter ? 1 : 0).Select(parameter =>
            (parameter.IsDefined(typeof(ParamArrayAttribute)) ? "params " : "")
            + TypeName(parameter.ParameterType) + " " + (anonymous ? "_" : parameter.Name)
            + (parameter.HasDefaultValue ? " = " + (parameter.DefaultValue ?? "null") : ""));
        var generics = method.IsGenericMethodDefinition
            ? "<" + string.Join(",", method.GetGenericArguments().Select(argument => argument.Name + ":" + argument.GenericParameterAttributes + ":" + string.Join("&", argument.GetGenericParameterConstraints().Select(TypeName)))) + ">"
            : "";
        var isStatic = method.IsStatic && !skipFirstParameter;
        var returns = method is MethodInfo info ? TypeName(info.ReturnType) : "ctor";
        var name = method is ConstructorInfo ? ".ctor" : method.Name;
        return $"{(isStatic ? "static " : "")}{name}{generics}({string.Join(", ", parameters)}) : {returns}";
    }

    // both libraries name their own types the same way; everything else keeps its full name
    static string TypeName(Type type)
    {
        if (type.IsGenericParameter)
            return "!" + type.GenericParameterPosition;
        if (type.IsArray)
            return TypeName(type.GetElementType()!) + "[]";
        if (type.IsByRef)
            return TypeName(type.GetElementType()!) + "&";
        if (type.IsGenericType && !type.IsGenericTypeDefinition)
            return TypeName(type.GetGenericTypeDefinition()) + "<" + string.Join(",", type.GetGenericArguments().Select(TypeName)) + ">";
        return type.Namespace == "Feeder.Logging" || OriginalNamespaces.Contains(type.Namespace) ? "Logging." + type.Name : type.FullName ?? type.Name;
    }
}

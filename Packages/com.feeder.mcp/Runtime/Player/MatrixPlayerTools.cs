#nullable enable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using Feeder.Json;

namespace Feeder.MCP.Player
{
    /// <summary>The tools a player offers: every static method marked <see cref="MatrixRuntimeToolAttribute"/>.</summary>
    internal sealed class MatrixPlayerTools
    {
        sealed class Entry
        {
            public MethodInfo Method = null!;
            public PlayerToolDescriptor Descriptor = null!;
        }

        readonly Dictionary<string, Entry> _tools = new Dictionary<string, Entry>(StringComparer.Ordinal);

        public static MatrixPlayerTools Scan()
        {
            var catalog = new MatrixPlayerTools();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // only this assembly and the ones that reference it can hold a runtime tool
                if (!CanHoldTools(assembly))
                    continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
                catch { continue; }

                foreach (var type in types)
                {
                    try
                    {
                        foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            var info = method.GetCustomAttribute<MatrixRuntimeToolAttribute>();
                            if (info == null || catalog._tools.ContainsKey(info.Name))
                                continue;
                            catalog._tools[info.Name] = new Entry { Method = method, Descriptor = Describe(info, method) };
                        }
                    }
                    catch (Exception)
                    {
                        // a type that cannot load its members holds no tool
                    }
                }
            }
            return catalog;
        }

        static bool CanHoldTools(Assembly assembly)
        {
            if (assembly == typeof(MatrixPlayerTools).Assembly)
                return true;
            try { return assembly.GetReferencedAssemblies().Any(reference => reference.Name == "Feeder.MCP.Player"); }
            catch (NotSupportedException) { return false; }
        }

        public int Count => _tools.Count;

        public PlayerRegistrySnapshot Snapshot(string projectId)
        {
            var tools = _tools.Values.Select(e => e.Descriptor).OrderBy(d => d.Name, StringComparer.Ordinal).ToArray();
            var source = string.Join("\n", tools.Select(t => t.Name + "\n" + t.Title + "\n" + t.Description + "\n" + Encoding.UTF8.GetString(t.InputSchemaJsonUtf8) + "\n" + t.ReadOnly));
            using (var sha = SHA256.Create())
                return new PlayerRegistrySnapshot
                {
                    ProjectId = projectId,
                    RegistryHash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(source)).Select(b => b.ToString("x2"))),
                    Tools = tools,
                };
        }

        /// <summary>Runs a tool; call on the main thread. Returns the CallToolResult JSON the matrix forwards to the client.</summary>
        public byte[] Run(string toolName, byte[] argumentsJson)
        {
            if (!_tools.TryGetValue(toolName, out var entry))
                throw new ToolException("FMP_TOOL_NOT_FOUND", $"Runtime tool '{toolName}' was not found in this player.");

            var arguments = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            if (argumentsJson.Length > 0)
                foreach (var pair in JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(argumentsJson) ?? new Dictionary<string, JsonElement>())
                    arguments[pair.Key] = pair.Value;

            var parameters = entry.Method.GetParameters();
            var values = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                values[i] = Bind(parameters[i], arguments);

            object? result;
            try { result = entry.Method.Invoke(null, values); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw new ToolException("FMP_TOOL_FAILED", e.InnerException.Message); }
            return Encoding.UTF8.GetBytes(ToCallToolResult(result));
        }

        static object? Bind(ParameterInfo parameter, Dictionary<string, JsonElement> arguments)
        {
            var name = parameter.Name ?? string.Empty;
            if (!arguments.TryGetValue(name, out var element) || element.ValueKind == JsonValueKind.Null)
            {
                if (parameter.HasDefaultValue)
                    return parameter.DefaultValue;
                if (!parameter.ParameterType.IsValueType)
                    return null;
                throw new ToolException("FMP_TOOL_FAILED", $"Missing argument '{name}'.");
            }
            var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            try
            {
                if (type.IsEnum)
                    return element.ValueKind == JsonValueKind.String ? Enum.Parse(type, element.GetString()!, true) : Enum.ToObject(type, element.GetInt32());
                return element.Deserialize(type, PlayerJson.Options);
            }
            catch (Exception e) when (!(e is ToolException))
            {
                throw new ToolException("FMP_TOOL_FAILED", $"Argument '{name}' is not a valid {type.Name}: {e.Message}");
            }
        }

        static string Quote(string text) => JsonSerializer.Serialize(text, typeof(string), PlayerJson.Options);

        static string ToCallToolResult(object? value)
        {
            if (value == null)
                return "{\"content\":[{\"type\":\"text\",\"text\":\"ok\"}]}";
            if (value is string text)
                return "{\"content\":[{\"type\":\"text\",\"text\":" + Quote(text) + "}]}";
            var json = JsonSerializer.Serialize(value, value.GetType(), PlayerJson.Options);
            var structured = json.StartsWith("{", StringComparison.Ordinal) ? ",\"structuredContent\":" + json : string.Empty;
            return "{\"content\":[{\"type\":\"text\",\"text\":" + Quote(json) + "}]" + structured + "}";
        }

        static PlayerToolDescriptor Describe(MatrixRuntimeToolAttribute info, MethodInfo method)
            => new PlayerToolDescriptor
            {
                Name = info.Name,
                Title = info.Title,
                Description = info.Description ?? method.GetCustomAttribute<DescriptionAttribute>()?.Description,
                InputSchemaJsonUtf8 = Encoding.UTF8.GetBytes(Schema(method)),
                ReadOnly = info.ReadOnly,
            };

        static string Schema(MethodInfo method)
        {
            var properties = new List<string>();
            var required = new List<string>();
            foreach (var parameter in method.GetParameters())
            {
                var type = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
                var name = parameter.Name ?? string.Empty;
                var description = parameter.GetCustomAttribute<DescriptionAttribute>()?.Description;
                var entry = "\"type\":\"" + JsonType(type) + "\"";
                if (type.IsEnum)
                    entry += ",\"enum\":[" + string.Join(",", Enum.GetNames(type).Select(Quote)) + "]";
                if (description != null)
                    entry += ",\"description\":" + Quote(description);
                properties.Add(Quote(name) + ":{" + entry + "}");
                if (!parameter.HasDefaultValue && parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) == null)
                    required.Add(Quote(name));
            }
            return "{\"type\":\"object\",\"properties\":{" + string.Join(",", properties) + "}" +
                   (required.Count > 0 ? ",\"required\":[" + string.Join(",", required) + "]" : string.Empty) + "}";
        }

        static string JsonType(Type type)
        {
            if (type == typeof(bool)) return "boolean";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte)) return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return "number";
            if (type.IsEnum || type == typeof(string)) return "string";
            return "object";
        }
    }

    internal sealed class ToolException : Exception
    {
        public string Code { get; }

        public ToolException(string code, string message) : base(message) => Code = code;
    }
}

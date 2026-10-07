#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Feeder.ToolHost;
using Feeder.Reflect.Model;
using Feeder.Reflect.Utils;
using Feeder.MCP.Editor.Matrix;
using Feeder.MCP.Utils;
using Feeder.Logging;

namespace Feeder.MCP.Editor.API
{
    public static partial class Tool_Script
    {
        public const string ScriptExecuteToolId = "script-execute";
        [AiTool
        (
            ScriptExecuteToolId,
            Title = "Script / Execute",
            OpenWorldHint = true
        )]
        [AiSkillDescription("Compiles and executes C# code dynamically using Roslyn. " +
            "Supports a full-code mode (default) and a body-only mode — see the skill body for the difference and for how to pass Unity object references as parameters.")]
        [AiSkillBody(
            "## Modes\n\n" +
            "- **Full code mode** (default, `isMethodBody=false`): the `csharpCode` argument must define a complete class with a static method (no top-level statements).\n" +
            "- **Body-only mode** (`isMethodBody=true`): provide only the method body statements. The tool auto-generates the usings, class, and method header.\n\n" +
            "## Passing Unity objects as parameters\n\n" +
            "Unity objects (`GameObject`, `Component`, etc.) can be passed as parameters using their `Ref` types (`GameObjectRef`, `ComponentRef`, etc.) or directly by type:\n\n" +
            "- `UnityEngine.GameObject` — resolves an actual GameObject from value `{\"instanceID\": N}`, `{\"name\": \"...\"}`, or `{\"path\": \"...\"}`.\n" +
            "- `UnityEngine.Component` (or any component subtype) — resolves from `{\"instanceID\": N}`.\n" +
            "- `Feeder.MCP.Data.GameObjectRef` — passes a `GameObjectRef` POCO directly; the method body calls `goRef.FindGameObject()` to resolve it.\n" +
            "- `Feeder.MCP.Data.ComponentRef` — passes a `ComponentRef` POCO.\n" +
            "- `Feeder.MCP.Data.ObjectRef` — passes a base `ObjectRef` POCO.")]
        [Description("Compiles and executes C# code dynamically using Roslyn. " +
            "Supports two modes: full code mode (default) requires a complete class definition, " +
            "while body-only mode (isMethodBody=true) auto-generates the boilerplate so you only " +
            "provide the method body. Unity objects (GameObject, Component, etc.) can be passed as " +
            "parameters using their Ref types (GameObjectRef, ComponentRef, etc.) or directly by type.")]
        public static SerializedMember? Execute
        (
            [Description("C# code to compile and execute. " +
                "In full code mode (default, isMethodBody=false): must define a complete class with a static method. " +
                "Example: 'using UnityEngine; public class Script { public static void Main() { Debug.Log(\"Hello\"); } }'. " +
                "Do NOT use top-level statements. " +
                "In body-only mode (isMethodBody=true): provide only the method body statements. " +
                "The tool auto-generates usings, class, and method header. " +
                "Example body: 'go.SetActive(false);'. " +
                "Custom helper classes can still be defined inline in the body-only string after the main logic, " +
                "but for complex additional class definitions use full code mode instead.")]
            string csharpCode,
            [Description("The name of the class containing the method to execute. " +
                "In body-only mode this becomes the generated class name.")]
            string className = "Script",
            [Description("The name of the method to execute. Must be a static method. " +
                "In body-only mode this becomes the generated method name.")]
            string methodName = "Main",
            [Description("Serialized parameters to pass to the method. Each entry must specify 'name' and 'typeName'. " +
                "Supported parameter types include primitives, strings, and Unity object references: " +
                "- 'UnityEngine.GameObject': resolves an actual GameObject from value '{\"instanceID\": N}', '{\"name\": \"...\"}', or '{\"path\": \"...\"}'. " +
                "- 'UnityEngine.Component' (or any component subtype): resolves from '{\"instanceID\": N}'. " +
                "- 'Feeder.MCP.Data.GameObjectRef': passes a GameObjectRef POCO directly; " +
                "  the method body calls goRef.FindGameObject() to resolve it. " +
                "- 'Feeder.MCP.Data.ComponentRef': passes a ComponentRef POCO. " +
                "- 'Feeder.MCP.Data.ObjectRef': passes a base ObjectRef POCO. " +
                "If the method does not require parameters, leave this empty.")]
            SerializedMemberList? parameters = null,
            [Description("When true, 'csharpCode' is treated as just the method body. " +
                "The tool auto-generates standard using directives (System, UnityEngine, " +
                "Feeder.MCP.Data, Feeder.MCP.Runtime.Extensions, UnityEditor), " +
                "the class definition, and the method signature (void return type). " +
                "Parameters from the 'parameters' list are automatically added to the method signature using their typeName and name. " +
                "When false (default), 'csharpCode' must be a complete C# compilation unit with class and method definitions.")]
            bool isMethodBody = false
        )
        {
            if (string.IsNullOrEmpty(csharpCode))
                throw new Exception($"'{nameof(csharpCode)}' is null or empty. Please provide valid C# code to execute.");

            if (string.IsNullOrEmpty(className))
                throw new Exception($"'{nameof(className)}' cannot be null or empty.");

            if (string.IsNullOrEmpty(methodName))
                throw new Exception($"'{nameof(methodName)}' cannot be null or empty.");

            if (HyperTesting.HyperTestingActivation.LooksLikeActivationAttempt(csharpCode))
                throw new Exception(HyperTesting.HyperTestingActivation.ActivationRefusal);

            string codeToCompile;
            if (isMethodBody)
            {
                codeToCompile = GenerateFullCode(csharpCode, className, methodName, parameters);
            }
            else
            {
                if (csharpCode.Contains(className) == false)
                    throw new Exception($"'{nameof(csharpCode)}' does not contain class '{className}'. Please ensure the class is defined in the provided code.");

                if (csharpCode.Contains(methodName) == false)
                    throw new Exception($"'{nameof(csharpCode)}' does not contain method '{methodName}'. Please ensure the method is defined in the provided code.");

                codeToCompile = csharpCode;
            }

            var assemblyBytes = CompileOnServer(codeToCompile);

            return MainThread.Instance.Run(() =>
            {
                var logger = UnityLoggerFactory.LoggerFactory.CreateLogger("Tool_Script.Execute");

                // load the assembly the server compiled and execute it immediately
                if (!ExecuteCSharpCode(
                    className: className,
                    methodName: methodName,
                    assemblyBytes: assemblyBytes,
                    parameters: parameters,
                    returnValue: out var result,
                    returnType: out var returnType,
                    error: out var error,
                    logger: logger))
                {
                    throw new Exception(error);
                }

                if (result is null)
                {
                    if (returnType is null)
                        return null;

                    var isVoid = returnType == typeof(void);
                    var ret = new SerializedMember
                    {
                        name = JsonSchema.Result,
                        typeName = isVoid ? "System.Void" : (returnType.FullName ?? returnType.Name ?? JsonSchema.Object)
                    };
                    return ret.SetJsonValue(isVoid ? "\"Success\"" : "\"null\"");
                }

                if (result is SerializedMember serializedResult)
                    return serializedResult;

                var reflector = MatrixPluginEditor.Instance.Reflector ?? throw new Exception("Reflector is not available.");

                var serializedResultByReflector = reflector.Serialize(
                    obj: result,
                    logger: logger);
                if (string.IsNullOrEmpty(serializedResultByReflector.name))
                    serializedResultByReflector.name = JsonSchema.Result;
                return serializedResultByReflector;
            });
        }

        static string GenerateFullCode(
            string methodBody,
            string className,
            string methodName,
            SerializedMemberList? parameters)
        {
            var sb = new StringBuilder();

            // Standard using directives
            sb.AppendLine("using System;");
            sb.AppendLine("using System.Collections;");
            sb.AppendLine("using System.Collections.Generic;");
            sb.AppendLine("using System.Linq;");
            sb.AppendLine("using UnityEngine;");
            // UnityEngine.UI ships in com.unity.ugui — only emit the using when its assembly
            // is loaded; otherwise Roslyn fails with CS0234 when the package isn't installed.
            if (IsAssemblyLoaded("UnityEngine.UI"))
                sb.AppendLine("using UnityEngine.UI;");
            sb.AppendLine("using UnityEngine.SceneManagement;");
            sb.AppendLine("using Feeder.MCP.Data;");
            sb.AppendLine("using Feeder.MCP.Runtime.Extensions;");
            sb.AppendLine("using UnityEditor;");
            sb.AppendLine();

            // Build method parameter list from the parameters SerializedMemberList
            var methodParams = parameters != null && parameters.Count > 0
                ? string.Join(", ", parameters.Select(p => $"{p.typeName ?? "object"} {p.name ?? "param"}"))
                : "";

            sb.AppendLine($"public class {className}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static void {methodName}({methodParams})");
            sb.AppendLine("    {");

            // Indent each line of the method body
            foreach (var line in methodBody.Split('\n'))
                sb.AppendLine($"        {line.TrimEnd()}");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        static bool IsAssemblyLoaded(string assemblyName)
            => AssemblyUtils.AllAssemblies.Any(a =>
                string.Equals(a.GetName().Name, assemblyName, StringComparison.Ordinal));

        // statics reset on domain reload, so the cache only has to track assemblies loaded since then
        static string[]? cachedReferencePaths;
        static int cachedReferenceSourceCount = -1;

        static string[] GetReferencePaths()
        {
            var sources = AssemblyUtils.AllAssemblies
                .Where(a => !a.IsDynamic) // Exclude dynamic assemblies
                .Where(a => !string.IsNullOrEmpty(a.Location))
                .ToArray();

            if (cachedReferencePaths != null && cachedReferenceSourceCount == sources.Length)
                return cachedReferencePaths;

            cachedReferencePaths = sources
                .Select(a => a.Location)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            cachedReferenceSourceCount = sources.Length;
            return cachedReferencePaths;
        }

        static byte[] CompileOnServer(string code)
        {
            var response = MatrixPluginEditor.Instance
                .CompileOnServerAsync(new CompileRequest { Source = code, ReferencePaths = GetReferencePaths() }, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(response.Error))
                throw new Exception($"Compile service failed: {response.Error}");
            if (!response.Success)
                throw new Exception($"Compilation failed:\n{string.Join("\n", response.Diagnostics ?? Array.Empty<string>())}");
            return response.Assembly ?? throw new Exception("Compile service returned no assembly.");
        }

        static bool ExecuteCSharpCode(
            string className,
            string methodName,
            byte[] assemblyBytes,
            SerializedMemberList? parameters,
            out object? returnValue,
            out Type? returnType,
            out string? error,
            ILogger? logger = null)
        {
            if (string.IsNullOrEmpty(className))
            {
                returnValue = null;
                returnType = null;
                error = $"'{nameof(className)}' cannot be null or empty.";
                return false;
            }
            if (string.IsNullOrEmpty(methodName))
            {
                returnValue = null;
                returnType = null;
                error = $"'{nameof(methodName)}' cannot be null or empty.";
                return false;
            }

            var reflector = MatrixPluginEditor.Instance.Reflector ?? throw new Exception("Reflector is not available.");

            var parsedParameters = parameters
                ?.Select(p => reflector.Deserialize(
                    data: p,
                    logger: logger))
                ?.ToArray();

            var assembly = Assembly.Load(assemblyBytes);
            var type = assembly.GetType(className);
            if (type == null)
            {
                error = $"Class '{className}' not found in the compiled assembly.";
                returnValue = null;
                returnType = null;
                return false;
            }
            var method = type.GetMethod(methodName);
            if (method == null)
            {
                error = $"Method '{methodName}' not found in class '{className}'.";
                returnValue = null;
                returnType = null;
                return false;
            }
            try
            {
                returnValue = method.Invoke(null, parsedParameters);
                returnType = method.ReturnType;
                error = null;
                return true;
            }
            catch (TargetInvocationException ex)
            {
                error = $"Execution failed. TargetInvocationException: {ex.InnerException?.Message ?? ex.Message}\n{ex.InnerException?.StackTrace ?? ex.StackTrace}";
                returnValue = null;
                returnType = null;
                return false;
            }
            catch (Exception ex)
            {
                error = $"Execution failed: {ex.InnerException?.Message ?? ex.Message}\n{ex.InnerException?.StackTrace ?? ex.StackTrace}";
                returnValue = null;
                returnType = null;
                return false;
            }
        }
    }
}

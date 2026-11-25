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
            OpenWorldHint = true,
            Enabled = true
        )]
        [AiSkillDescription("Compiles and executes C# code dynamically using Roslyn. " +
            "Supports a full-code mode (default) and a body-only mode — see the skill body for the difference and for how to pass Unity object references as parameters.")]
        [AiSkillBody(
            "## Modes\n\n" +
            "- **Full code mode** (default, `isMethodBody=false`): the `csharpCode` argument must define a complete class with a static method (no top-level statements).\n" +
            "- **Body-only mode** (`isMethodBody=true`): provide only the method body statements. The tool auto-generates the usings, class, and method header. " +
            "When the body returns a value (`return go.name;`), the method returns `object` and the value comes back as the result; otherwise it is `void`. " +
            "A `return x;` that sits only inside a lambda or local function keeps the method `void`. If some paths return a value, every path must (end with `return null;`).\n\n" +
            "## Passing Unity objects as parameters\n\n" +
            "Unity objects (`GameObject`, `Component`, etc.) can be passed as parameters using their `Ref` types (`GameObjectRef`, `ComponentRef`, etc.) or directly by type:\n\n" +
            "- `UnityEngine.GameObject` — resolves an actual GameObject from value `{\"instanceID\": N}`, `{\"name\": \"...\"}`, or `{\"path\": \"...\"}`.\n" +
            "- `UnityEngine.Component` (or any component subtype) — resolves from `{\"instanceID\": N}`.\n" +
            "- `Feeder.MCP.Data.GameObjectRef` — passes a `GameObjectRef` POCO directly; the method body calls `goRef.FindGameObject()` to resolve it.\n" +
            "- `Feeder.MCP.Data.ComponentRef` — passes a `ComponentRef` POCO.\n" +
            "- `Feeder.MCP.Data.ObjectRef` — passes a base `ObjectRef` POCO.\n\n" +
            "## Long runs\n\n" +
            "A call that runs longer than the project's tool timeout fails with a timeout. For code that is slow on purpose " +
            "(a bake, a long simulation) pass `timeoutSeconds`; the server caps it (30 minutes by default). " +
            "A domain reload cuts the call off: the error says so, and the code may have run in part.\n\n" +
            "## Keeping data between calls\n\n" +
            "Running the same code again reuses the assembly loaded for it, so its `static` fields keep their values between " +
            "those runs (a counter goes 1, 2, 3) until a domain reload (compile, entering play mode) clears them. Any change to " +
            "the code or to the loaded assemblies compiles a new assembly with empty statics. Pass `freshAssembly=true` when the " +
            "code must start from empty statics every time; that loads a new assembly per call, which Unity only frees on a " +
            "domain reload. To share a value between different pieces of code, store it in the domain: " +
            "`AppDomain.CurrentDomain.SetData(\"my-key\", value)` and `AppDomain.CurrentDomain.GetData(\"my-key\")`.\n\n" +
            "## Watching something change over time\n\n" +
            "The time between two calls includes your own thinking, often seconds, so polling from outside misses fast " +
            "changes. Record inside Unity instead: register a callback that samples every frame, removes itself after N " +
            "seconds, and keeps the samples in the domain; read them in a later call.\n\n" +
            "```csharp\n" +
            "var samples = new System.Collections.Generic.List<string>();\n" +
            "AppDomain.CurrentDomain.SetData(\"samples\", samples);\n" +
            "var until = EditorApplication.timeSinceStartup + 5;\n" +
            "EditorApplication.CallbackFunction sample = null;\n" +
            "sample = () =>\n" +
            "{\n" +
            "    if (EditorApplication.timeSinceStartup > until) { EditorApplication.update -= sample; return; }\n" +
            "    samples.Add($\"{Time.time:0.000} {Time.frameCount}\");   // what you want to watch\n" +
            "};\n" +
            "EditorApplication.update += sample;\n" +
            "```\n\n" +
            "`EditorApplication.update` also runs while the editor window is in the background; in play mode " +
            "`Application.onBeforeRender` runs once per rendered frame. Several agents can share one editor, so use keys " +
            "of your own and never leave a callback registered.\n\n" +
            "## Async code\n\n" +
            "A method that returns a `Task` is not awaited: start the work, keep progress in the domain and read it in a later call.\n\n" +
            "## Cookbook: `Mx` helpers (body mode imports them; guards and Undo built in)\n\n" +
            "Know the member names before writing: read the code, or turn on `rql-schema` (off by default) and call it with `typeName`. A wrong guess costs a compile round trip. " +
            "In body mode a bare `Object` and `Random` mean the `UnityEngine` ones; write `System.Random` for the other.\n\n" +
            "- `Mx.OpenScene(path, additive, ifDirty)` / `Mx.SaveScene(scene)` / `Mx.NewScene(path, ifDirty)` - `ifDirty`: `refuse` (default), `save`, `discard`.\n" +
            "- `Mx.CreateAsset(obj, path)` (never overwrites, makes folders) / `Mx.EnsureFolder(path)` / `Mx.TrashAsset(path)`.\n" +
            "- `Mx.Instantiate(prefabPath, parent)` (keeps the prefab link, one Undo step) / `Mx.EditPrefab(path, root => ...)`.\n" +
            "- `Mx.Call(objOrType, \"Method\", args)` - any method, private or static.\n" +
            "- `Mx.Shot(path, camera)` - PNG of the Game View or a camera, inside the project.\n" +
            "- Scene edits: `Undo.RecordObject(target, \"Edit\")` before the change, then `UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(go.scene)` (body mode does not import that namespace).\n" +
            "- A job you repeat for this project: make it a project tool (skill `matrix-tool-create`).")]
        [Description("Compiles C# with Roslyn and runs it in the Editor. Full mode (default): a class with a static method. " +
            "Body mode (isMethodBody=true): only the method body; usings, class and the Mx helpers come with it. " +
            "Unity objects can be passed as parameters. Identical code reuses its assembly, so statics persist (freshAssembly=true starts empty). " +
            "Before writing a script for prefab, asset file, scene create/unload, selection, build scenes or a debug method call, check unity-tool-list: a disabled tool may already do it.")]
        public static SerializedMember? Execute
        (
            [Description("The C#. Full mode, e.g. 'using UnityEngine; public class Script { public static void Main() { Debug.Log(\"Hi\"); } }'. " +
                "Body mode: statements only, e.g. 'go.SetActive(false);'; a 'return value;' becomes the result.")]
            string csharpCode = "",
            [Description("Class that holds the method; in body mode, the generated class name.")]
            string className = "Script",
            [Description("Static method to run; in body mode, the generated method name.")]
            string methodName = "Main",
            [Description("Method arguments as {name, typeName, value}. 'UnityEngine.GameObject' resolves from {\"instanceID\": N}, {\"name\": \"...\"} or {\"path\": \"...\"}; " +
                "a Component type from {\"instanceID\": N}; 'Feeder.MCP.Data.GameObjectRef' / 'ComponentRef' / 'ObjectRef' pass the reference itself.")]
            SerializedMemberList? parameters = null,
            [Description("true: csharpCode is a method body. Usings (System, UnityEngine, UnityEditor, Feeder.MCP.Editor; Object and Random are UnityEngine's), the class and the signature are generated; " +
                "parameters become method arguments.")]
            bool isMethodBody = false,
            [Description("Seconds this call may run; 0 (default) keeps the tool timeout. The server caps it (30 minutes by default).")]
            int timeoutSeconds = 0,
            [Description("true: load a new assembly for this call, so statics start empty. Default false reuses the assembly of identical code.")]
            bool freshAssembly = false,
            [RequestID]
            string? requestId = null
        )
        {
            if (string.IsNullOrEmpty(csharpCode))
                throw new ToolCallerException($"'{nameof(csharpCode)}' is null or empty. Please provide valid C# code to execute.");

            if (string.IsNullOrEmpty(className))
                throw new ToolCallerException($"'{nameof(className)}' cannot be null or empty.");

            if (string.IsNullOrEmpty(methodName))
                throw new ToolCallerException($"'{nameof(methodName)}' cannot be null or empty.");

            if (parameters != null && parameters.Any(p => p == null || string.IsNullOrWhiteSpace(p.name) || string.IsNullOrWhiteSpace(p.typeName)))
                throw new ToolCallerException("Every entry of 'parameters' needs 'name' and 'typeName', e.g. {\"name\":\"go\",\"typeName\":\"UnityEngine.GameObject\",\"value\":{\"path\":\"Player\"}}.");

            if (HyperTesting.HyperTestingActivation.LooksLikeActivationAttempt(csharpCode))
                throw new Exception(HyperTesting.HyperTestingActivation.ActivationRefusal);

            string codeToCompile;
            var returnsValue = false;
            if (isMethodBody)
            {
                returnsValue = BodyReturnsValue(csharpCode);
                codeToCompile = GenerateFullCode(csharpCode, className, methodName, parameters, returnsValue);
            }
            else
            {
                if (csharpCode.Contains(className) == false)
                    throw new ToolCallerException($"'{nameof(csharpCode)}' does not contain class '{className}'. Please ensure the class is defined in the provided code.");

                if (csharpCode.Contains(methodName) == false)
                    throw new ToolCallerException($"'{nameof(csharpCode)}' does not contain method '{methodName}'. Please ensure the method is defined in the provided code.");

                codeToCompile = csharpCode;
            }

            if (timeoutSeconds > 0)
                MatrixPluginEditor.RequestMoreTime(requestId, TimeSpan.FromSeconds(timeoutSeconds));

            var timer = System.Diagnostics.Stopwatch.StartNew();
            var assemblyBytes = returnsValue
                ? CompileBodyWithValue(csharpCode, className, methodName, parameters, codeToCompile, out var cached)
                : CompileOnServer(codeToCompile, out cached);
            var compileMs = timer.ElapsedMilliseconds;

            return MainThread.Instance.Run(() =>
            {
                var logger = UnityLoggerFactory.LoggerFactory.CreateLogger("Tool_Script.Execute");
                var waitMs = timer.ElapsedMilliseconds - compileMs;
                using var timing = new Timing(logger, compileMs, cached, waitMs, timer);

                // load the assembly the server compiled and execute it immediately
                if (!ExecuteCSharpCode(
                    className: className,
                    methodName: methodName,
                    assemblyBytes: assemblyBytes,
                    freshAssembly: freshAssembly,
                    parameters: parameters,
                    returnValue: out var result,
                    returnType: out var returnType,
                    error: out var error,
                    logger: logger))
                {
                    throw new ToolCallerException(error ?? "Execution failed.");
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

        // a 'return value;' may belong to a lambda or local function only; then the body compiles as void instead
        static byte[] CompileBodyWithValue(string body, string className, string methodName, SerializedMemberList? parameters, string objectCode, out bool cached)
        {
            try
            {
                return CompileOnServer(objectCode, out cached);
            }
            catch (ToolCallerException objectError) when (objectError.Message.Contains("CS0161") || objectError.Message.Contains("CS0126"))
            {
                try
                {
                    return CompileOnServer(GenerateFullCode(body, className, methodName, parameters, returnsValue: false), out cached);
                }
                catch (ToolCallerException voidError) when (voidError.Message.Contains("CS0127"))
                {
                    throw new ToolCallerException("The body returns a value on some paths but not on others. " +
                        "End it with a return on every path (for example 'return null;').\n" + objectError.Message);
                }
            }
        }

        static readonly System.Text.RegularExpressions.Regex ValueReturn =
            new System.Text.RegularExpressions.Regex(@"\breturn\b\s*[^;\s]", System.Text.RegularExpressions.RegexOptions.Compiled);

        internal static bool BodyReturnsValue(string body) => ValueReturn.IsMatch(StripLiterals(body));

        // blanks comments and string/char literals so a 'return' inside them is not taken for code
        internal static string StripLiterals(string code)
        {
            var sb = new StringBuilder(code.Length);
            var i = 0;
            while (i < code.Length)
            {
                var c = code[i];
                var next = i + 1 < code.Length ? code[i + 1] : '\0';
                if (c == '/' && next == '/')
                {
                    while (i < code.Length && code[i] != '\n')
                        i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    var end = code.IndexOf("*/", i + 2, StringComparison.Ordinal);
                    i = end < 0 ? code.Length : end + 2;
                    sb.Append(' ');
                    continue;
                }
                var verbatim = (c == '@' && next == '"') || (c == '$' && next == '@') || (c == '@' && next == '$');
                if (verbatim || c == '"' || (c == '$' && next == '"'))
                {
                    i = SkipString(code, i, verbatim);
                    sb.Append("\"\"");
                    continue;
                }
                if (c == '\'')
                {
                    i++;
                    while (i < code.Length && code[i] != '\'')
                        i += code[i] == '\\' ? 2 : 1;
                    i++;
                    sb.Append("' '");
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        static int SkipString(string code, int i, bool verbatim)
        {
            while (i < code.Length && code[i] != '"')
                i++;
            i++;
            while (i < code.Length)
            {
                var c = code[i];
                if (verbatim && c == '"' && i + 1 < code.Length && code[i + 1] == '"')
                {
                    i += 2;
                    continue;
                }
                if (!verbatim && c == '\\')
                {
                    i += 2;
                    continue;
                }
                if (c == '"')
                    return i + 1;
                i++;
            }
            return i;
        }

        internal static string GenerateFullCode(
            string methodBody,
            string className,
            string methodName,
            SerializedMemberList? parameters,
            bool returnsValue = false)
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
            sb.AppendLine("using Feeder.MCP.Editor;");
            // System and UnityEngine both define these, so a bare name was CS0104
            sb.AppendLine("using Object = UnityEngine.Object;");
            sb.AppendLine("using Random = UnityEngine.Random;");
            sb.AppendLine();

            // Build method parameter list from the parameters SerializedMemberList
            var methodParams = parameters != null && parameters.Count > 0
                ? string.Join(", ", parameters.Select(p => $"{p.typeName ?? "object"} {p.name ?? "param"}"))
                : "";

            sb.AppendLine($"public class {className}");
            sb.AppendLine("{");
            sb.AppendLine($"    public static {(returnsValue ? "object" : "void")} {methodName}({methodParams})");
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

        // Compiled images by source and reference set, newest last. Static, so a domain reload empties it. The image is
        // still loaded fresh on every call, so the script's static fields start empty each time as before.
        const int CompileCacheSize = 32;
        static readonly System.Collections.Generic.List<(string Key, byte[] Image)> compileCache = new();

        // The compile cache hands out the same byte[] for the same code, so that array identifies the assembly loaded from it.
        // Entries go when the cache evicts the bytes, and a domain reload empties the table.
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], Assembly> LoadedAssemblies = new();

        static byte[] CompileOnServer(string code, out bool cached)
        {
            var references = GetReferencePaths();
            var key = CompileKey(code, references);
            lock (compileCache)
            {
                var hit = compileCache.FindIndex(entry => entry.Key == key);
                if (hit >= 0)
                {
                    var entry = compileCache[hit];
                    compileCache.RemoveAt(hit);
                    compileCache.Add(entry);
                    cached = true;
                    return entry.Image;
                }
            }

            var response = MatrixPluginEditor.Instance
                .CompileOnServerAsync(new CompileRequest { Source = code, ReferencePaths = references }, CancellationToken.None)
                .GetAwaiter().GetResult();
            if (!string.IsNullOrEmpty(response.Error))
                throw new Exception($"Compile service failed: {response.Error}");
            if (!response.Success)
                throw new ToolCallerException($"Compilation failed:\n{string.Join("\n", response.Diagnostics ?? Array.Empty<string>())}");
            var image = response.Assembly ?? throw new Exception("Compile service returned no assembly.");

            lock (compileCache)
            {
                compileCache.Add((key, image));
                if (compileCache.Count > CompileCacheSize)
                    compileCache.RemoveAt(0);
            }
            cached = false;
            return image;
        }

        static string CompileKey(string code, string[] references)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var bytes = Encoding.UTF8.GetBytes(code + "\n\0" + string.Join("\n", references));
            return Convert.ToBase64String(sha.ComputeHash(bytes));
        }

        // Debug-level timing of each stage, to show where the time of a call goes before optimizing further
        sealed class Timing : IDisposable
        {
            readonly ILogger _logger;
            readonly long _compileMs;
            readonly bool _cached;
            readonly long _waitMs;
            readonly System.Diagnostics.Stopwatch _timer;
            readonly long _runStartMs;

            public Timing(ILogger logger, long compileMs, bool cached, long waitMs, System.Diagnostics.Stopwatch timer)
            {
                _logger = logger;
                _compileMs = compileMs;
                _cached = cached;
                _waitMs = waitMs;
                _timer = timer;
                _runStartMs = timer.ElapsedMilliseconds;
            }

            public void Dispose()
                => _logger.LogDebug("script-execute timing: compile {compileMs} ms{cache}, main thread wait {waitMs} ms, run {runMs} ms",
                    _compileMs, _cached ? " (cached, skipped)" : string.Empty, _waitMs, _timer.ElapsedMilliseconds - _runStartMs);
        }

        static bool ExecuteCSharpCode(
            string className,
            string methodName,
            byte[] assemblyBytes,
            bool freshAssembly,
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

            var assembly = freshAssembly ? Assembly.Load(assemblyBytes) : LoadedAssemblies.GetValue(assemblyBytes, Assembly.Load);
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
            if (!method.IsStatic)
            {
                error = $"Method '{className}.{methodName}' is not static. Declare it 'public static', or use isMethodBody=true.";
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
            catch (Exception ex)
            {
                var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                error = $"Execution failed: {inner.GetType().Name}: {inner.Message}\n{ScriptFrames(inner, assembly)}";
                returnValue = null;
                returnType = null;
                return false;
            }
        }

        // only the frames of the caller's own script; the tool's and Unity's frames say nothing to the caller
        static string ScriptFrames(Exception exception, Assembly script)
        {
            var frames = new System.Diagnostics.StackTrace(exception, true).GetFrames() ?? Array.Empty<System.Diagnostics.StackFrame>();
            var lines = frames
                .Where(frame => frame.GetMethod()?.DeclaringType?.Assembly == script)
                .Select(frame =>
                {
                    var method = frame.GetMethod()!;
                    var line = frame.GetFileLineNumber();
                    return $"  at {method.DeclaringType!.FullName}.{method.Name}{(line > 0 ? $" (line {line})" : string.Empty)}";
                })
                .ToArray();
            return lines.Length > 0
                ? string.Join("\n", lines)
                : string.Join("\n", (exception.StackTrace ?? string.Empty).Split('\n').Take(3).Select(l => l.TrimEnd()));
        }
    }
}

#nullable enable
using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using Feeder.ToolHost;
using Feeder.MCP.Editor.Matrix;
using Feeder.MCP.Editor.Utils;

namespace Feeder.MCP.Editor.API
{
    public static partial class Tool_Script
    {
        public const string ScriptValidateToolId = "script-validate";
        [AiTool
        (
            ScriptValidateToolId,
            Title = "Script / Validate",
            ReadOnlyHint = true,
            IdempotentHint = true,
            Enabled = false
        )]
        [AiSkillDescription("Check C# code for errors without writing or running it. `semantic=true` (default) compiles against " +
            "every loaded assembly like '" + ScriptExecuteToolId + "' does, so type and name errors show up; `semantic=false` only parses. " +
            "Errors come back as `line:col CODE message`.")]
        [AiSkillBody("Validates C# source from `content`, or from the file at `filePath` when `content` is omitted. Nothing is written.\n\n" +
            "A semantic check compiles the one file alone: a file that is a part of a partial class, or that uses types private to an " +
            "assembly definition, can report errors that Unity will not. Treat those as advisory.")]
        [Description("Checks C# code for syntax errors, or with semantic=true (default) for compile errors against all loaded assemblies. " +
            "Reads `content`, or the file at `filePath`. Writes nothing. Errors are returned as 'line:col CODE message'.")]
        public static string Validate
        (
            [Description("The path of a .cs file to check. Ignored when 'content' is given.")]
            string? filePath = null,
            [Description("C# source to check.")]
            string? content = null,
            [Description("true (default): compile with all references, finds type and name errors. false: syntax only.")]
            bool semantic = true
        )
        {
            if (content == null)
            {
                if (string.IsNullOrEmpty(filePath))
                    throw new ToolCallerException("Provide 'content' or 'filePath'.");
                if (!File.Exists(filePath))
                    throw new FileNotFoundException(Error.ScriptFileNotFound(filePath), filePath);
                content = File.ReadAllText(filePath);
            }

            var ok = CheckSource(content, semantic, out var errors, out var skipped);
            var level = semantic ? "semantic" : "syntax";
            if (skipped != null)
                return $"SKIPPED: the compile service is not available ({skipped}).";
            return ok
                ? $"OK: no {level} errors."
                : $"{errors.Length} {level} error(s):\n{string.Join("\n", errors.Select(ScriptEdit.FormatDiagnostic))}";
        }

        // skipped is set when the compile service could not answer; the code is then neither confirmed nor rejected
        internal static bool CheckSource(string code, bool semantic, out string[] errors, out string? skipped)
        {
            try
            {
                var request = new CompileRequest { Source = code, SyntaxOnly = !semantic, AssemblyName = "MatrixValidate" };
                if (semantic)
                    request.ReferencePaths = GetReferencePaths();
                var response = MatrixPluginEditor.Instance.CompileOnServerAsync(request, CancellationToken.None).GetAwaiter().GetResult();
                if (!string.IsNullOrEmpty(response.Error))
                {
                    errors = Array.Empty<string>();
                    skipped = response.Error;
                    return true;
                }
                errors = (response.Diagnostics ?? Array.Empty<string>())
                    .Where(d => !d.Contains(": warning ") && !d.Contains(": info "))
                    .ToArray();
                skipped = null;
                return response.Success;
            }
            catch (Exception e)
            {
                errors = Array.Empty<string>();
                skipped = e.Message;
                return true;
            }
        }
    }
}

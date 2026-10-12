#nullable enable
using Feeder.MCP.Data;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using Feeder.Json.Nodes;
using Feeder.Json.Serialization;
using System.Text.RegularExpressions;
using Feeder.ToolHost;
using Feeder.Reflect.Utils;
using Feeder.MCP.Utils;

namespace Feeder.MCP.Editor.API
{
    public partial class Tool_Tool
    {
        public const string ToolListId = "unity-tool-list";

        static readonly TimeSpan RegexTimeout = TimeSpan.FromMilliseconds(200);

        [Description("Specifies what to include for tool input arguments.")]
        public enum InputRequest
        {
            [Description("Do not include input arguments.")]
            None = 0,

            [Description("Include input argument names only.")]
            Inputs = 1,

            [Description("Include input argument names and descriptions.")]
            InputsWithDescription = 2
        }

        [AiTool
        (
            ToolListId,
            Title = "Tool / List",
            ReadOnlyHint = true,
            IdempotentHint = true
        )]
        [AiSkillDescription("List all Feeder MCP tools registered in the connected Unity Editor instance, including disabled ones " +
            "(see `enabled`); only a core set is on by default, so use this to find a tool, then enable it with tool-set-enabled-state. " +
            "Optional regex filter matches against tool name, description, and argument names/descriptions. " +
            "Use the `includeDescription` / `includeInputs` toggles to control the response size.")]
        [AiSkillBody("List all Feeder MCP tools registered in the connected Unity Editor instance. " +
            "Optionally filter by regex across tool names, descriptions, and arguments.\n\n" +
            "## Inputs\n\n" +
            "- `regexSearch` (optional) — case-insensitive regex with a 200ms execution-timeout guard. " +
            "Invalid regex throws an `ArgumentException`. Matches name → description → input names → input descriptions.\n" +
            "- `includeDescription` (default `false`) — populate each tool's `Description` field.\n" +
            "- `includeInputs` (default `None`) — one of `None`, `Inputs` (names only), `InputsWithDescription` (names + descriptions).\n\n" +
            "## Behavior\n\n" +
            "Iterates `MatrixPluginEditor.Instance.Tools.GetAllTools()`, evaluates the filter (if any), and projects " +
            "each surviving tool into a `ToolInfoData` honoring the verbosity toggles.")]
        [Description("List all Feeder MCP tools registered in the connected Unity Editor instance, including disabled ones " +
            "(see `enabled`). Only a core set is enabled to save tokens; turn another on with tool-set-enabled-state. " +
            "Optionally filter by regex across tool names, descriptions, and arguments.")]
        public ToolInfoData[] List
        (
            [Description("Regex pattern to filter tools. " +
                "Matches against tool name, description, and argument names and descriptions.")]
            string? regexSearch = null,

            [Description("Include tool descriptions in the result. Default: false")]
            bool? includeDescription = false,

            [Description("Include input arguments in the result. Default: None")]
            InputRequest? includeInputs = InputRequest.None
        )
        {
            return MainThread.Instance.Run(() =>
            {
                var toolManager = MatrixPluginEditor.Instance.Tools
                    ?? throw new InvalidOperationException(Error.ToolManagerNotAvailable());

                var allTools = toolManager.GetAllTools();
                if (allTools == null)
                    return Array.Empty<ToolInfoData>();

                Regex? regex = null;
                if (!string.IsNullOrWhiteSpace(regexSearch))
                {
                    try { regex = new Regex(regexSearch!, RegexOptions.IgnoreCase, RegexTimeout); }
                    catch (ArgumentException ex)
                    {
                        throw new ArgumentException($"Invalid regex pattern '{regexSearch}': {ex.Message}", nameof(regexSearch));
                    }
                }

                var inputsRequested = includeInputs ?? InputRequest.None;
                var descriptionRequested = includeDescription == true;
                var needInputs = regex != null || inputsRequested != InputRequest.None;

                var results = new List<ToolInfoData>();

                foreach (var tool in allTools)
                {
                    List<ToolInputData>? inputs = null;

                    if (regex != null)
                    {
                        if (!MatchesTool(regex, tool, needInputs ? tool.InputSchema : null, out inputs))
                            continue;
                    }

                    var info = new ToolInfoData { Name = tool.Name, Enabled = tool.Enabled };

                    if (descriptionRequested)
                        info.Description = tool.Description;

                    if (inputsRequested != InputRequest.None)
                    {
                        inputs ??= ParseInputs(tool.InputSchema);
                        info.Inputs = BuildInputResults(inputs, inputsRequested);
                    }

                    results.Add(info);
                }

                return results.ToArray();
            });
        }

        static bool MatchesTool(Regex regex, IRunTool tool, JsonNode? schema, out List<ToolInputData>? parsedInputs)
        {
            parsedInputs = null;

            if (regex.IsMatch(tool.Name ?? string.Empty))
                return true;

            if (tool.Description != null && regex.IsMatch(tool.Description))
                return true;

            if (schema == null)
                return false;

            parsedInputs = ParseInputs(schema);
            foreach (var input in parsedInputs)
            {
                if (regex.IsMatch(input.Name))
                    return true;

                if (input.Description != null && regex.IsMatch(input.Description))
                    return true;
            }

            return false;
        }

        static ToolInputData[] BuildInputResults(List<ToolInputData> inputs, InputRequest request)
        {
            var result = new ToolInputData[inputs.Count];
            for (int i = 0; i < inputs.Count; i++)
            {
                result[i] = new ToolInputData
                {
                    Name = inputs[i].Name,
                    Description = request == InputRequest.InputsWithDescription
                        ? inputs[i].Description
                        : null
                };
            }
            return result;
        }

        static List<ToolInputData> ParseInputs(JsonNode? schema)
        {
            var result = new List<ToolInputData>();

            if (schema is not JsonObject schemaObject)
                return result;

            if (!schemaObject.TryGetPropertyValue(JsonSchema.Properties, out var propertiesNode))
                return result;

            if (propertiesNode is not JsonObject propertiesObject)
                return result;

            foreach (var (name, element) in propertiesObject)
            {
                string? description = null;
                if (element is JsonObject propertyObject &&
                    propertyObject.TryGetPropertyValue(JsonSchema.Description, out var descriptionNode) &&
                    descriptionNode != null)
                {
                    description = descriptionNode.ToString();
                }

                result.Add(new ToolInputData
                {
                    Name = name,
                    Description = description
                });
            }

            return result;
        }
    }
}

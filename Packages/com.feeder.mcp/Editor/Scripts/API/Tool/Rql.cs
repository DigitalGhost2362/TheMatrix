#nullable enable
using System;
using System.ComponentModel;
using Feeder.MCP.Editor.Query;
using Feeder.Reflect.Utils;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;

namespace Feeder.MCP.Editor.API
{
    [AiToolType]
    public partial class Tool_Rql
    {
        public const string QueryToolId = "rql-query";
        public const string MutateToolId = "rql-mutate";
        public const string SchemaToolId = "rql-schema";

        const string Examples =
            "```\n" +
            "objects | where Light.intensity > 1 | select name, Light.intensity, $path\n" +
            "objects | where name startswith \"Enemy\" and active | count\n" +
            "objects | components | group $type\n" +
            "components(\"Light\") | where type == \"Point\" | sort intensity desc | take 5\n" +
            "find(\"Player\").transform.position\n" +
            "find(\"Player\") | descendants | where not activeSelf | select $path\n" +
            "assets(\"t:Material\", \"Assets/Art\") | where shader.name contains \"Lit\" | select name, $path, $[\"_BaseColor\"]\n" +
            "logs | where type == \"Error\" | take 20\n" +
            "scenes\n" +
            "```";

        static ResponseCallTool Respond(Func<string> body)
        {
            return MainThread.Instance.Run(() =>
            {
                try
                {
                    return ResponseCallTool.Success(body());
                }
                catch (QueryException e)
                {
                    return ResponseCallTool.Error(e.Message);
                }
                catch (Exception e)
                {
                    return ResponseCallTool.Error($"{e.GetType().Name}: {e.Message}");
                }
            });
        }

        [AiTool(QueryToolId, Title = "Query / Read", ReadOnlyHint = true, IdempotentHint = true)]
        [AiSkillDescription("Read anything in the open scenes, prefabs, assets, console and project with one pipeline query " +
            "(`objects | where Light.intensity > 1 | select name, $path`). Filters, projects, sorts, groups and counts many objects in one call and " +
            "answers as a compact text table. Use '" + SchemaToolId + "' for the syntax and a type's members, '" + MutateToolId + "' to change things.")]
        [AiSkillBody("# Query / Read\n\n" +
            "One call replaces many find/get calls: pick a source, then chain stages with `|`.\n\n" +
            "## Examples\n\n" + Examples + "\n\n" +
            "## Language\n\n```\n" + QuerySchema.Reference + "\n```\n\n" +
            "## Output\n\n" +
            "- `table` (default): a header line, then one line per row with ` | ` between cells. GameObjects show `id | path | active | components`, " +
            "components `id | type | path | enabled`, assets `id | type | name | path`. `select` picks your own columns.\n" +
            "- `json`: `{\"total\":N,\"items\":[...]}` with vectors as arrays and objects as `{id,name,type,path}`.\n" +
            "- Only `limit` rows are printed (default 100); the first line says how many matched and how to page with `skip`/`take`.\n" +
            "- A member that does not exist reads as `null` and adds a line under `warnings:`; check it when a filter matches nothing.\n\n" +
            "## Tips\n\n" +
            "- Read `sharedMaterial`, not `material`: reading `material` in edit mode makes a copy of the material.\n" +
            "- `rql-query` only calls reading methods (`GetComponent`, `CompareTag`, `ToString`...). Use '" + MutateToolId + "' for anything else.\n" +
            "- Ids (`$id`) are instance ids: they change after a domain reload, paths do not.")]
        [Description("Run a read-only pipeline query over the open scenes, prefab, assets, console and project. " +
            "Example: objects | where Light.intensity > 1 | select name, Light.intensity, $path. Returns a text table (or json).")]
        public ResponseCallTool Query
        (
            [Description("The query: a source (objects, roots, selection, scenes, logs, components, assets(\"t:Type\"), asset(\"Assets/...\"), find(\"Path\")) " +
                "followed by stages separated by '|': where, select, sort, take, skip, count, distinct, group, sum/avg/min/max, get, components, children, descendants, parent.")]
            string query,
            [Description("Maximum rows printed (default 100, max 2000). The total count is always reported.")]
            int limit = 100,
            [Description("table (default, compact) or json.")]
            string format = "table"
        )
        {
            return Respond(() => MatrixQuery.Run(query, Math.Min(limit, 2000), format).Text);
        }

        [AiTool(MutateToolId, Title = "Query / Change", DestructiveHint = true)]
        [AiSkillDescription("Change many objects, components and assets in one query: `objects | where name startswith \"Enemy\" | set Rigidbody.mass = 2`. " +
            "Also `call` methods, `add` components, `destroy` objects and create them with `new(...)`. All-or-nothing, one undo step, optional dry run.")]
        [AiSkillBody("# Query / Change\n\n" +
            "Select targets with the same pipeline as '" + QueryToolId + "', then end with one or more changing stages.\n\n" +
            "## Examples\n\n```\n" +
            "find(\"Player\") | set position = [0, 1, 0], name = \"Hero\"\n" +
            "objects | where name startswith \"Enemy\" | set Rigidbody.mass = Rigidbody.mass * 2\n" +
            "objects | where tag == \"Pickup\" | set active = false\n" +
            "find(\"Lamp\") | set Light.color = \"#ffcc88\", Light.intensity = 2, Light.type = \"Spot\"\n" +
            "find(\"Crate\") | set transform.localScale.y = 2\n" +
            "asset(\"Assets/Materials/Red.mat\") | set $[\"_BaseColor\"] = color(1, 0, 0)\n" +
            "new(\"Spawn\", \"Cube\", \"Level/Props\") | set position = [2, 0, 5] | add Rigidbody | set mass = 3\n" +
            "objects | where name == \"Door\" | call transform.SetParent(find(\"House\").transform)\n" +
            "objects | where name matches \"^Temp_\" | destroy\n" +
            "```\n\n" +
            "## Stages that change things\n\n" +
            "- `set PATH = EXPR, ...` writes members. Paths reach into structs (`transform.position.x`), components (`Light.intensity`), " +
            "shader properties (`$[\"_BaseColor\"]`, `sharedMaterial[\"_Color\"]`) and array elements (`sharedMaterials[0]`). " +
            "Every right-hand side is computed from the values before the change.\n" +
            "- `call Method(args)` or `call member.Method(args)` runs a method on each target.\n" +
            "- `add Type` adds a component to each GameObject; the next stages work on the new components.\n" +
            "- `destroy` removes GameObjects or components (not assets, not Transforms).\n" +
            "- `new(\"Name\"[, \"Cube\"][, parent])` as the source creates a GameObject (in the open prefab when prefab mode is on).\n\n" +
            "## Safety\n\n" +
            "- **All-or-nothing**: every target is checked before anything changes; if one fails, nothing changes and the error lists each problem. " +
            "If a later stage fails, the earlier stages are rolled back.\n" +
            "- **One undo step** named 'Matrix query' (Edit > Undo reverts the whole query).\n" +
            "- `dryRun=true` runs the query, reports every change as `target | action | old | new`, then reverts it. `call` is not run in a dry run.\n" +
            "- `maxTargets` (default 100) refuses queries that would touch more targets; narrow with `where` or raise it on purpose.\n" +
            "- Scenes and assets are marked dirty, not saved, unless `save=true`.\n\n" +
            "## Values\n\n" +
            "Numbers convert to the member's type (`3` to float, `1` to an enum value). Enums take names (`\"Spot\"`, flags `\"A|B\"`). " +
            "Vectors take `[x, y, z]` or `vec(x, y, z)`; rotations take euler `[x, y, z]`; colors take `\"#rrggbb\"`, names or `color(r, g, b[, a])`. " +
            "Object fields take `find(\"Path\")`, `asset(\"Assets/...\")`, or a path string. Lists fill arrays.\n\n" +
            "Use '" + SchemaToolId + "' with a type name to see which members can be written.")]
        [Description("Change objects, components and assets with a pipeline query ending in set/call/add/destroy (or starting with new(...)). " +
            "All-or-nothing, one undo step. Example: objects | where name startswith \"Enemy\" | set Rigidbody.mass = 2")]
        public ResponseCallTool Mutate
        (
            [Description("The query, e.g. find(\"Player\") | set position = [0, 1, 0]. Stages: set PATH = EXPR, call Method(args), add Type, destroy; source new(\"Name\"[, \"Cube\"][, parent]).")]
            string query,
            [Description("true: report every change (target | action | old | new), then revert it. Nothing stays changed.")]
            bool dryRun = false,
            [Description("Refuse when a changing stage would touch more targets than this (default 100).")]
            int maxTargets = 100,
            [Description("true: save the touched scenes, prefab and assets after the change. Default false: only marked dirty.")]
            bool save = false
        )
        {
            return Respond(() => MatrixQuery.Mutate(query, dryRun, maxTargets, save).Text);
        }

        [AiTool(SchemaToolId, Title = "Query / Schema", ReadOnlyHint = true, IdempotentHint = true)]
        [AiSkillDescription("Syntax card of the query language plus the component types present in the open scenes; with `typeName`, " +
            "the members (type, read or write) and methods of that type. Call it before writing a '" + QueryToolId + "' or '" + MutateToolId + "' query you are unsure of.")]
        [AiSkillBody("# Query / Schema\n\n" +
            "- No arguments: the language reference, every function, and the component types in the open scenes with their counts.\n" +
            "- `typeName` (`Light`, `BoxCollider`, `UnityEngine.UI.Image`, a script class, an enum): its members as `member | type | access` " +
            "(`get set` can be written by `set`) and its public methods; `*` marks methods `rql-query` may call.\n" +
            "- `filter`: a regex on member and method names. `includeInherited`: also list members from Component/MonoBehaviour/Object.")]
        [Description("Without arguments: the query language reference and the component types in the open scenes. " +
            "With typeName: that type's members (get/set) and methods.")]
        public ResponseCallTool Schema
        (
            [Description("Type to describe, e.g. Light, Rigidbody, Material, UnityEngine.UI.Image, or an enum. Empty: the language reference.")]
            string? typeName = null,
            [Description("Regex on member and method names, e.g. 'color|intensity'.")]
            string? filter = null,
            [Description("List public methods too (default true).")]
            bool includeMethods = true,
            [Description("Also list members inherited from Component, Behaviour, MonoBehaviour and Object.")]
            bool includeInherited = false
        )
        {
            return Respond(() => QuerySchema.Describe(typeName, filter, includeMethods, includeInherited));
        }
    }
}

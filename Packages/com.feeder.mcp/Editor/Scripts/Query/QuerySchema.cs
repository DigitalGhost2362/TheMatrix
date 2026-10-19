#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Feeder.MCP.Editor.Query
{
    public static class QuerySchema
    {
        static readonly HashSet<Type> BaseTypes = new HashSet<Type>
        {
            typeof(object), typeof(Object), typeof(Component), typeof(Behaviour), typeof(MonoBehaviour), typeof(ScriptableObject),
        };

        public const string Reference =
            "query := source ( '|' stage )*\n" +
            "sources: objects (every GameObject in the open scenes, or the open prefab), roots, selection, scenes, logs, components, prefab,\n" +
            "  assets(\"t:Material\"[, \"Assets/Folder\"]), asset(\"Assets/x.mat\"), find(\"Path/Or/Name\" | id), obj(id), any expression (find(\"Player\").transform)\n" +
            "  rql-mutate only: new(\"Name\"[, \"Cube|Sphere|Capsule|Cylinder|Plane|Quad\"[, parent]])\n" +
            "stages: where EXPR | select EXPR [as name], ... | sort EXPR [desc], ... | take N | skip N | first | last | count | reverse\n" +
            "  | distinct [EXPR] | group EXPR (rows: key, count) | sum/avg/min/max [EXPR] | get EXPR (map, flattens lists)\n" +
            "  | components [Type] | children | descendants [depth] | parent | gameobject\n" +
            "  rql-mutate only: set PATH = EXPR, ... | call METHOD(args) | add Type | destroy\n" +
            "expressions: members (name, transform.position.x, Light.intensity: a component by its type on a GameObject),\n" +
            "  index a[0], material[\"_BaseColor\"], go[\"Rigidbody\"], meta $ $index $id $path $type,\n" +
            "  literals 1 2.5 \"text\" 'text' true false null [1, 2, 3], operators == != < <= > >= and or not ?? + - * / %,\n" +
            "  contains startswith endswith (ignore case) matches (regex) in [..], is Type, is null\n" +
            "GameObject shortcuts: parent active position localPosition rotation eulerAngles localEulerAngles localScale\n" +
            "values to write: numbers, \"Enum\" or \"A|B\", [x,y,z] or vec(x,y,z) for vectors, [x,y,z] euler for rotations,\n" +
            "  \"#ff8800\" or color(1,0.5,0) for colors, objects by find()/asset()/\"Assets/path\"/\"Hierarchy/Path\", lists for arrays\n" +
            "missing members read as null with a warning; queries only call reading methods (Get*, Is*, Has*, Find*, To*...)";

        public static string Describe(string? typeName = null, string? filter = null, bool includeMethods = true, bool includeInherited = false)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return Overview();
            var type = QueryTypes.Resolve(typeName!, -1);
            var regex = string.IsNullOrEmpty(filter) ? null : QueryOps.MakeRegex("(?i)" + filter, -1);
            var sb = new StringBuilder();
            sb.Append(type.FullName);
            if (type.BaseType != null)
                sb.Append(" : ").Append(QueryTypes.ShortName(type.BaseType));
            sb.Append('\n');
            if (type.IsEnum)
            {
                sb.Append("values: ").Append(string.Join(", ", Enum.GetNames(type)));
                return sb.ToString();
            }
            if (type == typeof(GameObject))
                sb.Append("shortcuts: parent active position localPosition rotation localRotation eulerAngles localEulerAngles localScale; any component type name reads that component (Light, Rigidbody)\n");
            if (type == typeof(Material))
                sb.Append("shader properties: material[\"_BaseColor\"] reads and writes by the shader's own property names\n");

            var members = QueryMembers.ListMembers(type)
                .Where(m => includeInherited || !BaseTypes.Contains(m.DeclaringType!))
                .Where(m => regex == null || regex.IsMatch(m.Name))
                .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
            sb.Append("member | type | access\n");
            foreach (var m in members)
            {
                var accessor = QueryMembers.Find(type, m.Name);
                var valueType = accessor?.ValueType ?? (m is PropertyInfo p ? p.PropertyType : ((FieldInfo)m).FieldType);
                sb.Append(m.Name).Append(" | ").Append(QueryTypes.ShortName(valueType)).Append(" | ").Append(accessor?.Set != null ? "get set" : "get").Append('\n');
            }
            if (members.Count == 0)
                sb.Append("(none").Append(includeInherited ? string.Empty : "; pass includeInherited=true for base class members").Append(")\n");

            if (includeMethods)
            {
                var methods = type.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => !m.IsSpecialName && !m.IsGenericMethodDefinition && (includeInherited || !BaseTypes.Contains(m.DeclaringType!)))
                    .Where(m => m.GetCustomAttribute<ObsoleteAttribute>() == null)
                    .Where(m => regex == null || regex.IsMatch(m.Name))
                    .OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(Signature)
                    .Distinct()
                    .ToList();
                sb.Append("methods (* = callable from rql-query; every method works in rql-mutate 'call'):\n");
                if (methods.Count == 0)
                    sb.Append("(none)\n");
                foreach (var signature in methods.Take(150))
                    sb.Append(signature).Append('\n');
                if (methods.Count > 150)
                    sb.Append($"… {methods.Count - 150} more; narrow with filter\n");
            }
            return sb.ToString().TrimEnd('\n');
        }

        static string Signature(MethodInfo m)
        {
            var parameters = string.Join(", ", m.GetParameters().Select(p => QueryTypes.ShortName(p.ParameterType) + " " + p.Name + (p.IsOptional ? "?" : string.Empty)));
            return $"{(QueryMembers.IsReadingMethod(m) ? "*" : " ")}{m.Name}({parameters}) -> {QueryTypes.ShortName(m.ReturnType)}";
        }

        static string Overview()
        {
            var sb = new StringBuilder(Reference);
            sb.Append("\n\nfunctions:\n");
            foreach (var pair in QueryFunctions.All.OrderBy(p => p.Key))
                sb.Append("  ").Append(pair.Value.Usage).Append('\n');
            List<object?> components;
            try
            {
                components = QuerySources.Components(null);
            }
            catch (Exception)
            {
                return sb.ToString().TrimEnd('\n');
            }
            var counts = components.GroupBy(c => c!.GetType()).OrderByDescending(g => g.Count()).ThenBy(g => g.Key.Name).ToList();
            sb.Append($"\ncomponent types in the open scenes ({components.Count} components):\n");
            sb.Append(string.Join(", ", counts.Take(80).Select(g => $"{g.Key.Name} {g.Count()}")));
            if (counts.Count > 80)
                sb.Append($", … {counts.Count - 80} more");
            return sb.ToString();
        }
    }
}

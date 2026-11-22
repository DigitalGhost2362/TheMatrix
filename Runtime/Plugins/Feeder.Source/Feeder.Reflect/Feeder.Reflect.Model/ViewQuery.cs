using System;
using System.ComponentModel;

namespace Feeder.Reflect.Model
{
public class ViewQuery
{
	[Description("Navigate to this path first, then serialize only that subtree. Path segments are separated by '/'. Use '[i]' for array/list index (e.g. 'users/[2]/name') and '[key]' for dictionary entry (e.g. 'config/[timeout]'). A leading '#/' is stripped automatically. Examples: 'admin/name', 'users/[0]/email', 'config/[timeout]'. Leave null to start from the root object.")]
	public string? Path { get; set; }

	[Description("Case-insensitive .NET regex pattern matched against field and property names. Only branches containing at least one match are kept in the result tree. Examples: 'orbitRadius' (exact name), 'orbit.*' (prefix match), 'radius|speed' (either name). When nothing matches, the root envelope is returned with empty fields/props. Leave null to return all fields and properties without filtering.")]
	public string? NamePattern { get; set; }

	[Description("Maximum nesting depth of the returned serialized tree. 0 = root type name and value only — no nested fields or properties. 1 = one level of fields/props visible, their children stripped. 2 = two levels visible, and so on. Leave null (default) for unlimited depth.")]
	public int? MaxDepth { get; set; }

	[Description("When set, prunes the result tree to members whose runtime type is assignable to this type. Non-matching branches are removed; the root envelope is always preserved. Examples: typeof(float) keeps only float fields, typeof(IEnumerable) keeps only collections. Leave null to include members of any type.")]
	public Type? TypeFilter { get; set; }
}
}

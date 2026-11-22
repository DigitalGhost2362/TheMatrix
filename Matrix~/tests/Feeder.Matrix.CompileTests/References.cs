namespace Feeder.Matrix.CompileTests;

internal static class References
{
    private static readonly string RuntimeDirectory = Path.GetDirectoryName(typeof(object).Assembly.Location)!;

    public static readonly string[] Core =
    [
        typeof(object).Assembly.Location,
        Path.Combine(RuntimeDirectory, "System.Runtime.dll"),
        typeof(Enumerable).Assembly.Location,
        typeof(Console).Assembly.Location,
    ];

    // what the editor sends: every loaded, non-dynamic assembly that has a file
    public static string[] Loaded()
        => [.. AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.Location.Length > 0).Select(a => a.Location)];
}

using System.Reflection;
using System.Text;
using Feeder.Matrix.Compile;
using Feeder.Matrix.Protocol;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Feeder.Matrix.CompileTests;

public sealed class ScriptCompilerTests : IDisposable
{
    private const string CS0127 = "error CS0127: Since 'Script.Main()' returns void, a return keyword must not be followed by an object expression";
    private const string CS0168 = "warning CS0168: The variable 'unused' is declared but never used";

    private readonly DirectoryInfo _temp = Directory.CreateTempSubdirectory("matrix-compile-");

    public void Dispose() => _temp.Delete(recursive: true);

    // expected texts are what the 4.8 compiler bundled in the editor prints for the same source
    public static TheoryData<string, string> EditorErrors => new()
    {
        { "using System;\n\npublic class Script\n{\n    public static void Main()\n    {\n        return 1;\n    }\n}\n", "(7,9): " + CS0127 },
        { "public class Script\n{\n    public static void Main()\n    {\n        int x = ;\n    }\n}\n", "(5,17): error CS1525: Invalid expression term ';'" },
        { "public class Script\n{\n    public static void Main()\n    {\n        var y = missing + 1;\n    }\n}\n", "(5,17): error CS0103: The name 'missing' does not exist in the current context" },
    };

    [Fact]
    public async Task CSharp12_source_compiles_and_runs()
    {
        const string source = """"
            namespace Demo;

            public record Point(int X, int Y);

            public class Greeter(string name)
            {
                public string Hello() => $"hi {name}";
            }

            public static class Script
            {
                public static string Main()
                {
                    int[] values = [1, 2, 3];
                    var p = new Point(1, 2) with { Y = 5 };
                    var where = p switch { { X: > 0, Y: > 4 } => "far", _ => "near" };
                    var shape = values is [1, .., 3] ? "list" : "none";
                    var raw = """
                        raw "quoted"
                        """;
                    return $"{where}|{shape}|{raw}|{new Greeter("x").Hello()}|{p}";
                }
            }
            """";

        var response = await NewCompiler().CompileAsync(Request(source), CancellationToken.None);

        Assert.True(response.Success, string.Join("\n", response.Diagnostics));
        Assert.Equal("far|list|raw \"quoted\"|hi x|Point { X = 1, Y = 5 }", Run(response, "Demo.Script"));
    }

    [Fact]
    public async Task Language_version_is_pinned_to_csharp12()
    {
        var response = await NewCompiler().CompileAsync(
            Request("public static class Script { public static int Count(params System.ReadOnlySpan<int> values) => values.Length; }"),
            CancellationToken.None);

        Assert.False(response.Success);
        Assert.Contains(response.Diagnostics, d => d.Contains("is not available in C# 12.0", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(EditorErrors))]
    public async Task Failure_diagnostics_keep_the_editor_text(string source, string expected)
    {
        var response = await NewCompiler().CompileAsync(Request(source), CancellationToken.None);

        Assert.False(response.Success);
        Assert.Null(response.Assembly);
        Assert.Null(response.Error);
        Assert.Equal(new[] { expected }, response.Diagnostics);
    }

    [Fact]
    public async Task Failure_lists_warnings_and_errors_in_emit_order()
    {
        const string sameMethod = "public class Script\n{\n    public static void Main()\n    {\n        int unused;\n        return 1;\n    }\n}\n";
        const string split = "public class Script\n{\n    public static void Unused()\n    {\n        int unused;\n    }\n\n    public static void Main()\n    {\n        return 1;\n    }\n}\n";
        var compiler = NewCompiler();

        var first = await compiler.CompileAsync(Request(sameMethod), CancellationToken.None);
        var second = await compiler.CompileAsync(Request(split), CancellationToken.None);

        Assert.Equal(new[] { "(6,9): " + CS0127, "(5,13): " + CS0168 }, first.Diagnostics);
        Assert.Equal(new[] { "(5,13): " + CS0168, "(10,9): " + CS0127 }, second.Diagnostics);
    }

    [Fact]
    public async Task Success_returns_the_image_and_no_diagnostics()
    {
        var response = await NewCompiler().CompileAsync(
            Request("public static class Script { public static int Main() { int unused; return 42; } }"),
            CancellationToken.None);

        Assert.True(response.Success);
        Assert.Null(response.Error);
        Assert.Empty(response.Diagnostics);
        Assert.Equal(42, Run(response, "Script"));
    }

    [Fact]
    public async Task Editor_style_reference_list_compiles()
    {
        var response = await NewCompiler().CompileAsync(
            Request("using System.Linq; public static class Script { public static int Main() => new[] { 1, 2, 3 }.Sum(); }", References.Loaded()),
            CancellationToken.None);

        Assert.True(response.Success, string.Join("\n", response.Diagnostics));
        Assert.Equal(6, Run(response, "Script"));
    }

    [Fact]
    public async Task Missing_or_unreadable_references_are_skipped_and_counted_only_on_failure()
    {
        var bogus = Path.Combine(_temp.FullName, "NotAnAssembly.dll");
        await File.WriteAllTextAsync(bogus, "not a pe image");
        string[] references = [.. References.Core, Path.Combine(_temp.FullName, "Missing.dll"), bogus];
        var compiler = NewCompiler();

        var ok = await compiler.CompileAsync(Request("public static class Script { public static int Main() => 1; }", references), CancellationToken.None);
        var failed = await compiler.CompileAsync(Request("public static class Script { public static int Main() => missing; }", references), CancellationToken.None);

        Assert.True(ok.Success);
        Assert.Empty(ok.Diagnostics);
        Assert.False(failed.Success);
        Assert.Equal(2, failed.Diagnostics.Length);
        Assert.Contains("error CS0103", failed.Diagnostics[0]);
        Assert.Equal($"2 of {references.Length} reference paths were missing or unreadable and were skipped.", failed.Diagnostics[1]);
    }

    [Fact]
    public async Task Same_file_or_identity_twice_is_tolerated()
    {
        var linq = typeof(Enumerable).Assembly.Location;
        var copy = Path.Combine(_temp.FullName, Path.GetFileName(linq));
        File.Copy(linq, copy);
        string[] references = [.. References.Core, linq, linq.ToUpperInvariant(), copy];

        var response = await NewCompiler().CompileAsync(
            Request("using System.Linq; public static class Script { public static int Main() => new[] { 1, 2 }.Sum(); }", references),
            CancellationToken.None);

        Assert.True(response.Success, string.Join("\n", response.Diagnostics));
        Assert.Equal(3, Run(response, "Script"));
    }

    [Fact]
    public async Task Same_weak_name_twice_keeps_the_first_instead_of_failing()
    {
        var compiler = NewCompiler();
        var v1 = await BuildLibraryAsync(compiler, "v1", "1.0.0.0", "public static class Thing { public static int V => 1; }");
        var v2 = await BuildLibraryAsync(compiler, "v2", "2.0.0.0", "public static class Thing { public static int V => 2; }");
        const string user = "public static class Script { public static int Main() => Thing.V; }";

        var plain = CSharpCompilation.Create(
            "DynamicAssembly",
            [CSharpSyntaxTree.ParseText(user)],
            [.. References.Core.Append(v1).Append(v2).Select(p => MetadataReference.CreateFromFile(p))],
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        Assert.Contains(plain.GetDiagnostics(), d => d.Id == "CS1704");

        var response = await compiler.CompileAsync(Request(user, [.. References.Core, v1, v2]), CancellationToken.None);

        Assert.True(response.Success, string.Join("\n", response.Diagnostics));
    }

    [Fact]
    public async Task Reference_cache_hits_until_the_file_changes()
    {
        var compiler = NewCompiler();
        var lib = Path.Combine(_temp.FullName, "Lib.dll");
        await File.WriteAllBytesAsync(lib, await ImageAsync(compiler, "Lib", "public static class Lib { public static int One => 1; }"));

        var first = compiler.LoadReference(lib);
        Assert.NotNull(first);
        Assert.Same(first, compiler.LoadReference(lib));
        var before = await compiler.CompileAsync(Request("public static class Script { public static int Main() => Lib.One; }", [.. References.Core, lib]), CancellationToken.None);
        Assert.True(before.Success, string.Join("\n", before.Diagnostics));

        // rewriting the file in place also proves the cached image does not lock it
        await File.WriteAllBytesAsync(lib, await ImageAsync(compiler, "Lib", "public static class Lib { public static int Two => 2; }"));
        File.SetLastWriteTimeUtc(lib, DateTime.UtcNow.AddMinutes(1));

        Assert.NotSame(first, compiler.LoadReference(lib));
        var after = await compiler.CompileAsync(Request("public static class Script { public static int Main() => Lib.Two; }", [.. References.Core, lib]), CancellationToken.None);
        Assert.True(after.Success, string.Join("\n", after.Diagnostics));
    }

    [Fact]
    public async Task Timeout_turns_into_an_error()
    {
        var response = await NewCompiler().CompileAsync(Request(BigSource()) with { TimeoutMs = 1 }, CancellationToken.None);

        Assert.False(response.Success);
        Assert.Null(response.Assembly);
        Assert.Contains("timed out", response.Error);
    }

    [Fact]
    public async Task Caller_cancellation_stops_the_compile()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NewCompiler().CompileAsync(Request(BigSource()), cancel.Token));
    }

    [Fact]
    public async Task Full_queue_refuses_more_work()
    {
        var compiler = NewCompiler(o => { o.CompileConcurrency = 1; o.CompileQueueCapacity = 0; });
        using var cancel = new CancellationTokenSource();
        var running = compiler.CompileAsync(Request(BigSource()), cancel.Token);

        var refused = await compiler.CompileAsync(Request("public static class Script { }"), CancellationToken.None);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        var afterwards = await compiler.CompileAsync(Request("public static class Script { }"), CancellationToken.None);

        Assert.Contains("queue is full", refused.Error);
        Assert.True(afterwards.Success);
    }

    [Fact]
    public async Task Syntax_only_reports_parse_errors_without_compiling()
    {
        var compiler = NewCompiler();

        var broken = await compiler.CompileAsync(new CompileRequest { Source = "public class Script { public static void Main() { int a = 1 } }", SyntaxOnly = true }, CancellationToken.None);
        var semantic = await compiler.CompileAsync(new CompileRequest { Source = "public class Script { public static void Main() { var y = missing; } }", SyntaxOnly = true }, CancellationToken.None);

        Assert.False(broken.Success);
        Assert.Null(broken.Assembly);
        Assert.Equal(new[] { "(1,61): error CS1002: ; expected" }, broken.Diagnostics);
        Assert.True(semantic.Success);
        Assert.Null(semantic.Assembly);
        Assert.Empty(semantic.Diagnostics);
    }

    [Fact]
    public async Task Oversize_requests_are_refused()
    {
        var compiler = NewCompiler(o => { o.CompileMaxSourceChars = 16; o.CompileMaxReferences = 2; });

        var tooLong = await compiler.CompileAsync(new CompileRequest { Source = new string(' ', 17) }, CancellationToken.None);
        var tooMany = await compiler.CompileAsync(new CompileRequest { Source = "class C { }", ReferencePaths = ["a", "b", "c"] }, CancellationToken.None);

        Assert.False(tooLong.Success);
        Assert.Contains("the limit is 16", tooLong.Error);
        Assert.False(tooMany.Success);
        Assert.Contains("the limit is 2", tooMany.Error);
    }

    [Fact]
    public void Defaults_match_the_contract()
    {
        var options = new MatrixOptions();

        Assert.Equal(2, options.CompileConcurrency);
        Assert.Equal(TimeSpan.FromSeconds(60), options.CompileTimeout);
        Assert.Equal(5 * 1024 * 1024, options.CompileMaxSourceChars);
        Assert.Equal(2000, options.CompileMaxReferences);
        Assert.Equal("DynamicAssembly", new CompileRequest { Source = string.Empty }.AssemblyName);
    }

    private static ScriptCompiler NewCompiler(Action<MatrixOptions>? configure = null)
    {
        var options = new MatrixOptions();
        configure?.Invoke(options);
        return new ScriptCompiler(Options.Create(options), NullLogger<ScriptCompiler>.Instance);
    }

    private static CompileRequest Request(string source, string[]? references = null)
        => new() { Source = source, ReferencePaths = references ?? References.Core };

    private static object? Run(CompileResponse response, string typeName)
        => Assembly.Load(response.Assembly!).GetType(typeName, throwOnError: true)!.GetMethod("Main")!.Invoke(null, null);

    // thousands of methods: compiling takes far longer than the timeouts used above
    private static string BigSource()
    {
        var source = new StringBuilder("public static class Big\n{\n");
        for (var i = 0; i < 4000; i++)
            source.Append("    public static int M").Append(i).Append("(int x) { var a = x * ").Append(i).Append(" + 1; var b = a - x; return a + b; }\n");
        return source.Append("}\n").ToString();
    }

    private static async Task<byte[]> ImageAsync(ScriptCompiler compiler, string assemblyName, string source)
    {
        var response = await compiler.CompileAsync(Request(source) with { AssemblyName = assemblyName }, CancellationToken.None);
        Assert.True(response.Success, string.Join("\n", response.Diagnostics));
        return response.Assembly!;
    }

    private async Task<string> BuildLibraryAsync(ScriptCompiler compiler, string folder, string version, string source)
    {
        var path = Path.Combine(_temp.CreateSubdirectory(folder).FullName, "Dup.dll");
        await File.WriteAllBytesAsync(path, await ImageAsync(compiler, "Dup", $"[assembly: System.Reflection.AssemblyVersion(\"{version}\")]\n{source}"));
        return path;
    }
}

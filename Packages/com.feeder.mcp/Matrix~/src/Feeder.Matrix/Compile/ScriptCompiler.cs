using System.Collections.Concurrent;
using Feeder.Matrix.Protocol;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Options;

namespace Feeder.Matrix.Compile;

/// <summary>
/// Compiles C# for the Unity link (script-execute) and returns the emitted assembly. The matrix
/// never loads or runs the result; the editor does.
/// </summary>
public sealed class ScriptCompiler(IOptions<MatrixOptions> options, ILogger<ScriptCompiler> logger)
{
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp12);
    private static readonly CSharpCompilationOptions CompilationOptions = new(OutputKind.DynamicallyLinkedLibrary);

    private readonly MatrixOptions _settings = options.Value;
    private readonly SemaphoreSlim _slots = new(options.Value.CompileConcurrency);
    private readonly ConcurrentDictionary<string, CachedReference> _references = new(StringComparer.OrdinalIgnoreCase);
    private int _admitted;

    public async Task<CompileResponse> CompileAsync(CompileRequest request, CancellationToken cancellationToken)
    {
        var source = request.Source ?? string.Empty;
        var paths = request.ReferencePaths ?? [];
        if (source.Length > _settings.CompileMaxSourceChars)
            return Failure($"Source has {source.Length} characters; the limit is {_settings.CompileMaxSourceChars}.");
        if (paths.Length > _settings.CompileMaxReferences)
            return Failure($"{paths.Length} reference paths were sent; the limit is {_settings.CompileMaxReferences}.");

        if (Interlocked.Increment(ref _admitted) > _settings.CompileConcurrency + _settings.CompileQueueCapacity)
        {
            Interlocked.Decrement(ref _admitted);
            logger.LogWarning("Compile refused: queue full");
            return Failure("The compile queue is full; retry shortly.");
        }

        var timeout = request.TimeoutMs > 0 ? TimeSpan.FromMilliseconds(request.TimeoutMs) : _settings.CompileTimeout;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            await _slots.WaitAsync(deadline.Token);
            try
            {
                return await Task.Run(() => Compile(request, source, paths, deadline.Token), deadline.Token);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Compile timed out after {Timeout}", timeout);
            return Failure($"Compilation timed out after {timeout.TotalSeconds:0.###} s.");
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Compile failed unexpectedly");
            return Failure($"Compiler failure: {e.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _admitted);
        }
    }

    private CompileResponse Compile(CompileRequest request, string source, string[] paths, CancellationToken cancellationToken)
    {
        var tree = CSharpSyntaxTree.ParseText(source, ParseOptions, cancellationToken: cancellationToken);
        if (request.SyntaxOnly)
        {
            string[] errors = [.. tree.GetDiagnostics(cancellationToken).Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString())];
            return new CompileResponse { Success = errors.Length == 0, Diagnostics = errors };
        }

        var (references, skipped) = ResolveReferences(paths, cancellationToken);
        var compilation = CSharpCompilation.Create(
            string.IsNullOrEmpty(request.AssemblyName) ? FmpConstants.DefaultCompileAssemblyName : request.AssemblyName,
            [tree],
            references,
            CompilationOptions);

        using var image = new MemoryStream();
        var result = compilation.Emit(image, cancellationToken: cancellationToken);
        if (result.Success)
            return new CompileResponse { Success = true, Assembly = image.ToArray() };

        var diagnostics = result.Diagnostics.Select(d => d.ToString());
        return new CompileResponse
        {
            Diagnostics = skipped == 0
                ? [.. diagnostics]
                : [.. diagnostics, $"{skipped} of {paths.Length} reference paths were missing or unreadable and were skipped."],
        };
    }

    private (List<MetadataReference> References, int Skipped) ResolveReferences(string[] paths, CancellationToken cancellationToken)
    {
        var references = new List<MetadataReference>(paths.Length);
        var assemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var skipped = 0;
        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cached = LoadReference(path);
            if (cached is null)
                skipped++;
            else if (assemblies.Add(cached.AssemblyKey))
                references.Add(cached.Reference);
        }
        return (references, skipped);
    }

    internal CachedReference? LoadReference(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
            {
                _references.TryRemove(file.FullName, out _);
                return null;
            }
            if (_references.TryGetValue(file.FullName, out var cached) &&
                cached.LastWriteUtc == file.LastWriteTimeUtc && cached.Length == file.Length)
                return cached;

            // createfromfile copies the whole image into memory, so the editor can still rewrite the dll
            var reference = MetadataReference.CreateFromFile(file.FullName);
            var name = ((AssemblyMetadata)reference.GetMetadata()).GetModules()[0]
                .GetMetadataReader().GetAssemblyDefinition().GetAssemblyName();
            // roslyn keeps one weak-named assembly per simple name (cs1704) and merges equal strong identities
            var key = name.GetPublicKeyToken() is { Length: > 0 } ? name.FullName : name.Name!;
            return _references[file.FullName] = new CachedReference(file.LastWriteTimeUtc, file.Length, reference, key);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static CompileResponse Failure(string error) => new() { Error = error };

    internal sealed record CachedReference(DateTime LastWriteUtc, long Length, MetadataReference Reference, string AssemblyKey);
}

# ADR-0006: Script compile on the matrix

Status: Accepted - 2026-10-06

## Context

`script-execute` compiled C# inside the editor with a compiler the Unity package shipped as five precompiled assemblies (about 14 MB, most of the package's binary weight). Compiling ran on the editor main thread without timeout or cancellation, and the reference list was rebuilt after every domain reload. The package is moving to zero precompiled assemblies.

## Decision

- The matrix carries Roslyn (`Microsoft.CodeAnalysis.CSharp` 5.6.0, the newest release from the compiler's stable branch on 2026-10-06; 5.9.0 exists but was built from the insiders (preview) branch and depends on a prerelease package) and serves the FMP hub method `Compile` ([protocol section 7](../protocol/feeder-matrix-protocol-v1.md)). The editor sends the source and its reference paths, receives a PE image and diagnostics, then loads and runs the image as before. The matrix never loads or runs compiled code.
- Behaviour is pinned to what the editor did: C# 12, `OutputKind.DynamicallyLinkedLibrary` with default options, no PDB, no preprocessor symbols, diagnostics formatted by `Diagnostic.ToString()` relative to the submitted source. A `syntaxOnly` mode replaces the editor's local syntax check.
- Only a link that completed `Register` may compile. At most 2 compiles run at once and 16 more may wait; each has a 60 s deadline (queueing included) that the request can override; a dropped connection cancels its compile; source size and reference count are capped.
- Reference metadata is cached per path and reloaded when the file's last write time or length changes, so it survives editor domain reloads. Images are copied into memory, so the editor can keep rewriting its assemblies.
- Duplicate references are dropped before compiling (the first one wins) instead of failing the compile.
- The register response advertises `"compile"` in a new `capabilities` array. The protocol stays at v1 (additive change).

## Consequences

- `script-execute` and the syntax check of the script tools need a running matrix that advertises `compile`; the editor must report a clear error otherwise.
- The self-contained executable grows by about 10 MB (106,508,876 to 116,633,080 bytes, same SDK and publish flags, measured 2026-10-06). Only English compiler resources ship, since the matrix runs with invariant globalization; all locales would add another 6.8 MB.
- The matrix holds the images of the referenced assemblies in memory while it runs, roughly the size of the editor's loaded assemblies.
- Compiling leaves the editor main thread; only loading and running the result stays there. The first compile after the matrix starts pays the compiler warm-up (about 1.5 s measured with a small reference set); later compiles reuse the warm compiler and the cached references.
- Upgrading the compiler package does not change the language: C# 12 stays pinned until this decision is revised.
- A compile occupies its link's hub call slot, so later messages from that link wait until it finishes.

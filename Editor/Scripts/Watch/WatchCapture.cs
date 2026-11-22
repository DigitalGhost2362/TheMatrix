#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Feeder.Logging;
using Feeder.MCP.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using ILogger = Feeder.Logging.ILogger;

namespace Feeder.MCP.Editor.Watch
{
    // small JPG frames read back from the Game View without stalling the game, and 3x3 contact sheets
    internal static class WatchCapture
    {
        public const int FrameWidth = 640;
        public const int SheetColumns = 3;
        public const int SheetCells = 9;
        const int CellWidth = 426;
        const int Gap = 2;

        sealed class Pending
        {
            public AsyncGPUReadbackRequest Request;
            public RenderTexture Target = null!;
            public string Path = string.Empty;
            public int Width;
            public int Height;
        }

        static readonly Type? _gameViewType = Type.GetType("UnityEditor.GameView,UnityEditor");
        static readonly FieldInfo? _textureField = FindField(_gameViewType, "m_RenderTexture");
        static readonly ILogger _logger = UnityLoggerFactory.LoggerFactory.CreateLogger(nameof(WatchCapture));
        static readonly List<Pending> _pending = new List<Pending>();
        static readonly List<Task> _writes = new List<Task>();

        public static bool Capture(string path)
        {
            var source = GameViewTexture();
            if (source == null)
                return false;

            var width = Mathf.Min(FrameWidth, source.width);
            var height = Mathf.Max(1, Mathf.RoundToInt(source.height * (float)width / source.width));
            var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            // the game view texture is stored upside down on top-left-origin graphics APIs
            if (SystemInfo.graphicsUVStartsAtTop)
                Graphics.Blit(source, target, new Vector2(1f, -1f), new Vector2(0f, 1f));
            else
                Graphics.Blit(source, target);
            RenderTexture.active = previous;

            _pending.Add(new Pending
            {
                Request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32),
                Target = target,
                Path = path,
                Width = width,
                Height = height
            });
            return true;
        }

        public static void ProcessDone()
        {
            for (var i = _pending.Count - 1; i >= 0; i--)
            {
                if (!_pending[i].Request.done)
                    continue;
                Complete(_pending[i]);
                _pending.RemoveAt(i);
            }
            _writes.RemoveAll(task => task.IsCompleted);
        }

        public static void Flush()
        {
            foreach (var pending in _pending)
            {
                pending.Request.WaitForCompletion();
                Complete(pending);
            }
            _pending.Clear();
            if (_writes.Count > 0 && !Task.WaitAll(_writes.ToArray(), 5000))
                _logger.LogWarning("{method} some frames were still being written after 5 s.", nameof(Flush));
            _writes.Clear();
        }

        // one sheet per 9 frames in order; a frame that is missing keeps its cell, drawn grey
        public static List<(string Sheet, List<string> Frames)> BuildSheets(string folder, IReadOnlyList<string> frames)
        {
            var sheets = new List<(string, List<string>)>();
            if (frames.Count == 0)
                return sheets;
            Directory.CreateDirectory(Path.Combine(folder, "sheets"));

            var previous = RenderTexture.active;
            try
            {
                for (var start = 0; start < frames.Count; start += SheetCells)
                {
                    var name = $"sheets/sheet-{start / SheetCells + 1:00}.jpg";
                    var cells = frames.Skip(start).Take(SheetCells).ToList();
                    if (WriteSheet(folder, cells, name))
                        sheets.Add((name, cells));
                }
            }
            finally
            {
                RenderTexture.active = previous;
            }
            return sheets;
        }

        static bool WriteSheet(string folder, List<string> frames, string name)
        {
            var textures = new List<Texture2D?>();
            Texture2D? sheet = null;
            try
            {
                foreach (var frame in frames)
                {
                    var path = Path.Combine(folder, frame);
                    var texture = new Texture2D(2, 2);
                    if (File.Exists(path) && texture.LoadImage(File.ReadAllBytes(path)))
                    {
                        textures.Add(texture);
                        continue;
                    }
                    UnityEngine.Object.DestroyImmediate(texture);
                    textures.Add(null);
                }
                var first = textures.FirstOrDefault(texture => texture != null);
                if (first == null)
                    return false;

                var cellHeight = Mathf.Max(1, Mathf.RoundToInt(first.height * (float)CellWidth / first.width));
                var rows = (textures.Count + SheetColumns - 1) / SheetColumns;
                var sheetWidth = SheetColumns * CellWidth + (SheetColumns - 1) * Gap;
                var sheetHeight = rows * cellHeight + (rows - 1) * Gap;
                sheet = new Texture2D(sheetWidth, sheetHeight, TextureFormat.RGB24, false);
                sheet.SetPixels32(Enumerable.Repeat(new Color32(0, 0, 0, 255), sheetWidth * sheetHeight).ToArray());

                var cell = RenderTexture.GetTemporary(CellWidth, cellHeight, 0, RenderTextureFormat.ARGB32);
                try
                {
                    for (var i = 0; i < textures.Count; i++)
                    {
                        var column = i % SheetColumns;
                        var row = i / SheetColumns;
                        var texture = textures[i];
                        Graphics.Blit(texture != null ? texture : Texture2D.grayTexture, cell);
                        RenderTexture.active = cell;
                        sheet.ReadPixels(new Rect(0, 0, CellWidth, cellHeight),
                            column * (CellWidth + Gap), (rows - 1 - row) * (cellHeight + Gap));
                    }
                }
                finally
                {
                    RenderTexture.ReleaseTemporary(cell);
                }
                sheet.Apply();
                File.WriteAllBytes(Path.Combine(folder, name), sheet.EncodeToJPG(80));
                return true;
            }
            finally
            {
                foreach (var texture in textures)
                    if (texture != null)
                        UnityEngine.Object.DestroyImmediate(texture);
                if (sheet != null)
                    UnityEngine.Object.DestroyImmediate(sheet);
            }
        }

        static void Complete(Pending pending)
        {
            try
            {
                if (pending.Request.hasError)
                    return;
                var data = pending.Request.GetData<byte>().ToArray();
                var path = pending.Path;
                var width = (uint)pending.Width;
                var height = (uint)pending.Height;
                _writes.Add(Task.Run(() =>
                {
                    try
                    {
                        File.WriteAllBytes(path, ImageConversion.EncodeArrayToJPG(data, GraphicsFormat.R8G8B8A8_UNorm, width, height, 0, 75));
                    }
                    catch (Exception e)
                    {
                        _logger.LogWarning("{method} could not write {path}: {error}", nameof(Complete), path, e.Message);
                    }
                }));
            }
            finally
            {
                RenderTexture.ReleaseTemporary(pending.Target);
            }
        }

        static RenderTexture? GameViewTexture()
        {
            if (_gameViewType == null || _textureField == null)
                return null;
            RenderTexture? best = null;
            foreach (var view in Resources.FindObjectsOfTypeAll(_gameViewType))
            {
                if (_textureField.GetValue(view) is RenderTexture texture && texture.IsCreated() &&
                    (best == null || texture.width * texture.height > best.width * best.height))
                    best = texture;
            }
            return best;
        }

        static FieldInfo? FindField(Type? type, string name)
        {
            for (; type != null; type = type.BaseType)
            {
                var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
            }
            return null;
        }
    }
}

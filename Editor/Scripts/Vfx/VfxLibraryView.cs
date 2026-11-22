#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using IoPath = System.IO.Path;

namespace Feeder.MCP.Editor.Vfx
{
    [Serializable]
    sealed class VfxLibraryView : IDisposable
    {
        const float BarHeight = 24f;
        const float TreeWidth = 220f;
        const float RowHeight = 22f;
        const float CellMin = 118f;
        const float Gap = 10f;
        const float ScrollbarWidth = 15f;

        sealed class Cell
        {
            public string Path = "";
            public string Name = "";
            public string Sub = "";
            public VfxLibrary.Item? Item;
        }

        struct GridLayout
        {
            public int Cols;
            public float Cell;
            public float CellHeight;
            public float Width;
            public float Height;
        }

        [SerializeField] string folder = VfxLibrary.RootFolder;
        [SerializeField] string selected = "";
        [SerializeField] string search = "";
        [SerializeField] Vector2 treeScroll;
        [SerializeField] Vector2 gridScroll;

        [NonSerialized] SearchField? searchField;
        [NonSerialized] VfxThumbnails? thumbnails;
        [NonSerialized] GUIStyle? link;
        [NonSerialized] GUIStyle? nameCenter;
        [NonSerialized] GUIStyle? miniCenter;
        [NonSerialized] public Action? OnRefresh;

        public bool HasPending => thumbnails != null && thumbnails.HasPending;

        GUIStyle Link => link ??= new GUIStyle(EditorStyles.label)
        {
            normal = { textColor = VfxUi.Link }, hover = { textColor = Color.white }, padding = new RectOffset(0, 0, 0, 0)
        };

        GUIStyle NameCenter => nameCenter ??= new GUIStyle(VfxUi.Name) { alignment = TextAnchor.UpperCenter };

        GUIStyle MiniCenter => miniCenter ??= new GUIStyle(VfxUi.Mini) { alignment = TextAnchor.UpperCenter };

        public bool Pump() => thumbnails != null && thumbnails.Pump();

        public void Prune(VfxLibrary library) => VfxThumbnails.Prune(library.Items);

        public void Forget(VfxLibrary.Item item) => thumbnails?.Forget(item);

        public void Dispose()
        {
            thumbnails?.Dispose();
            thumbnails = null;
        }

        public VfxLibrary.Item? Draw(Rect area, VfxLibrary library)
        {
            thumbnails ??= new VfxThumbnails();
            searchField ??= new SearchField();
            if (folder != VfxLibrary.RootFolder && library.CountIn(folder) == 0) folder = VfxLibrary.RootFolder;
            DrawBar(new Rect(area.x, area.y, area.width, BarHeight), library);
            var body = new Rect(area.x, area.y + BarHeight, area.width, area.height - BarHeight);
            var tree = new Rect(body.x, body.y, TreeWidth, body.height);
            DrawTree(tree, library);
            return DrawRight(new Rect(tree.xMax, body.y, body.width - TreeWidth, body.height), library);
        }

        void DrawBar(Rect bar, VfxLibrary library)
        {
            VfxUi.Fill(bar, VfxUi.Bar);
            VfxUi.BottomLine(bar);
            search = searchField!.OnToolbarGUI(new Rect(bar.x + 6f, bar.y + 3f, 240f, 18f), search);
            var refresh = new Rect(bar.xMax - 66f, bar.y + 2f, 60f, 20f);
            if (GUI.Button(refresh, "Refresh", EditorStyles.miniButton)) OnRefresh?.Invoke();
            var scan = library.ScanSeconds.ToString("0.0#", CultureInfo.InvariantCulture);
            VfxUi.Label(new Rect(refresh.x - 250f, bar.y + 3f, 244f, 18f), $"{library.Items.Count} prefab VFX · quét {scan}s", VfxUi.MiniRight, true);
        }

        void DrawTree(Rect r, VfxLibrary library)
        {
            var view = new Rect(0f, 0f, r.width - ScrollbarWidth, library.Folders.Count * RowHeight);
            if (view.height <= r.height) view.width = r.width;
            treeScroll = GUI.BeginScrollView(r, treeScroll, view);
            var icon = VfxUi.Icon("Folder Icon");
            for (var i = 0; i < library.Folders.Count; i++)
            {
                var f = library.Folders[i];
                var row = new Rect(0f, i * RowHeight, view.width, RowHeight);
                VfxUi.Row(row, search.Length == 0 && f.Path == folder);
                var x = 6f + f.Depth * 14f;
                if (icon != null) GUI.DrawTexture(new Rect(x, row.y + 3f, 16f, 16f), icon, ScaleMode.ScaleToFit);
                VfxUi.Label(new Rect(x + 20f, row.y, row.width - x - 70f, RowHeight), IoPath.GetFileName(f.Path), VfxUi.Name);
                VfxUi.Label(new Rect(row.xMax - 46f, row.y, 40f, RowHeight), f.Count.ToString(CultureInfo.InvariantCulture), VfxUi.MiniRight, true);
                if (!VfxUi.Click(row, out _)) continue;
                folder = f.Path;
                selected = "";
                search = "";
            }
            GUI.EndScrollView();
            VfxUi.RightLine(r);
        }

        VfxLibrary.Item? DrawRight(Rect r, VfxLibrary library)
        {
            var head = new Rect(r.x, r.y, r.width, BarHeight);
            VfxUi.Fill(head, VfxUi.Header);
            VfxUi.BottomLine(head);
            var open = DrawHead(head, library);
            DrawGrid(new Rect(r.x, head.yMax, r.width, r.height - BarHeight), BuildCells(library), ref open);
            return open;
        }

        VfxLibrary.Item? DrawHead(Rect head, VfxLibrary library)
        {
            var x = head.x + 8f;
            if (search.Length > 0) VfxUi.Label(new Rect(x, head.y, 300f, head.height), $"Kết quả cho \"{search}\"", VfxUi.Name);
            else
            {
                var parts = folder.Split('/');
                for (var i = 0; i < parts.Length; i++)
                {
                    var width = Link.CalcSize(VfxUi.Content(parts[i])).x;
                    if (GUI.Button(new Rect(x, head.y + 2f, width, head.height - 4f), VfxUi.Content(parts[i]), Link))
                        folder = string.Join("/", parts.Take(i + 1));
                    x += width;
                    if (i == parts.Length - 1) break;
                    VfxUi.Label(new Rect(x, head.y, 14f, head.height), "/", VfxUi.Mini, true);
                    x += 14f;
                }
            }
            var chosen = library.Find(selected);
            var openButton = new Rect(head.xMax - 66f, head.y + 2f, 60f, head.height - 4f);
            VfxUi.Label(new Rect(openButton.x - 200f, head.y, 194f, head.height), "bấm đúp prefab để mở", VfxUi.MiniRight, true);
            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = VfxUi.Primary;
            var pressed = false;
            using (new EditorGUI.DisabledScope(chosen == null))
                pressed = GUI.Button(openButton, "Mở ▸", EditorStyles.miniButton);
            GUI.backgroundColor = previousColor;
            return pressed ? chosen : null;
        }

        List<Cell> BuildCells(VfxLibrary library)
        {
            var cells = new List<Cell>();
            if (search.Length > 0)
            {
                cells.AddRange(library.Items.Where(i => i.Name.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).Select(ItemCell));
                return cells;
            }
            foreach (var sub in library.SubFolders(folder))
                cells.Add(new Cell { Path = sub, Name = IoPath.GetFileName(sub), Sub = $"{library.CountIn(sub)} VFX" });
            cells.AddRange(library.ItemsIn(folder).Select(ItemCell));
            return cells;
        }

        static Cell ItemCell(VfxLibrary.Item item) => new Cell { Path = item.Path, Name = item.Name, Sub = $"{item.Emitters} emitter", Item = item };

        static GridLayout Layout(float width, int count)
        {
            var cols = Mathf.Max(1, Mathf.FloorToInt((width - Gap) / (CellMin + Gap)));
            var cell = (width - Gap - cols * Gap) / cols;
            var cellHeight = cell + 34f;
            return new GridLayout
            {
                Cols = cols, Cell = cell, CellHeight = cellHeight, Width = width,
                Height = Gap + Mathf.CeilToInt(count / (float)cols) * (cellHeight + Gap)
            };
        }

        void DrawGrid(Rect r, List<Cell> cells, ref VfxLibrary.Item? open)
        {
            if (cells.Count == 0)
            {
                VfxUi.Label(new Rect(r.x, r.y + 30f, r.width, 20f), "Thư mục trống", MiniCenter, true);
                return;
            }
            var layout = Layout(r.width, cells.Count);
            if (layout.Height > r.height) layout = Layout(r.width - ScrollbarWidth, cells.Count);
            gridScroll = GUI.BeginScrollView(r, gridScroll, new Rect(0f, 0f, layout.Width, layout.Height));
            for (var i = 0; i < cells.Count; i++)
            {
                var cell = new Rect(
                    Gap + i % layout.Cols * (layout.Cell + Gap), Gap + i / layout.Cols * (layout.CellHeight + Gap),
                    layout.Cell, layout.CellHeight);
                if (cell.yMax >= gridScroll.y && cell.y <= gridScroll.y + r.height) DrawCell(cell, cells[i], ref open);
            }
            GUI.EndScrollView();
        }

        void DrawCell(Rect cell, Cell data, ref VfxLibrary.Item? open)
        {
            var isSelected = selected == data.Path;
            VfxUi.Row(cell, isSelected);
            if (isSelected) VfxUi.Outline(cell, VfxUi.Accent);
            var box = new Rect(cell.x + 5f, cell.y + 5f, cell.width - 10f, cell.width - 10f);
            if (data.Item == null) DrawFolderBox(box);
            else DrawThumbnail(box, data.Item);
            VfxUi.Label(new Rect(box.x, box.yMax + 2f, box.width, 16f), data.Name, NameCenter);
            VfxUi.Label(new Rect(box.x, box.yMax + 17f, box.width, 14f), data.Sub, MiniCenter, true);
            if (!VfxUi.Click(cell, out var clicks)) return;
            selected = data.Path;
            if (clicks < 2) return;
            if (data.Item != null) open = data.Item;
            else
            {
                folder = data.Path;
                selected = "";
            }
        }

        static void DrawFolderBox(Rect box)
        {
            VfxUi.Fill(box, VfxUi.GroupBar);
            var icon = VfxUi.Icon("Folder Icon");
            var size = box.width * 0.5f;
            if (icon != null) GUI.DrawTexture(new Rect(box.center.x - size / 2f, box.center.y - size / 2f, size, size), icon, ScaleMode.ScaleToFit);
        }

        void DrawThumbnail(Rect box, VfxLibrary.Item item)
        {
            VfxUi.Fill(box, VfxUi.Backdrop);
            var thumbnail = thumbnails!.Get(item);
            if (thumbnail != null) GUI.DrawTexture(box, thumbnail, ScaleMode.ScaleToFit, true);
        }
    }
}

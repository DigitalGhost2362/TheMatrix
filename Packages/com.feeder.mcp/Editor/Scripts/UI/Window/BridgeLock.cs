#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Feeder.MCP.Editor.Matrix;
using Feeder.MCP.Editor.Utils;
using Feeder.Rx;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.UI
{
    /// <summary>
    /// The editor lock as the matrix reports it: which agent session (or lock file) holds the Unity editor,
    /// for what and since when, plus the menu to free a claim.
    /// </summary>
    internal static class BridgeLock
    {
        public const string KindAgent = "agent";
        public const string KindOutside = "outside";

        static readonly TimeSpan Cap = TimeSpan.FromMinutes(15);

        // null: not linked, or the server does not keep the lock
        static readonly ReactiveProperty<EditorLockState?> _state = new(null);

        public static ReadOnlyReactiveProperty<EditorLockState?> State => _state;

        internal static void Publish(EditorLockState? state) => _state.Value = state;

        public static bool IsPastCap(EditorLockState state) => state.Held && Age(state.AcquiredUnixMs) > Cap;

        /// <summary>The holder is past the cap while others wait for the editor.</summary>
        public static bool IsBlocking(EditorLockState state) => IsPastCap(state) && state.Waiting.Length > 0;

        /// <summary>One short line: who, for what, for how long.</summary>
        public static string Line(EditorLockState state)
        {
            if (!state.Held)
                return state.Waiting.Length > 0 ? $"editor free  -  {state.Waiting.Length} waiting" : "editor free";
            var age = Format(Age(state.AcquiredUnixMs));
            var blocking = IsBlocking(state) ? $"  -  OVER THE CAP, {state.Waiting.Length} waiting" : string.Empty;
            return state.Kind switch
            {
                KindOutside => $"{Name(state)} (lock file)  -  {Task(state)}{age}{blocking}",
                _ => $"{Name(state)}  -  {Task(state)}{age}{blocking}",
            };
        }

        public static string Tooltip(EditorLockState state)
        {
            if (!state.Held)
                return "No agent holds the Unity editor: it is yours. Agents claim it for each compile or play test.";

            var text = new StringBuilder();
            text.AppendLine(state.Kind switch
            {
                KindOutside => $"Held by {Name(state)} through Temp/agent-editor-lock (a session claiming with the lock file).",
                _ => $"Held by agent {Name(state)}. Other sessions only run read-only tools.",
            });
            var client = $"{state.ClientName} {state.ClientVersion}".Trim();
            if (client.Length > 0)
                text.AppendLine($"Client: {client}" + (state.SessionId.Length > 0 ? $", session {Short(state.SessionId)}" : string.Empty));
            if (state.Task.Length > 0)
                text.AppendLine($"Task: {state.Task}");
            text.Append($"Since: {Clock(state.AcquiredUnixMs)} ({Format(Age(state.AcquiredUnixMs))})");
            text.AppendLine(IsPastCap(state) ? ", past the 15-minute cap" : string.Empty);
            if (state.Kind == KindAgent)
                text.AppendLine(state.CallsInFlight > 0
                    ? $"Running: {state.CallsInFlight} call(s)"
                    : $"Last call: {Format(Age(state.LastActivityUnixMs))} ago");
            else if (state.Kind == KindOutside && state.LastActivityUnixMs > 0)
                text.AppendLine($"Last heartbeat: {Format(Age(state.LastActivityUnixMs))} ago");
            if (state.ExpiresUnixMs > 0)
                text.AppendLine($"Freed on its own at {Clock(state.ExpiresUnixMs)} if it stays idle");
            if (state.Subagent)
                text.AppendLine($"A subagent{(state.AgentType.Length > 0 ? $" ({state.AgentType})" : string.Empty)}: it claims for itself, not for its session.");
            else if (state.Kind == KindAgent && !state.Hooked)
                text.AppendLine("This session does not report its subagents, so any it has share this claim.");
            if (state.Waiting.Length > 0)
                text.AppendLine($"Waiting: {state.Waiting.Length} (the first gets the editor when it is freed)");
            text.Append("Click for options.");
            return text.ToString();
        }

        public static void ShowMenu(Rect at)
        {
            var state = _state.Value;
            var menu = new GenericMenu();
            if (state == null || (!state.Held && state.Waiting.Length == 0 && !state.QueuePaused))
                return;
            if (state.Held)
                menu.AddItem(new GUIContent($"Free the claim of {Name(state)}..."), false, ConfirmRelease);
            menu.AddItem(new GUIContent(state.QueuePaused ? "Resume the queue" : "Pause the queue"), false, () => SetPaused(!state.QueuePaused));
            menu.AddItem(new GUIContent("Copy details"), false, CopyDetails);
            menu.DropDown(at);
        }

        static void ConfirmRelease()
        {
            var state = _state.Value;
            if (state == null || !state.Held)
                return;
            var ok = EditorUtility.DisplayDialog(
                "Free the editor lock",
                $"{Name(state)} holds the editor for \"{state.Task}\".\n\nIt may be in the middle of a compile or a play test. " +
                "Freeing the claim lets other sessions change the editor right away.",
                "Free it",
                "Cancel");
            if (ok)
                Release();
        }

        static async void Release()
        {
            try
            {
                await MatrixPluginEditor.Instance.ReleaseEditorLockAsync();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Bridge] Could not free the editor lock: {e.Message}");
            }
        }

        static async void SetPaused(bool paused)
        {
            try
            {
                await MatrixPluginEditor.Instance.SetEditorLockQueuePausedAsync(paused);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Bridge] Could not {(paused ? "pause" : "resume")} the editor lock queue: {e.Message}");
            }
        }

        static async void Remove(EditorLockWaiter waiter)
        {
            try
            {
                await MatrixPluginEditor.Instance.RemoveFromEditorLockQueueAsync(waiter.Ticket);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Bridge] Could not remove {waiter.Owner} from the editor lock queue: {e.Message}");
            }
        }

        static async void SkipAfter(EditorLockWaiter waiter)
        {
            try
            {
                await MatrixPluginEditor.Instance.SkipEditorLockAfterAsync(waiter.Ticket);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Matrix Bridge] Could not let {waiter.Owner} go ahead: {e.Message}");
            }
        }

        static void ShowWaiterMenu(EditorLockWaiter waiter, Rect at)
        {
            var who = waiter.Owner.Length > 0 ? waiter.Owner : "a session";
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent($"Let {who} go now (it waits for {waiter.After})"), false, () => SkipAfter(waiter));
            menu.AddItem(new GUIContent($"Take {who} out of the queue"), false, () => Remove(waiter));
            menu.DropDown(at);
        }

        // as in MatrixBridge.uss: the track and the step from one row of cars to the next, without and with pets
        const float TrackHeight = 70f;
        const float PetTrackHeight = 88f;
        const float RowPitch = 52f;
        const float PetRowPitch = 82f;
        const int MaxRows = 3;
        const float CarWidth = 118f;
        const float CarPitch = CarWidth + 10f;
        const float EnterFrom = 140f;
        const float LeaveTo = -36f;
        const long StaggerMs = 60;
        const long LeaveMs = 300;
        const long PopMs = 200;
        const int RainColumns = 6;
        const int RainRows = 16;
        const float RainRowHeight = 10f;
        const float RainColumnPitch = 19f;
        const long RainStepMs = 40;
        const float DropTravel = 36f;
        const float DropMs = 1100f;
        const float DropStepMs = 170f;
        const float DropDelayMs = 260f;
        const int GlowSize = 12;
        static readonly Color Green = new(0f, 1f, 65f / 255f);
        static readonly Color Amber = new(1f, 196f / 255f, 64f / 255f);
        const int DecodeSteps = 6;
        const long DecodeStepMs = 60;

        // the atlas holds one baked animation per row, up to 20 frames of 72px; a pet shows at half size.
        // each pose group lists the rows a pet picks from, and a pet picks again every few seconds
        const string PetAtlasPath = "Editor/UI/Pets/clawd-pets.png";
        const int PetColumns = 20;
        const float PetSize = 36f;
        const double CheerSeconds = 1.5;
        const double PetPhaseSeconds = 10, PetSwapMinSeconds = 8, PetSwapMaxSeconds = 14;
        const int PetTyping = 0, PetBuilding = 1, PetGaming = 2, PetTesting = 3, PetLoading = 4, PetCarrying = 5,
            PetCelebrating = 6, PetOverheated = 7, PetAngry = 8, PetIdle = 9, PetYawning = 10, PetSleeping = 11, PetCoffee = 12;
        static readonly int[][] PetGroups =
        {
            new[] { 0, 1, 2, 3, 4, 5, 6 }, new[] { 7, 8 }, new[] { 9, 10, 11 }, new[] { 12, 13, 14 }, new[] { 15, 16 },
            new[] { 17, 18 }, new[] { 19, 20, 21, 22 }, new[] { 23, 24 }, new[] { 25, 26 }, new[] { 27, 28, 29, 30, 31 },
            new[] { 32, 33, 34 }, new[] { 35 }, new[] { 36, 37 },
        };
        static readonly (int Frames, int Ms)[] PetFrames =
        {
            (15, 100), (20, 200), (20, 200), (20, 200), (12, 100), (20, 150), (20, 150), (10, 100), (20, 150), (20, 200),
            (20, 125), (20, 200), (20, 150), (20, 150), (20, 200), (20, 150), (20, 150), (20, 100), (20, 150), (20, 100),
            (20, 150), (20, 150), (20, 150), (20, 150), (20, 120), (10, 100), (20, 150), (20, 800), (20, 250), (20, 150),
            (20, 200), (20, 150), (20, 150), (20, 300), (20, 200), (20, 300), (20, 200), (20, 100),
        };
        static Texture2D? _petAtlas;
        static bool _petAtlasMissing;

        sealed class Car
        {
            public readonly VisualElement Root = new();
            public readonly Label Name = new();
            public readonly Label Sub = new();
            public readonly Label Remove = new("X");
            public readonly VisualElement Glow = new() { pickingMode = PickingMode.Ignore };
            public readonly VisualElement Clip = new() { pickingMode = PickingMode.Ignore };
            public readonly Label[] Rain = new Label[RainColumns];
            public readonly float[] RainY = new float[RainColumns];
            public readonly float[] RainSpeed = new float[RainColumns];
            public readonly VisualElement Pet = new() { pickingMode = PickingMode.Ignore };
            public readonly VisualElement PetSheet = new() { pickingMode = PickingMode.Ignore };
            public bool HasPet;
            public double CheerUntil;
            public readonly double PetPhase = _rng.NextDouble() * PetPhaseSeconds;
            public int PetGroup = -1;
            public int PetRow;
            public double PetSwapAt;
            public EditorLockWaiter? Waiter;
            public string NameText = string.Empty;
            public string SubText = string.Empty;
            public int Decode;
            public float X = float.NaN;
            public float Y;
            public bool Entering;
        }

        static readonly System.Random _rng = new();

        static VisualElement? _queueView;
        static VisualElement? _header;
        static Label? _title;
        static Label? _pause;
        static VisualElement? _track;
        static VisualElement? _rail;
        static Label? _lockLabel;
        static Label? _queueLabel;
        static readonly List<VisualElement> _drops = new();
        static Texture2D? _railTexture, _tintGreen, _tintAmber, _dropTexture, _glowTexture;
        static readonly Dictionary<string, Car> _cars = new();
        static readonly List<Car> _order = new();
        // owners more than one car goes by: those cars also show the end of their id
        static readonly HashSet<string> _shared = new();
        static VisualElement? _more;
        static Label? _moreLabel;

        /// <summary>
        /// Draws the holder and the sessions waiting for the editor as cars on one track: the holder is the
        /// first car, a new waiter drives in from the right, and the line moves up when the editor is freed.
        /// Returns false when there is nothing to draw, so the caller shows its one-line lock text instead.
        /// </summary>
        public static bool ShowQueue(VisualElement view, EditorLockState? state)
        {
            var visible = state != null && (state.Held || state.Waiting.Length > 0 || state.QueuePaused);
            view.EnableInClassList("mb-queue--visible", visible);
            if (!ReferenceEquals(view, _queueView))
                Build(view);
            if (!visible || state == null)
            {
                ClearCars();
                return false;
            }

            ShowHeader(state);
            ShowCars(state);
            var pets = false;
            foreach (var car in _order)
                pets |= car.HasPet;
            _track?.EnableInClassList("mb-track--pets", pets);
            TickPets();
            Layout();
            return true;
        }

        static void Build(VisualElement view)
        {
            view.Clear();
            _cars.Clear();
            _order.Clear();
            _queueView = view;

            _header = new VisualElement();
            _header.AddToClassList("mb-queue__row");
            _title = new Label { name = "mb-queue-title" };
            _pause = new Label();
            _pause.AddToClassList("mb-queue__button");
            _pause.RegisterCallback<ClickEvent>(_ => SetPaused(_state.Value is not { QueuePaused: true }));
            _header.Add(_title);
            _header.Add(_pause);
            view.Add(_header);

            _track = new VisualElement();
            _track.AddToClassList("mb-track");
            _rail = new VisualElement { pickingMode = PickingMode.Ignore };
            _rail.AddToClassList("mb-track__rail");
            _track.Add(_rail);
            _lockLabel = TrackLabel("◆ LOCK", "mb-track__label--lock");
            _queueLabel = TrackLabel("QUEUE  ›", "mb-track__label--queue");
            _drops.Clear();
            _more = new VisualElement();
            _more.AddToClassList("mb-car");
            _more.AddToClassList("mb-car--more");
            _moreLabel = new Label();
            _moreLabel.AddToClassList("mb-car__name");
            _more.Add(_moreLabel);
            BridgeFont.Apply(_more);
            _track.Add(_more);
            _track.RegisterCallback<GeometryChangedEvent>(_ => Layout());
            _track.schedule.Execute(TickRain).Every(RainStepMs);
            view.Add(_track);
        }

        static void ShowHeader(EditorLockState state)
        {
            if (_header == null || _title == null || _pause == null)
                return;
            var queued = state.Waiting.Length > 0 || state.QueuePaused;
            _header.style.display = queued ? DisplayStyle.Flex : DisplayStyle.None;
            var note = state.QueuePaused ? "  -  PAUSED"
                : !string.IsNullOrEmpty(state.PlayLeftBy) ? $"  -  PLAY LEFT ON BY {state.PlayLeftBy!.ToUpperInvariant()}"
                : state.QueueHeldByPlayMode ? "  -  WAITING FOR PLAY MODE TO END" : string.Empty;
            _title.text = $"QUEUE  {state.Waiting.Length} waiting{note}";
            _pause.text = state.QueuePaused ? "RESUME" : "PAUSE";
            _pause.tooltip = state.QueuePaused ? "Let the first in line have the editor" : "Nobody is granted the editor until you resume";
        }

        static void ShowCars(EditorLockState state)
        {
            if (_track == null)
                return;

            // cars are keyed by session, so the waiter that gets the editor stays the same car and becomes the holder
            var seen = new HashSet<string>();
            _order.Clear();
            _shared.Clear();
            var owners = new HashSet<string>();
            if (state.Held)
                owners.Add(state.Owner);
            foreach (var waiter in state.Waiting)
                if (!owners.Add(waiter.Owner))
                    _shared.Add(waiter.Owner);
            if (state.Held)
                AddCar(HolderKey(state), null, state, seen);
            foreach (var waiter in state.Waiting)
                AddCar(WaiterKey(waiter), waiter, state, seen);

            var gone = new List<string>();
            foreach (var pair in _cars)
                if (!seen.Contains(pair.Key))
                    gone.Add(pair.Key);
            foreach (var key in gone)
            {
                Leave(_cars[key]);
                _cars.Remove(key);
            }
        }

        static void AddCar(string key, EditorLockWaiter? waiter, EditorLockState state, HashSet<string> seen)
        {
            if (!seen.Add(key))
                return;
            var isNew = !_cars.TryGetValue(key, out var car);
            if (isNew)
            {
                car = NewCar();
                _cars[key] = car;
                _track!.Add(car.Root);
            }

            var holder = waiter == null;
            if (holder && !isNew && !car!.Root.ClassListContains("mb-car--holder"))
                Pop(car);
            Fill(car!, waiter, state);
            _order.Add(car!);
        }

        static Car NewCar()
        {
            var car = new Car();
            car.Root.AddToClassList("mb-car");
            car.Name.AddToClassList("mb-car__name");
            car.Sub.AddToClassList("mb-car__sub");
            car.Remove.AddToClassList("mb-car__x");
            car.Glow.AddToClassList("mb-car__glow");
            car.Glow.style.backgroundImage = GlowTexture();
            car.Glow.style.unitySliceLeft = car.Glow.style.unitySliceRight = GlowSize;
            car.Glow.style.unitySliceTop = car.Glow.style.unitySliceBottom = GlowSize;
            car.Clip.AddToClassList("mb-car__clip");
            car.Root.Add(car.Glow);
            car.Root.Add(car.Clip);
            for (var i = 0; i < RainColumns; i++)
            {
                var column = new Label(RandomColumn()) { pickingMode = PickingMode.Ignore };
                column.AddToClassList("mb-car__rain");
                column.style.left = 4f + i * RainColumnPitch;
                car.Rain[i] = column;
                car.RainY[i] = -(float)_rng.NextDouble() * RainRows * RainRowHeight;
                car.RainSpeed[i] = 1.2f + (float)_rng.NextDouble() * 1.4f;
                car.Clip.Add(column);
            }
            car.Root.Add(car.Name);
            car.Root.Add(car.Sub);
            car.Root.Add(car.Remove);
            car.Pet.AddToClassList("mb-car__pet");
            car.PetSheet.AddToClassList("mb-car__pet-sheet");
            car.Pet.Add(car.PetSheet);
            car.Root.Add(car.Pet);
            BridgeFont.Apply(car.Root);
            car.Root.style.opacity = 0f;
            car.Root.RegisterCallback<ClickEvent>(_ =>
            {
                if (car.Waiter == null)
                    ShowMenu(car.Root.worldBound);
                else if (car.Waiter.After.Length > 0)
                    ShowWaiterMenu(car.Waiter, car.Root.worldBound);
            });
            car.Remove.RegisterCallback<ClickEvent>(evt =>
            {
                evt.StopPropagation();
                if (car.Waiter != null)
                    Remove(car.Waiter);
            });
            return car;
        }

        static void Fill(Car car, EditorLockWaiter? waiter, EditorLockState state)
        {
            car.Waiter = waiter;
            var holder = waiter == null;
            car.Root.EnableInClassList("mb-car--holder", holder);
            car.Root.EnableInClassList("mb-car--over", holder && IsPastCap(state));
            car.Root.EnableInClassList("mb-car--blocking", holder && IsBlocking(state));
            car.Root.EnableInClassList("mb-car--waiter", !holder);
            car.Clip.style.backgroundImage = holder ? Ramp(ref _tintAmber, Amber, 0.16f, 0.03f, 0.7f) : Ramp(ref _tintGreen, Green, 0.10f, 0.02f, 0.7f);
            ShowPet(car);

            if (waiter == null)
            {
                car.NameText = state.Kind == KindOutside ? $"{Name(state)} (lock file)" : Who(Name(state), state.Subagent, state.SessionId);
                car.SubText = Sub(state.Task, state.AcquiredUnixMs);
                ShowText(car);
                car.Root.tooltip = Tooltip(state);
                return;
            }

            var who = waiter.Owner.Length > 0 ? waiter.Owner : "a session";
            var client = waiter.ClientName.Length > 0 && waiter.ClientName != waiter.Owner ? $" ({waiter.ClientName})" : string.Empty;
            car.NameText = Who(who, waiter.Subagent, waiter.SessionId) + client;
            car.SubText = Sub(waiter.After.Length > 0 ? "after " + waiter.After : waiter.Task, waiter.WaitingSinceUnixMs);
            ShowText(car);
            car.Root.tooltip = $"{who}{client} waits for the editor" + Environment.NewLine
                + (waiter.Subagent ? $"A subagent{(waiter.AgentType.Length > 0 ? $" ({waiter.AgentType})" : string.Empty)}" + Environment.NewLine : string.Empty)
                + (waiter.Task.Length > 0 ? $"Task: {waiter.Task}" + Environment.NewLine : string.Empty)
                + (waiter.After.Length > 0 ? $"After: {waiter.After} must finish first. Click to let it go now." + Environment.NewLine : string.Empty)
                + $"Since: {Clock(waiter.WaitingSinceUnixMs)} ({Format(Age(waiter.WaitingSinceUnixMs))})";
            car.Remove.tooltip = $"Take {who} out of the queue";
        }

        static void Layout()
        {
            if (_track == null)
                return;
            var width = _track.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f)
                return;

            // cars that do not fit in one row go to the next; past the last row the rest is one "+N" car
            var pets = _track.ClassListContains("mb-track--pets");
            var rowPitch = pets ? PetRowPitch : RowPitch;
            var perRow = Mathf.Max(1, Mathf.FloorToInt((width + CarPitch - CarWidth) / CarPitch));
            var slots = Mathf.Min(_order.Count, perRow * RowsThatFit(pets ? PetTrackHeight : TrackHeight, rowPitch));
            var shown = slots < _order.Count ? slots - 1 : slots;
            var rows = Mathf.Max(1, Mathf.CeilToInt(slots / (float)perRow));
            var used = Mathf.Min(slots, perRow) * CarPitch - (CarPitch - CarWidth);
            var start = Mathf.Max(0f, (width - used) / 2f);
            for (var i = 0; i < _order.Count; i++)
            {
                _order[i].Root.style.display = i < shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (i < shown)
                    Place(_order[i], start + i % perRow * CarPitch, i / perRow * rowPitch, i);
            }
            _track.style.height = rows > 1 ? (pets ? PetTrackHeight : TrackHeight) + (rows - 1) * rowPitch : StyleKeyword.Null;
            ShowMore(shown, start + shown % perRow * CarPitch, shown / perRow * rowPitch);

            if (_rail != null)
            {
                _rail.style.left = start;
                _rail.style.width = Mathf.Max(0f, used);
                _rail.style.backgroundImage = Ramp(ref _railTexture, Green, 1f, 0.35f, 0.3f, 0.05f);
            }
            var held = _order.Count > 0 && _order[0].Waiter == null;
            var firstWaiter = held ? 1 : 0;
            PlaceLabel(_lockLabel, held, start);
            PlaceLabel(_queueLabel, _order.Count > firstWaiter && perRow > firstWaiter, start + firstWaiter * CarPitch);

            // a light drop falls in each gap between two cars of the first row
            var gaps = Mathf.Max(0, Mathf.Min(slots, perRow) - 1);
            while (_drops.Count < gaps)
            {
                var drop = new VisualElement { pickingMode = PickingMode.Ignore };
                drop.AddToClassList("mb-track__drop");
                drop.style.backgroundImage = DropTexture();
                _track.Add(drop);
                _drops.Add(drop);
            }
            for (var i = 0; i < _drops.Count; i++)
            {
                _drops[i].style.display = i < gaps ? DisplayStyle.Flex : DisplayStyle.None;
                _drops[i].style.left = start + (i + 1) * CarPitch - (CarPitch - CarWidth) / 2f;
            }
        }

        static Label TrackLabel(string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("mb-track__label");
            label.AddToClassList(className);
            _track!.Add(label);
            return label;
        }

        static void PlaceLabel(Label? label, bool shown, float x)
        {
            if (label == null)
                return;
            label.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
            label.style.left = x;
        }

        // how many rows the window has room for: the track sits in a column that never scrolls, so a row that does not fit would be cut off
        static int RowsThatFit(float oneRow, float rowPitch)
        {
            var column = _queueView?.parent;
            if (column == null || _track == null)
                return 1;
            var used = 0f;
            foreach (var child in column.Children())
                if (child.resolvedStyle.display != DisplayStyle.None)
                    used += child.layout.height + child.resolvedStyle.marginTop + child.resolvedStyle.marginBottom;
            var room = column.layout.height - used + _track.layout.height - oneRow;
            return float.IsNaN(room) ? 1 : Mathf.Clamp(1 + Mathf.FloorToInt(room / rowPitch), 1, MaxRows);
        }

        // the cars past the last row, as one car that lists them
        static void ShowMore(int shown, float x, float y)
        {
            if (_more == null || _moreLabel == null)
                return;
            var hidden = _order.Count - shown;
            _more.style.display = hidden > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (hidden <= 0)
                return;
            _more.style.translate = new Translate(x, y);
            _moreLabel.text = $"+{hidden}";
            var list = new StringBuilder($"{hidden} more waiting:");
            for (var i = shown; i < _order.Count; i++)
                list.Append(Environment.NewLine).Append(_order[i].NameText).Append("  -  ").Append(_order[i].SubText);
            _more.tooltip = list.ToString();
        }

        static void Place(Car car, float x, float y, int index)
        {
            car.Root.style.transitionDelay = new List<TimeValue> { new(index * StaggerMs, TimeUnit.Millisecond) };
            car.Y = y;
            if (float.IsNaN(car.X))
            {
                // a change made before the car's first style pass jumps instead of animating, so the drive-in waits for its first layout
                car.Root.style.translate = new Translate(x + EnterFrom, y);
                car.Entering = true;
                void Enter() => car.Root.schedule.Execute(() =>
                {
                    car.Entering = false;
                    car.Root.style.opacity = 1f;
                    car.Root.style.translate = new Translate(car.X, car.Y);
                    Decode(car);
                });
                // a car placed from the track's own first layout is already laid out and gets no further geometry event
                if (car.Root.layout.width > 0f)
                    Enter();
                else
                {
                    EventCallback<GeometryChangedEvent>? laidOut = null;
                    laidOut = _ =>
                    {
                        car.Root.UnregisterCallback(laidOut);
                        Enter();
                    };
                    car.Root.RegisterCallback(laidOut);
                }
            }
            car.X = x;
            if (!car.Entering)
                car.Root.style.translate = new Translate(x, y);
        }

        static void Leave(Car car)
        {
            car.Root.AddToClassList("mb-car--leaving");
            car.Root.pickingMode = PickingMode.Ignore;
            car.Root.style.transitionDelay = new List<TimeValue> { new(0, TimeUnit.Millisecond) };
            car.Root.style.opacity = 0f;
            car.Root.style.translate = new Translate(car.X + LeaveTo, car.Y);
            car.Root.schedule.Execute(car.Root.RemoveFromHierarchy).StartingIn(LeaveMs);
        }

        static void Pop(Car car)
        {
            car.CheerUntil = EditorApplication.timeSinceStartup + CheerSeconds;
            car.Root.AddToClassList("mb-car--pop");
            car.Root.schedule.Execute(() => car.Root.RemoveFromClassList("mb-car--pop")).StartingIn(PopMs);
        }

        static void ShowText(Car car)
        {
            if (car.Decode > 0)
                return;
            car.Name.text = car.NameText;
            car.Sub.text = car.SubText;
        }

        // the name of a car that drives in resolves from random glyphs, like the rain behind it
        static void Decode(Car car)
        {
            car.Decode = DecodeSteps;
            car.Name.text = Scramble(car.NameText);
            car.Sub.text = Scramble(car.SubText);
            car.Root.schedule.Execute(() =>
            {
                car.Decode--;
                if (car.Decode > 0)
                {
                    car.Name.text = Scramble(car.NameText);
                    car.Sub.text = Scramble(car.SubText);
                }
                else
                    ShowText(car);
            }).Every(DecodeStepMs).Until(() => car.Decode <= 0);
        }

        static void TickRain()
        {
            const float span = RainRows * RainRowHeight;
            var now = (float)(EditorApplication.timeSinceStartup * 1000.0);
            for (var i = 0; i < _drops.Count; i++)
            {
                var duration = DropMs + (i + 1) * DropStepMs;
                var t = Mathf.Repeat(now - (i + 1) * DropDelayMs, duration) / duration;
                _drops[i].style.translate = new Translate(0, DropTravel * t);
                _drops[i].style.opacity = t < 0.3f ? t / 0.3f : (1f - t) / 0.7f;
            }
            TickPets();
            foreach (var car in _cars.Values)
                for (var i = 0; i < RainColumns; i++)
                {
                    var y = car.RainY[i] + car.RainSpeed[i];
                    if (y > car.Root.layout.height)
                    {
                        y = -span;
                        car.Rain[i].text = RandomColumn();
                    }
                    car.RainY[i] = y;
                    car.Rain[i].style.translate = new Translate(0, y);
                }
        }

        // every session rides on the roof of its car, posed by what it does or how long it has waited
        static void ShowPet(Car car)
        {
            car.HasPet = PetAtlas() != null;
            car.Pet.style.display = car.HasPet ? DisplayStyle.Flex : DisplayStyle.None;
            if (!car.HasPet)
                return;
            car.PetSheet.style.backgroundImage = _petAtlas;
            car.PetSheet.style.width = PetColumns * PetSize;
            car.PetSheet.style.height = PetFrames.Length * PetSize;
        }

        static void TickPets()
        {
            if (_state.Value is not { } state)
                return;
            var now = EditorApplication.timeSinceStartup;
            foreach (var car in _cars.Values)
            {
                if (!car.HasPet)
                    continue;
                var group = PetGroupOf(car, state, now);
                if (group != car.PetGroup || now >= car.PetSwapAt)
                    PickPetRow(car, group, now);
                var (frames, ms) = PetFrames[car.PetRow];
                var frame = (int)((now + car.PetPhase) * 1000.0 / ms) % frames;
                car.PetSheet.style.translate = new Translate(-frame * PetSize, -car.PetRow * PetSize);
            }
        }

        static void PickPetRow(Car car, int group, double now)
        {
            var rows = PetGroups[group];
            var pick = _rng.Next(rows.Length);
            if (group == car.PetGroup && rows.Length > 1 && rows[pick] == car.PetRow)
                pick = (pick + 1) % rows.Length;
            car.PetGroup = group;
            car.PetRow = rows[pick];
            car.PetSwapAt = now + PetSwapMinSeconds + _rng.NextDouble() * (PetSwapMaxSeconds - PetSwapMinSeconds);
        }

        static int PetGroupOf(Car car, EditorLockState state, double now)
        {
            if (car.Waiter is { } waiter)
            {
                if (state.QueuePaused)
                    return PetCoffee;
                var waited = Age(waiter.WaitingSinceUnixMs).TotalMinutes;
                return waited < 1 ? PetIdle : waited < 3 ? PetYawning : PetSleeping;
            }
            if (now < car.CheerUntil)
                return PetCelebrating;
            if (IsBlocking(state))
                return PetAngry;
            if (IsPastCap(state))
                return PetOverheated;
            var task = state.Task.ToLowerInvariant();
            return task.Contains("play") ? PetGaming
                : task.Contains("test") ? PetTesting
                : task.Contains("compil") || task.Contains("build") ? PetBuilding
                : task.Contains("refresh") || task.Contains("import") ? PetLoading
                : task.Contains("save") || task.Contains("scene") || task.Contains("prefab") ? PetCarrying
                : PetTyping;
        }

        static Texture2D? PetAtlas()
        {
            if (_petAtlas != null || _petAtlasMissing)
                return _petAtlas;
            _petAtlas = EditorAssetLoader.LoadAssetAtPath<Texture2D>(EditorAssetLoader.GetEditorAssetPaths(PetAtlasPath));
            if (_petAtlas == null)
            {
                _petAtlasMissing = true;
                Debug.LogWarning($"[Matrix Bridge] The queue pets are off: {PetAtlasPath} is missing from the package.");
            }
            return _petAtlas;
        }

        // horizontal alpha ramp through (0, a0), (mid, a1), (1, a2); runtime textures are lost on a domain reload, so they are rebuilt when gone
        static Texture2D Ramp(ref Texture2D? cache, Color color, float a0, float a1, float mid, float a2 = -1f)
        {
            if (cache != null)
                return cache;
            if (a2 < 0f)
                a2 = a1;
            const int width = 64;
            var pixels = new Color[width];
            for (var x = 0; x < width; x++)
            {
                var t = x / (width - 1f);
                var a = t < mid ? Mathf.Lerp(a0, a1, t / mid) : Mathf.Lerp(a1, a2, (t - mid) / (1f - mid));
                pixels[x] = new Color(color.r, color.g, color.b, a);
            }
            return cache = NewTexture(width, 1, pixels);
        }

        static Texture2D DropTexture()
        {
            if (_dropTexture != null)
                return _dropTexture;
            const int height = 16;
            var pixels = new Color[height];
            // row 0 is the bottom of the texture: bright at the bottom, clear at the top
            for (var y = 0; y < height; y++)
                pixels[y] = new Color(Green.r, Green.g, Green.b, 1f - y / (height - 1f));
            return _dropTexture = NewTexture(1, height, pixels);
        }

        // a soft amber halo for 9-slicing around the holder: clear in the middle, fading out over GlowSize pixels
        static Texture2D GlowTexture()
        {
            if (_glowTexture != null)
                return _glowTexture;
            const int size = GlowSize * 2 + 1;
            var pixels = new Color[size * size];
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var d = Mathf.Max(Mathf.Abs(x - GlowSize), Mathf.Abs(y - GlowSize));
                    var a = d == 0 ? 0f : 0.11f * (1f - (d - 1f) / GlowSize);
                    pixels[y * size + x] = new Color(Amber.r, Amber.g, Amber.b, a * a / 0.11f);
                }
            return _glowTexture = NewTexture(size, size, pixels);
        }

        static Texture2D NewTexture(int width, int height, Color[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            texture.SetPixels(pixels);
            texture.Apply();
            return texture;
        }

        static string RandomColumn()
        {
            var text = new StringBuilder(RainRows * 2);
            for (var k = 0; k < RainRows; k++)
            {
                if (k > 0)
                    text.Append('\n');
                text.Append(RandomGlyph());
            }
            return text.ToString();
        }

        static string Scramble(string text)
        {
            var chars = text.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
                if (!char.IsWhiteSpace(chars[i]))
                    chars[i] = RandomGlyph();
            return new string(chars);
        }

        static char RandomGlyph() => MatrixRainRenderer.Glyphs[_rng.Next(MatrixRainRenderer.Glyphs.Length)];

        static void ClearCars()
        {
            foreach (var car in _cars.Values)
                car.Root.RemoveFromHierarchy();
            _cars.Clear();
            _order.Clear();
        }

        static string HolderKey(EditorLockState state)
            => state.SessionId.Length > 0 ? state.SessionId : $"holder:{state.Kind}:{state.Owner}";

        static string WaiterKey(EditorLockWaiter waiter)
            => waiter.SessionId.Length > 0 ? waiter.SessionId : $"ticket:{waiter.Ticket}";

        static string Sub(string task, long sinceUnixMs)
            => task.Length > 0 ? $"{task} · {Format(Age(sinceUnixMs))}" : Format(Age(sinceUnixMs));

        static void CopyDetails()
        {
            if (_state.Value is { } state)
                GUIUtility.systemCopyBuffer = Tooltip(state).Replace("Click for options.", string.Empty).Trim();
        }

        static string Name(EditorLockState state) => state.Owner.Length > 0 ? state.Owner : "a session";

        // a subagent is marked, and cars that go by one owner also show the end of their own id
        static string Who(string owner, bool subagent, string id)
            => (subagent ? "› " : string.Empty) + owner + (_shared.Contains(owner) && id.Length > 0 ? " " + id.Substring(Math.Max(0, id.Length - 4)) : string.Empty);

        static string Task(EditorLockState state) => state.Task.Length > 0 ? state.Task + "  -  " : string.Empty;

        static TimeSpan Age(long unixMs)
        {
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeMilliseconds(unixMs);
            return age < TimeSpan.Zero ? TimeSpan.Zero : age;
        }

        static string Format(TimeSpan span)
            => span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m"
                : span.TotalMinutes >= 1 ? $"{(int)span.TotalMinutes}m {span.Seconds}s"
                : $"{span.Seconds}s";

        static string Clock(long unixMs) => DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime().ToString("HH:mm");

        static string Short(string sessionId) => sessionId.Length > 8 ? sessionId.Substring(0, 8) : sessionId;
    }
}

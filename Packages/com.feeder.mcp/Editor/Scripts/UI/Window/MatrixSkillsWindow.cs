#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost;
using Feeder.MCP.Editor.UI.Controls;
using Feeder.MCP.Editor.Utils;
using Feeder.Logging;
using Feeder.Rx;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.UI
{
    public class MatrixSkillsWindow : McpWindowBase
    {
        private static readonly string[] _windowUxmlPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uxml/MatrixSkills.uxml");
        private static readonly string[] _rowUxmlPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uxml/SkillRow.uxml");
        private static readonly string[] _groupUxmlPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uxml/SkillGroup.uxml");
        private static readonly string[] _windowUssPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uss/MatrixSkills.uss");

        private const int FilterAll = 0;
        private const int FilterOn = 1;
        private const int SyncDelayMs = 1000;
        private const int RevealRows = 8;
        private const int RevealStepMs = 26;

        protected override string WindowTitle => "Matrix Skills";
        protected override string[] WindowUxmlPaths => _windowUxmlPaths;
        protected override string[] WindowUssPaths => _windowUssPaths;

        private sealed class RowView
        {
            public RowView(SkillInfo info, VisualElement root, Toggle toggle, VisualElement hint)
            {
                Info = info;
                Root = root;
                Toggle = toggle;
                Hint = hint;
            }

            public SkillInfo Info { get; }
            public VisualElement Root { get; }
            public Toggle Toggle { get; }
            public VisualElement Hint { get; }
            public string HintKey { get; set; } = string.Empty;
        }

        private sealed class GroupView
        {
            public GroupView(SkillGroup? group, Button button, Label count)
            {
                Group = group;
                Button = button;
                Count = count;
            }

            public SkillGroup? Group { get; }
            public Button Button { get; }
            public Label Count { get; }
        }

        private readonly CompositeDisposable _subscriptions = new();
        private IDisposable? _toolsSubscription;
        private readonly List<RowView> _rows = new();
        private readonly List<GroupView> _groupViews = new();
        private readonly Dictionary<SkillGroup, Label> _headers = new();
        private readonly Dictionary<string, SkillInfo> _byName = new();

        private IReadOnlyList<SkillInfo> _infos = Array.Empty<SkillInfo>();
        private IToolManager? _manager;
        private SkillGroup? _selected;
        private string _query = string.Empty;
        private int _filter = FilterAll;
        private int _shown;
        private int _totalTokens;
        private int _builtTotal;
        private bool _built;
        private bool _dormant;
        private bool _syncPending;

        private VisualElement _off = null!;
        private ScrollView _groups = null!;
        private ScrollView _list = null!;
        private ToolbarSearchField _search = null!;
        private Label _empty = null!;
        private Label _title = null!;
        private Label _subtitle = null!;
        private Label _sideCount = null!;
        private Label _sync = null!;
        private VisualElement _syncDot = null!;
        private VisualElement _meter = null!;
        private VisualElement _meterFill = null!;
        private VisualElement _tickLean = null!;
        private VisualElement _tickHeavy = null!;
        private Label _cost = null!;
        private Label _tier = null!;
        private IVisualElementScheduledItem? _syncItem;

        public static MatrixSkillsWindow ShowWindow()
        {
            MatrixActivation.RequestWindow();
            var isNew = !HasOpenInstances<MatrixSkillsWindow>();
            var window = GetWindow<MatrixSkillsWindow>("Matrix Skills");
            window.SetupWindowWithIcon();
            window.minSize = new Vector2(600, 340);
            if (isNew)
                window.position = new Rect(window.position.x, window.position.y, 780, 520);
            window.Focus();
            return window;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _dormant = MatrixActivation.CloseIfDormant(this);
            if (_dormant)
                return;
            SetupWindowWithIcon();
        }

        private void OnDisable()
        {
            FlushSync();
            Unbind();
        }

        private void OnFocus() => Refresh();

        public override void CreateGUI()
        {
            if (_dormant)
                return;
            Unbind();
            base.CreateGUI();
        }

        protected override void OnGUICreated(VisualElement root)
        {
            var off = root.Q<VisualElement>("sk-off");
            var groups = root.Q<ScrollView>("sk-groups");
            var list = root.Q<ScrollView>("sk-list");
            var search = root.Q<ToolbarSearchField>("sk-search");
            var filterSlot = root.Q<VisualElement>("sk-filter-slot");
            var title = root.Q<Label>("sk-title");
            var subtitle = root.Q<Label>("sk-subtitle");
            var sideCount = root.Q<Label>("sk-side-count");
            var sync = root.Q<Label>("sk-sync");
            var syncDot = root.Q<VisualElement>("sk-sync-dot");
            var meter = root.Q<VisualElement>("sk-meter");
            var meterFill = root.Q<VisualElement>("sk-meter-fill");
            var tickLean = root.Q<VisualElement>("sk-tick-lean");
            var tickHeavy = root.Q<VisualElement>("sk-tick-heavy");
            var cost = root.Q<Label>("sk-cost");
            var tier = root.Q<Label>("sk-tier");
            var core = root.Q<Button>("sk-core");
            var all = root.Q<Button>("sk-all");
            var setup = root.Q<Button>("sk-setup");

            if (off == null || groups == null || list == null || search == null || filterSlot == null || title == null || subtitle == null ||
                sideCount == null || sync == null || syncDot == null || meter == null || meterFill == null || tickLean == null ||
                tickHeavy == null || cost == null || tier == null || core == null || all == null || setup == null)
            {
                Logger.LogError("{method} MatrixSkills.uxml is missing one of its named elements.", nameof(OnGUICreated));
                return;
            }

            _off = off;
            _groups = groups;
            _list = list;
            _search = search;
            _title = title;
            _subtitle = subtitle;
            _sideCount = sideCount;
            _sync = sync;
            _syncDot = syncDot;
            _meter = meter;
            _meterFill = meterFill;
            _tickLean = tickLean;
            _tickHeavy = tickHeavy;
            _cost = cost;
            _tier = tier;
            _empty = new Label("> no skill matches");
            _empty.AddToClassList("sk-empty");

            _selected = null;
            _query = string.Empty;
            _filter = FilterAll;
            _manager = null;
            _syncPending = false;
            _built = true;
            SetSync(false, "Skill files up to date");

            var filter = new SegmentedControl("All", "On", "Off");
            filter.SetValueWithoutNotify(FilterAll);
            filter.RegisterCallback<ChangeEvent<int>>(OnFilterChanged);
            filterSlot.Add(filter);

            _search.UnregisterValueChangedCallback(OnQueryChanged);
            _search.RegisterValueChangedCallback(OnQueryChanged);
            core.clicked -= OnCoreClicked;
            core.clicked += OnCoreClicked;
            all.clicked -= OnAllClicked;
            all.clicked += OnAllClicked;
            setup.clicked -= OnSetupClicked;
            setup.clicked += OnSetupClicked;

            Bind();
            Rebuild();
        }

        private void Bind()
        {
            MatrixPluginEditor.PluginProperty
                .WhereNotNull()
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnPlugin)
                .AddTo(_subscriptions);
        }

        private void Unbind()
        {
            _subscriptions.Clear();
            _toolsSubscription?.Dispose();
            _toolsSubscription = null;
            _syncItem?.Pause();
            _syncItem = null;
            _built = false;
        }

        private void OnPlugin(IToolHost plugin)
        {
            _toolsSubscription?.Dispose();
            _toolsSubscription = null;
            var manager = plugin.McpManager.ToolManager;
            if (manager != null)
            {
                _toolsSubscription = manager.OnToolsUpdated
                    .ObserveOnCurrentSynchronizationContext()
                    .Subscribe(OnToolsUpdated);
            }

            if (!ReferenceEquals(manager, _manager))
                Rebuild();
        }

        private void OnToolsUpdated(Unit unit) => Refresh();

        private IReadOnlyCollection<string> CoreNames => MatrixSetup.CoreTools;

        private static ToolFacts Facts(IRunTool tool) => new ToolFacts(
            tool.Name,
            tool.Title,
            string.IsNullOrWhiteSpace(tool.SkillDescription) ? tool.Description : tool.SkillDescription,
            tool.ReadOnlyHint,
            tool.DestructiveHint,
            tool.TokenCount);

        private void Rebuild()
        {
            if (!_built)
                return;

            _manager = MatrixPluginEditor.Instance.Tools;
            _selected = null;
            _builtTotal = _manager?.TotalToolsCount ?? 0;
            _groups.Clear();
            _list.Clear();
            _rows.Clear();
            _groupViews.Clear();
            _headers.Clear();
            _byName.Clear();
            _infos = Array.Empty<SkillInfo>();
            _totalTokens = 0;
            _off.EnableInClassList("sk-off--show", _manager == null);

            if (_manager != null)
            {
                _infos = SkillCatalog.Build(_manager.GetAllTools().Select(Facts), CoreNames);
                _totalTokens = _infos.Sum(info => info.Tokens);
                foreach (var info in _infos)
                    _byName[info.Name] = info;
                BuildGroups();
                BuildRows();
                MarkGroups();
                BridgeFont.Apply(rootVisualElement, "sk-mono");
            }

            _list.Add(_empty);
            Refresh();
            ApplyFilter(true);
        }

        private void BuildGroups()
        {
            var template = EditorAssetLoader.LoadAssetAtPath<VisualTreeAsset>(_groupUxmlPaths, Logger);
            if (template == null)
            {
                Logger.LogError("{method} SkillGroup.uxml could not be loaded.", nameof(BuildGroups));
                return;
            }

            AddGroup(template, null, "*", "All skills");
            foreach (var group in SkillCatalog.GroupsOf(_infos))
                AddGroup(template, group, group.Mono, group.Name);
        }

        private void AddGroup(VisualTreeAsset template, SkillGroup? group, string mono, string name)
        {
            var button = template.Instantiate().Q<Button>(className: "sk-group");
            var monoLabel = button?.Q<Label>("sk-group-mono");
            var nameLabel = button?.Q<Label>("sk-group-name");
            var count = button?.Q<Label>("sk-group-count");
            if (button == null || monoLabel == null || nameLabel == null || count == null)
            {
                Logger.LogError("{method} SkillGroup.uxml is missing .sk-group or one of its labels.", nameof(AddGroup));
                return;
            }

            monoLabel.text = mono;
            nameLabel.text = name;
            var view = new GroupView(group, button, count);
            button.userData = view;
            button.RegisterCallback<ClickEvent>(OnGroupClicked);
            _groups.Add(button);
            _groupViews.Add(view);
        }

        private void BuildRows()
        {
            var template = EditorAssetLoader.LoadAssetAtPath<VisualTreeAsset>(_rowUxmlPaths, Logger);
            if (template == null)
            {
                Logger.LogError("{method} SkillRow.uxml could not be loaded.", nameof(BuildRows));
                return;
            }

            SkillGroup? current = null;
            foreach (var info in _infos)
            {
                if (info.Group != current)
                {
                    current = info.Group;
                    var header = new Label(current.Name.ToUpperInvariant());
                    header.AddToClassList("sk-list__header");
                    _headers[current] = header;
                    _list.Add(header);
                }

                var row = CreateRow(template, info);
                if (row != null)
                {
                    _rows.Add(row);
                    _list.Add(row.Root);
                }
            }
        }

        private RowView? CreateRow(VisualTreeAsset template, SkillInfo info)
        {
            var root = template.Instantiate().Q<VisualElement>(className: "sk-row");
            var title = root?.Q<Label>("sk-row-title");
            var id = root?.Q<Label>("sk-row-id");
            var core = root?.Q<Label>("sk-row-core");
            var risk = root?.Q<Label>("sk-row-risk");
            var desc = root?.Q<Label>("sk-row-desc");
            var hint = root?.Q<VisualElement>("sk-row-hint");
            var cost = root?.Q<Label>("sk-row-cost");
            var toggle = root?.Q<Toggle>("sk-row-toggle");
            if (root == null || title == null || id == null || core == null || risk == null || desc == null || hint == null || cost == null || toggle == null)
            {
                Logger.LogError("{method} SkillRow.uxml is missing .sk-row or one of its elements.", nameof(CreateRow));
                return null;
            }

            title.text = info.Label;
            id.text = info.Name;
            core.style.display = info.IsCore ? DisplayStyle.Flex : DisplayStyle.None;
            risk.style.display = info.Risk == SkillRisk.ReadOnly ? DisplayStyle.None : DisplayStyle.Flex;
            risk.text = info.Risk == SkillRisk.Destructive ? "destructive" : "writes";
            risk.AddToClassList(info.Risk == SkillRisk.Destructive ? "sk-tag--destructive" : "sk-tag--writes");
            desc.text = info.Summary;
            cost.text = "~" + SkillCatalog.FormatTokens(info.Tokens);
            cost.tooltip = $"About {info.Tokens} tokens per request while this skill is on.";
            var tooltip = string.IsNullOrEmpty(info.Description) ? info.Name : $"{info.Title}\n\n{info.Description}";
            title.tooltip = tooltip;
            desc.tooltip = tooltip;

            var view = new RowView(info, root, toggle, hint);
            toggle.userData = view;
            toggle.RegisterValueChangedCallback(OnToggled);
            return view;
        }

        private void OnGroupClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement element && element.userData is GroupView view)
                Select(view.Group);
        }

        private void Select(SkillGroup? group)
        {
            _selected = group;
            _query = string.Empty;
            _search.SetValueWithoutNotify(string.Empty);
            MarkGroups();
            ApplyFilter(true);
        }

        private void MarkGroups()
        {
            foreach (var view in _groupViews)
                view.Button.EnableInClassList("sk-group--on", view.Group == _selected);
        }

        private void OnQueryChanged(ChangeEvent<string> evt)
        {
            _query = evt.newValue ?? string.Empty;
            if (_query.Trim().Length > 0 && _selected != null)
            {
                _selected = null;
                MarkGroups();
            }
            ApplyFilter(true);
        }

        private void OnFilterChanged(ChangeEvent<int> evt)
        {
            _filter = evt.newValue;
            ApplyFilter(true);
        }

        private void ApplyFilter(bool reveal)
        {
            var manager = _manager;
            var words = _query.ToLowerInvariant().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var visible = new List<VisualElement>();
            var groupsShown = new HashSet<SkillGroup>();
            foreach (var row in _rows)
            {
                var info = row.Info;
                var show = manager != null &&
                           (_selected == null || info.Group == _selected) &&
                           (_filter == FilterAll || (_filter == FilterOn) == manager.IsToolEnabled(info.Name)) &&
                           words.All(word => info.SearchText.Contains(word));
                row.Root.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
                if (!show)
                    continue;
                visible.Add(row.Root);
                groupsShown.Add(info.Group);
            }

            foreach (var pair in _headers)
                pair.Value.style.display = _selected == null && groupsShown.Contains(pair.Key) ? DisplayStyle.Flex : DisplayStyle.None;

            _shown = visible.Count;
            _empty.EnableInClassList("sk-empty--show", manager != null && _shown == 0);
            UpdateHeading();
            _list.scrollOffset = Vector2.zero;
            if (reveal)
                Reveal(visible);
        }

        private static void Reveal(List<VisualElement> rows)
        {
            for (var i = 0; i < Math.Min(rows.Count, RevealRows); i++)
            {
                var row = rows[i];
                row.AddToClassList("sk-row--pre");
                row.schedule.Execute(() => row.RemoveFromClassList("sk-row--pre")).StartingIn(RevealStepMs * (i + 1));
            }
        }

        private void UpdateHeading()
        {
            var manager = _manager;
            if (manager == null)
            {
                _title.text = "Skills";
                _subtitle.text = string.Empty;
                return;
            }

            var query = _query.Trim();
            if (query.Length > 0)
            {
                _title.text = "Results";
                _subtitle.text = $"{_shown} of {_infos.Count} skills match \"{query}\"";
            }
            else if (_selected == null)
            {
                _title.text = "All skills";
                _subtitle.text = $"{_infos.Count} skills - {manager.EnabledToolsCount} on";
            }
            else
            {
                _title.text = _selected.Name;
                _subtitle.text = _selected.Blurb;
            }
        }

        private void Refresh()
        {
            var manager = _manager;
            if (!_built || manager == null)
                return;

            if (manager.TotalToolsCount != _builtTotal)
            {
                Rebuild();
                return;
            }

            foreach (var row in _rows)
            {
                var on = manager.IsToolEnabled(row.Info.Name);
                row.Toggle.SetValueWithoutNotify(on);
                RefreshHint(row, on, manager);
            }

            foreach (var view in _groupViews)
            {
                var rows = _rows.Where(row => view.Group == null || row.Info.Group == view.Group).ToList();
                var enabled = rows.Count(row => row.Toggle.value);
                view.Count.text = $"{enabled}/{rows.Count}";
            }

            _sideCount.text = $"{manager.EnabledToolsCount} of {manager.TotalToolsCount} on";
            RefreshMeter(manager);
            if (_query.Trim().Length == 0 && _selected == null)
                UpdateHeading();
        }

        private void RefreshHint(RowView row, bool on, IToolManager manager)
        {
            var missing = on
                ? row.Info.Companions.Where(name => !manager.IsToolEnabled(name)).ToList()
                : new List<string>();
            var key = string.Join("|", missing);
            if (key == row.HintKey)
                return;

            row.HintKey = key;
            row.Hint.Clear();
            row.Hint.EnableInClassList("sk-row__hint--show", missing.Count > 0);
            if (missing.Count == 0)
                return;

            row.Hint.Add(MakeLabel("Works with", "sk-hint__text"));
            foreach (var name in missing)
            {
                var link = new Button { text = _byName.TryGetValue(name, out var companion) ? companion.Title : name };
                link.AddToClassList("sk-hint__link");
                link.userData = name;
                link.RegisterCallback<ClickEvent>(OnHintClicked);
                row.Hint.Add(link);
            }
            row.Hint.Add(MakeLabel("(off)", "sk-hint__off"));
        }

        private static Label MakeLabel(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        private void RefreshMeter(IToolManager manager)
        {
            var tokens = manager.EnabledToolsTokenCount;
            var ratio = _totalTokens > 0 ? Mathf.Clamp01(tokens / (float)_totalTokens) : 0f;
            _meterFill.style.scale = new Scale(new Vector3(ratio, 1f, 1f));
            PlaceTick(_tickLean, SkillCatalog.LeanBelow);
            PlaceTick(_tickHeavy, SkillCatalog.HeavyFrom);

            var cost = SkillCatalog.CostOf(tokens);
            _meter.EnableInClassList("sk-meter--moderate", cost == SkillCost.Moderate);
            _meter.EnableInClassList("sk-meter--heavy", cost == SkillCost.Heavy);
            _tier.EnableInClassList("sk-foot__tier--moderate", cost == SkillCost.Moderate);
            _tier.EnableInClassList("sk-foot__tier--heavy", cost == SkillCost.Heavy);
            _tier.text = cost.ToString().ToUpperInvariant();
            _cost.text = $"{manager.EnabledToolsCount} on - ~{SkillCatalog.FormatTokens(tokens)} tokens / request";
        }

        private void PlaceTick(VisualElement tick, int threshold)
        {
            var percent = _totalTokens > 0 ? threshold * 100f / _totalTokens : 100f;
            tick.style.display = percent < 100f ? DisplayStyle.Flex : DisplayStyle.None;
            tick.style.left = new Length(percent, LengthUnit.Percent);
        }

        private void OnToggled(ChangeEvent<bool> evt)
        {
            if (evt.target is not Toggle toggle || toggle.userData is not RowView row)
                return;

            var manager = _manager;
            if (manager == null)
            {
                toggle.SetValueWithoutNotify(!evt.newValue);
                return;
            }

            if (!manager.SetToolEnabled(row.Info.Name, evt.newValue))
                Logger.LogError("{method} could not switch '{tool}': it is not registered.", nameof(OnToggled), row.Info.Name);
            Refresh();
            ScheduleSync();
        }

        private void OnHintClicked(ClickEvent evt)
        {
            if (evt.currentTarget is not VisualElement element || element.userData is not string name)
                return;

            var manager = _manager;
            if (manager == null)
                return;

            if (!manager.SetToolEnabled(name, true))
                Logger.LogError("{method} could not switch '{tool}': it is not registered.", nameof(OnHintClicked), name);
            Refresh();
            ScheduleSync();
        }

        private void ScheduleSync()
        {
            _syncPending = true;
            SetSync(true, "Updating skill files...");
            _syncItem ??= rootVisualElement.schedule.Execute(SyncNow);
            _syncItem.ExecuteLater(SyncDelayMs);
        }

        private void FlushSync()
        {
            if (_syncPending)
                SyncNow();
        }

        private void SyncNow()
        {
            _syncPending = false;
            try
            {
                MatrixPluginEditor.Instance.Save();
                var clients = BridgeAgents.RegenerateSkills();
                SetSync(false, clients == 0 ? "No wired agent writes skill files" : "Skill files up to date");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{method} could not write the skill files.", nameof(SyncNow));
                SetSync(false, "Skill files failed, see the Console");
            }
        }

        private void SetSync(bool busy, string text)
        {
            if (!_built)
                return;
            _sync.text = text;
            _syncDot.EnableInClassList("sk-dot--busy", busy);
        }

        private void OnCoreClicked()
        {
            MatrixTools.CoreOnly();
            Refresh();
        }

        private void OnAllClicked()
        {
            MatrixTools.EnableAll();
            Refresh();
        }

        private static void OnSetupClicked() => MatrixSetup.Run();
    }
}

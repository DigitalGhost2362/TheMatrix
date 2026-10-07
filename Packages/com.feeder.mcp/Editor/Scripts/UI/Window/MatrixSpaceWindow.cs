#nullable enable

using System;
using System.Collections.Generic;
using Feeder.MCP.Editor.MatrixSpace;
using Feeder.MCP.Editor.MatrixSpace.Setup;
using Feeder.MCP.Editor.UI.MatrixSpace;
using Feeder.MCP.Editor.UI.Controls;
using Feeder.MCP.Editor.Utils;
using Feeder.Logging;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Feeder.MCP.Editor.UI
{
    /// <summary>
    /// Matrix Space — a MatrixSpace-style multi-agent workspace: a grid of 1–8 panes (each
    /// its own CLI agent session), a task board (MatrixBoard-lite), shared memory
    /// (MatrixMemory-lite), and swarm missions (MatrixSwarm-lite), all Matrix-themed.
    /// </summary>
    public class MatrixSpaceWindow : McpWindowBase
    {
        private static readonly string[] _windowUxmlPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uxml/MatrixSpaceWindow.uxml");
        private static readonly string[] _paneUxmlPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uxml/AgentPane.uxml");
        private static readonly string[] _windowUssPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uss/MatrixSpaceWindow.uss");

        private const long MatrixRainRepaintIntervalMs = 50;
        private static readonly Color MatrixWindowBackground = new(0.004f, 0.016f, 0.006f, 1f);

        protected override string[] WindowUxmlPaths => _windowUxmlPaths;
        protected override string[] WindowUssPaths => _windowUssPaths;
        protected override string WindowTitle => "Matrix Space";

        private readonly Dictionary<string, AgentSession> _sessions = new();
        private PaneGridController? _grid;
        private VisualTreeAsset? _paneTemplate;
        private SegmentedControl? _presetControl;

        private KanbanBoardView? _board;
        private MemoryWorkspaceView? _memoryPanel;
        private SwarmBoardView? _swarmBoard;
        private UsageView? _usageView;
        private MatrixSpaceViewRouter? _router;
        private SidebarView? _sidebar;
        private Label? _breadcrumb;
        private Label? _reloadStatus;

        private ToastLayer? _toasts;
        private MatrixRainRenderer? _matrixRain;
        private IMGUIContainer? _matrixRainContainer;
        private double _lastMatrixRainStep;
        private bool _dormant;

        public static MatrixSpaceWindow ShowWindow()
        {
            MatrixActivation.RequestWindow();
            var window = GetWindow<MatrixSpaceWindow>("Matrix Space");
            window.SetupWindowWithIcon();
            window.minSize = new Vector2(900, 500);
            window.Show();
            window.Focus();
            return window;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _dormant = MatrixActivation.CloseIfDormant(this);
            if (_dormant)
                return;

            // Re-applies the tab title + icon after domain reloads (ShowWindow only runs once).
            SetupWindowWithIcon();

            ModelCatalogService.EnsureLoaded();
            ModelCatalogService.Updated -= RefreshAllModelDropdowns;
            ModelCatalogService.Updated += RefreshAllModelDropdowns;
            MatrixSpaceReloadCoordinator.StateChanged -= OnReloadStateChanged;
            MatrixSpaceReloadCoordinator.StateChanged += OnReloadStateChanged;
            MatrixSpaceSessionStore.instance.MigrateIfNeeded();
        }

        private void OnDisable()
        {
            if (_dormant)
                return;

            ModelCatalogService.Updated -= RefreshAllModelDropdowns;
            MatrixSpaceReloadCoordinator.StateChanged -= OnReloadStateChanged;
            CaptureAllSessions();
            MatrixSpaceSessionStore.instance.PersistNow();
            _matrixRain?.Dispose();
            _matrixRain = null;
        }

        public override void CreateGUI()
        {
            if (_dormant)
                return;

            TearDownUiBindings();
            try
            {
                base.CreateGUI();
                if (rootVisualElement.childCount == 0)
                    throw new InvalidOperationException("Matrix Space UI was created without any root elements.");
            }
            catch (Exception ex)
            {
                ShowRecoveryPanel(ex);
            }
        }

        protected override void OnGUICreated(VisualElement root)
        {
            root.style.backgroundColor = MatrixWindowBackground;

            _paneTemplate = EditorAssetLoader.LoadAssetAtPath<VisualTreeAsset>(_paneUxmlPaths, Logger);
            if (_paneTemplate == null)
                throw new InvalidOperationException("AgentPane.uxml could not be loaded.");

            if (MatrixSpaceSettings.RainBackground.Value)
                SetupMatrixRain(root);

            SetupTopbar(root);
            SetupSettingsPanel(root);

            var gridRoot = root.Q<VisualElement>("pane-grid");
            _grid = new PaneGridController(gridRoot, CreatePane);
            _grid.ApplyPreset(MatrixSpaceSessionStore.instance.GridPresetIndex);

            SetupSidebarAndRouter(root);

            // Added last so toasts render above every view (and the detail modal scrim).
            _toasts = new ToastLayer();
            root.Add(_toasts);
        }

        // ── Sidebar + view router ────────────────────────────────────────

        private void SetupSidebarAndRouter(VisualElement root)
        {
            var center = root.Q<VisualElement>("matrix-center");
            _breadcrumb = root.Q<Label>("topbar-breadcrumb");

            _sidebar = new SidebarView();
            root.Q<VisualElement>("sidebar-slot").Add(_sidebar);

            MatrixSpaceTaskStore.instance.MigrateIfNeeded();

            _board = new KanbanBoardView { PaneTargetsProvider = GetPaneTargets };
            _board.DispatchRequested += DispatchTask;
            _board.BoardChanged += UpdateSidebarBadges;
            _board.ToastRequested += (message, actionLabel, action) =>
                _toasts?.Show(message, actionLabel, action);
            _board.CardOpenRequested += card => CardDetailView.Open(
                rootVisualElement, card, GetPaneTargets, () =>
                {
                    _board.Refresh();
                    UpdateSidebarBadges();
                });
            _board.AddToClassList("matrix-center-view");

            _memoryPanel = new MemoryWorkspaceView();
            _memoryPanel.AddToClassList("matrix-center-view");

            _swarmBoard = new SwarmBoardView
            {
                SessionResolver = paneId => _sessions.TryGetValue(paneId, out var s) ? s : null,
            };
            _swarmBoard.LaunchRequested += LaunchMission;
            _swarmBoard.AddToClassList("matrix-center-view");

            _usageView = new UsageView();
            _usageView.AddToClassList("matrix-center-view");

            _router = new MatrixSpaceViewRouter(center);
            _router.Register(MatrixSpaceView.Construct, root.Q<VisualElement>("pane-grid"));
            _router.Register(MatrixSpaceView.Boards, _board, () =>
            {
                _board.Refresh();
                UpdateSidebarBadges();
            });
            _router.Register(MatrixSpaceView.Memory, _memoryPanel, () =>
            {
                _memoryPanel.Load();
                UpdateSidebarBadges();
            });
            _router.Register(MatrixSpaceView.Swarm, _swarmBoard, () => _swarmBoard.RefreshFromState());
            _router.Register(MatrixSpaceView.Usage, _usageView, () => _usageView.OnShown());
            _router.Register(MatrixSpaceView.Config, root.Q<VisualElement>("settings-panel"));

            _router.ViewChanged += view =>
            {
                MatrixSpaceSessionStore.instance.ActiveViewIndex = (int)view;
                MatrixSpaceSessionStore.instance.SchedulePersist();
                _sidebar.SetSelected(view);
                if (_breadcrumb != null)
                    _breadcrumb.text = $"matrix › {view.ToString().ToLowerInvariant()}";
                if (_presetControl != null)
                    _presetControl.style.display = view == MatrixSpaceView.Construct
                        ? DisplayStyle.Flex : DisplayStyle.None;
            };

            _sidebar.ItemClicked += view => _router.Show(view);

            var restored = (MatrixSpaceView)MatrixSpaceSessionStore.instance.ActiveViewIndex;
            if (!System.Enum.IsDefined(typeof(MatrixSpaceView), restored))
                restored = MatrixSpaceView.Construct;
            _router.Show(restored);
            UpdateSidebarBadges();
        }

        private void UpdateSidebarBadges()
        {
            if (_sidebar == null)
                return;
            _sidebar.SetBadge(MatrixSpaceView.Construct, _grid?.VisiblePaneCount ?? 0);
            _sidebar.SetBadge(MatrixSpaceView.Boards, MatrixSpaceTaskStore.instance.Tasks.Count);
            _sidebar.SetBadge(MatrixSpaceView.Memory, _memoryPanel?.NodeCount ?? 0);
        }

        // ── Panes / sessions ─────────────────────────────────────────────

        private AgentPaneView CreatePane(int index)
        {
            var record = MatrixSpaceSessionStore.instance.GetOrCreatePane(index);
            var pane = new AgentPaneView(_paneTemplate!);
            pane.ResetRequested += OnPaneResetRequested;
            pane.BackendChangeRequested += OnPaneBackendChangeRequested;
            pane.ModelChangeRequested += OnPaneModelChangeRequested;
            pane.ModeChangeRequested += OnPaneModeChangeRequested;
            pane.EffortChangeRequested += OnPaneEffortChangeRequested;
            pane.PromptDraftChanged += OnPromptDraftChanged;
            pane.ExpandToggleRequested += p => _grid?.ToggleExpanded(p);
            pane.Focused += OnPaneFocused;
            pane.SetBackendSelection(record.BackendId);
            pane.SetModelSelection(record.ModelId);
            pane.SetModeSelection(record.ModeId);
            pane.SetEffortSelection(record.EffortId);
            pane.SetPromptDraft(record.PromptDraft);
            pane.Bind(GetOrCreateSession(record));
            return pane;
        }

        private void OnPaneFocused(AgentPaneView focused)
        {
            if (_grid == null)
                return;
            foreach (var pane in _grid.Panes)
                pane.SetActive(pane == focused);
        }

        private AgentSession GetOrCreateSession(MatrixSpaceSessionStore.PaneRecord record)
        {
            if (_sessions.TryGetValue(record.PaneId, out var existing))
                return existing;

            var backend = AgentBackendCatalog.Create(record.BackendId);
            backend.SessionId = record.BackendSessionId;
            backend.ModelOverride = record.ModelId;
            backend.ModeOverride = record.ModeId;
            backend.EffortOverride = record.EffortId;
            var session = new AgentSession(record.PaneId, record.DisplayName, backend, record.BackendId, record);

            // Keep the store in sync so a domain reload can resume this conversation.
            session.Changed += s => MatrixSpaceSessionStore.instance.CaptureSession(record, s);
            session.TurnEnded += OnTurnEnded;

            _sessions[record.PaneId] = session;
            MatrixSpaceSessionStore.instance.CaptureSession(record, session);
            return session;
        }

        private void OnPaneResetRequested(AgentPaneView pane)
            => RestartPane(pane, keepBackend: true, newBackendId: null);

        private void OnPaneBackendChangeRequested(AgentPaneView pane, string backendId)
            => RestartPane(pane, keepBackend: false, newBackendId: backendId);

        private void OnPaneModelChangeRequested(AgentPaneView pane, string model)
        {
            var session = pane.Session;
            if (session == null)
                return;

            var store = MatrixSpaceSessionStore.instance;
            var index = store.Panes.FindIndex(p => p.PaneId == session.PaneId);
            if (index >= 0)
                store.Panes[index].ModelId = model;
            session.Backend.ModelOverride = model;
            store.SchedulePersist();
        }

        private void OnPaneModeChangeRequested(AgentPaneView pane, string mode)
        {
            var session = pane.Session;
            if (session == null)
                return;

            var store = MatrixSpaceSessionStore.instance;
            var index = store.Panes.FindIndex(p => p.PaneId == session.PaneId);
            if (index >= 0)
                store.Panes[index].ModeId = mode;
            session.Backend.ModeOverride = mode;
            store.SchedulePersist();
        }

        private void OnPaneEffortChangeRequested(AgentPaneView pane, string effort)
        {
            var session = pane.Session;
            if (session == null)
                return;

            var store = MatrixSpaceSessionStore.instance;
            var index = store.Panes.FindIndex(p => p.PaneId == session.PaneId);
            if (index >= 0)
                store.Panes[index].EffortId = effort;
            session.Backend.EffortOverride = effort;
            store.SchedulePersist();
        }

        private void OnPromptDraftChanged(AgentPaneView pane, string promptDraft)
        {
            var session = pane.Session;
            if (session == null)
                return;

            var store = MatrixSpaceSessionStore.instance;
            var record = store.Panes.Find(p => p.PaneId == session.PaneId);
            if (record == null)
                return;

            record.PromptDraft = promptDraft;
            store.SchedulePersist();
        }

        private void RestartPane(AgentPaneView pane, bool keepBackend, string? newBackendId)
        {
            var session = pane.Session;
            if (session == null)
                return;

            var store = MatrixSpaceSessionStore.instance;
            var index = store.Panes.FindIndex(p => p.PaneId == session.PaneId);
            if (index < 0)
                return;

            var backendId = keepBackend ? store.Panes[index].BackendId : newBackendId!;

            pane.Unbind();
            _sessions.Remove(session.PaneId);
            session.Dispose();

            store.ResetPane(index);
            var record = store.GetOrCreatePane(index);
            record.BackendId = backendId;
            store.SchedulePersist();
            pane.SetBackendSelection(backendId);
            pane.SetModelSelection(record.ModelId);
            pane.SetModeSelection(record.ModeId);
            pane.SetEffortSelection(record.EffortId);
            pane.SetPromptDraft(record.PromptDraft);
            pane.Bind(GetOrCreateSession(record));
        }

        // ── Task board wiring ────────────────────────────────────────────

        private void DispatchTask(MatrixSpaceTaskStore.TaskCard card, string? paneId)
        {
            var session = paneId != null
                ? (_sessions.TryGetValue(paneId, out var s) ? s : null)
                : FindFreeSession();

            if (session is not { } target || !target.CanSend)
            {
                Logger.LogWarning("{method} No free pane to dispatch task '{title}'.", nameof(DispatchTask), card.Title);
                return;
            }

            card.AssignedPaneId = target.PaneId;
            card.DispatchMarker = target.Transcript.Count;
            card.Failed = false;
            MatrixSpaceTaskStore.instance.LogActivity(card, "dispatched", $"→ {target.DisplayName}");
            MatrixSpaceTaskStore.instance.ReorderCard(card, TaskCardStatus.InProgress, int.MaxValue);

            target.Send($"[TASK] {card.Title}\n\n{card.Prompt}");
            _toasts?.Show($"'{card.Title}' dispatched to {target.DisplayName}");
            _board?.Refresh();
            UpdateSidebarBadges();
        }

        private AgentSession? FindFreeSession()
        {
            if (_grid == null)
                return null;

            for (var i = 0; i < _grid.VisiblePaneCount && i < _grid.Panes.Count; i++)
            {
                var session = _grid.Panes[i].Session;
                if (session is { CanSend: true })
                    return session;
            }

            return null;
        }

        private void OnTurnEnded(AgentSession session, bool success)
        {
            var store = MatrixSpaceTaskStore.instance;
            var changed = false;
            foreach (var card in store.Tasks)
            {
                if (card.Status != TaskCardStatus.InProgress || card.AssignedPaneId != session.PaneId)
                    continue;

                if (success)
                {
                    store.LogActivity(card, "agent-finished", $"{session.DisplayName} → IN REVIEW");
                    store.ReorderCard(card, TaskCardStatus.InReview, int.MaxValue);
                    _toasts?.Show($"'{card.Title}' → IN REVIEW");
                }
                else
                {
                    card.Failed = true;
                    card.AssignedPaneId = null;
                    store.LogActivity(card, "agent-failed", $"{session.DisplayName} → back to TO DO");
                    store.ReorderCard(card, TaskCardStatus.Todo, int.MaxValue);
                    _toasts?.Show($"'{card.Title}' failed — back to TO DO");
                }
                changed = true;
            }

            if (changed)
            {
                store.Persist();
                _board?.Refresh();
                UpdateSidebarBadges();
            }

            CaptureSession(session);
            MatrixSpaceSessionStore.instance.PersistNow();
        }

        private List<(string PaneId, string Label, bool CanSend)> GetPaneTargets()
        {
            var targets = new List<(string, string, bool)>();
            if (_grid == null)
                return targets;

            for (var i = 0; i < _grid.VisiblePaneCount && i < _grid.Panes.Count; i++)
            {
                var session = _grid.Panes[i].Session;
                if (session != null)
                    targets.Add((session.PaneId, session.DisplayName, session.CanSend));
            }

            return targets;
        }

        // ── Topbar ───────────────────────────────────────────────────────

        private void SetupTopbar(VisualElement root)
        {
            var presetLabels = new string[PaneGridController.Presets.Length];
            for (var i = 0; i < presetLabels.Length; i++)
                presetLabels[i] = PaneGridController.Presets[i].Label;

            _presetControl = new SegmentedControl(presetLabels);
            _presetControl.SetValueWithoutNotify(MatrixSpaceSessionStore.instance.GridPresetIndex);
            _presetControl.RegisterCallback<ChangeEvent<int>>(evt =>
            {
                MatrixSpaceSessionStore.instance.GridPresetIndex = evt.newValue;
                MatrixSpaceSessionStore.instance.SchedulePersist();
                _grid?.ApplyPreset(evt.newValue);
                UpdateSidebarBadges();
            });
            root.Q<VisualElement>("grid-preset-slot").Add(_presetControl);

            var usageChip = new UsageIndicatorView();
            usageChip.Clicked += () => _router?.Show(MatrixSpaceView.Usage);
            root.Q<VisualElement>("usage-slot")?.Add(usageChip);

            root.Q<VisualElement>("mcp-clients-slot")?.Add(new McpClientsIndicatorView());

            _reloadStatus = root.Q<Label>("reload-status");
            OnReloadStateChanged(MatrixSpaceReloadCoordinator.ActiveTurnCount,
                MatrixSpaceReloadCoordinator.ReloadPending);

            root.Q<Button>("kill-all-button").clicked += () =>
            {
                foreach (var session in _sessions.Values)
                    session.Cancel();
            };

            root.Q<Button>("settings-button").clicked += () => _router?.Show(MatrixSpaceView.Config);
        }

        // ── Settings (CONFIG) ────────────────────────────────────────────

        private void SetupSettingsPanel(VisualElement root)
        {
            BindToggle(root, "setting-matrix-persona",
                () => MatrixSpaceSettings.MatrixPersona.Value, v => MatrixSpaceSettings.MatrixPersona.Value = v);
            BindToggle(root, "setting-allow-feeder",
                () => MatrixSpaceSettings.AllowFeederMcpTools.Value, v => MatrixSpaceSettings.AllowFeederMcpTools.Value = v);
            BindToggle(root, "setting-shared-memory",
                () => MatrixSpaceSettings.SharedMemory.Value, v => MatrixSpaceSettings.SharedMemory.Value = v);
            BindToggle(root, "setting-skip-permissions",
                () => MatrixSpaceSettings.SkipAllPermissions.Value, v => MatrixSpaceSettings.SkipAllPermissions.Value = v);
            BindToggle(root, "setting-rain",
                () => MatrixSpaceSettings.RainBackground.Value, v =>
                {
                    MatrixSpaceSettings.RainBackground.Value = v;
                    CreateGUI();
                });

            var agentsContainer = root.Q<VisualElement>("agents-config-container");
            if (agentsContainer == null)
                return;

            agentsContainer.Add(BuildSetupAllRow());

            foreach (var preset in AgentBackendCatalog.Presets)
            {
                var section = new AgentConfigSectionView(preset);
                section.ModelsEdited += RefreshAllModelDropdowns;
                agentsContainer.Add(section);
            }
        }

        /// <summary>
        /// One-click machine bootstrap: runs every agent's automated setup sequentially
        /// (install CLI + write MCP config). Only per-account LOGIN stays manual.
        /// </summary>
        private VisualElement BuildSetupAllRow()
        {
            var row = new VisualElement();
            row.AddToClassList("agent-setup-all-row");

            var setupAllButton = new Button(AgentSetupService.RunSetupAll) { text = "SETUP ALL AGENTS" };
            setupAllButton.AddToClassList("btn-primary");
            setupAllButton.tooltip = "Installs every supported CLI and writes their MCP configs. " +
                                     "Afterwards, use each agent's LOGIN button to sign in.";
            row.Add(setupAllButton);

            var hint = new Label("Installs CLIs + MCP config automatically; sign-in stays manual (LOGIN).");
            hint.AddToClassList("agent-setup-all-hint");
            row.Add(hint);

            System.Action refresh = () => setupAllButton.SetEnabled(!AgentSetupService.IsAnyRunning);
            AgentSetupService.Changed += refresh;
            row.RegisterCallback<DetachFromPanelEvent>(_ => AgentSetupService.Changed -= refresh);
            refresh();

            return row;
        }

        private void RefreshAllModelDropdowns()
        {
            if (_grid == null)
                return;

            var store = MatrixSpaceSessionStore.instance;
            for (var i = 0; i < _grid.Panes.Count; i++)
            {
                var record = store.GetOrCreatePane(i);
                _grid.Panes[i].RefreshModelChoices(record.BackendId);
                // Keep the persisted selection even when it is not in the refreshed list
                // (e.g. an older model that dropped out of the live catalog).
                if (!string.IsNullOrEmpty(record.ModelId))
                    _grid.Panes[i].SetModelSelection(record.ModelId);
            }
        }

        private static void BindToggle(VisualElement root, string name, System.Func<bool> get, System.Action<bool> set)
        {
            var toggle = root.Q<Toggle>(name);
            if (toggle == null)
                return;
            toggle.SetValueWithoutNotify(get());
            toggle.RegisterValueChangedCallback(evt => set(evt.newValue));
        }

        // ── Swarm mission ────────────────────────────────────────────────

        private void LaunchMission(string mission, List<SwarmMission.Role> roles)
        {
            if (_grid == null)
                return;

            MatrixSpacePaths.EnsureCreated();

            // Grow the grid until enough panes are visible for the whole team.
            var presetIndex = MatrixSpaceSessionStore.instance.GridPresetIndex;
            while (PaneGridController.Presets[presetIndex].PaneCount < roles.Count &&
                   presetIndex < PaneGridController.Presets.Length - 1)
            {
                presetIndex++;
            }

            if (presetIndex != MatrixSpaceSessionStore.instance.GridPresetIndex)
            {
                MatrixSpaceSessionStore.instance.GridPresetIndex = presetIndex;
                _presetControl?.SetValueWithoutNotify(presetIndex);
                _grid.ApplyPreset(presetIndex);
            }

            // Dispatch each role to a free pane (coordinator first) and record the
            // role → pane binding so the swarm board can rebind after a domain reload.
            var swarm = MatrixSpaceSessionStore.instance.Swarm;
            swarm.Active = true;
            swarm.Mission = mission;
            swarm.StartedTicksUtc = System.DateTime.UtcNow.Ticks;
            swarm.Assignments.Clear();

            var used = new HashSet<string>();
            foreach (var role in roles)
            {
                AgentSession? target = null;
                for (var i = 0; i < _grid.VisiblePaneCount && i < _grid.Panes.Count; i++)
                {
                    var session = _grid.Panes[i].Session;
                    if (session is { CanSend: true } && !used.Contains(session.PaneId))
                    {
                        target = session;
                        break;
                    }
                }

                if (target == null)
                {
                    Logger.LogWarning("{method} Not enough free panes for role {role}.", nameof(LaunchMission), role.Label);
                    continue;
                }

                used.Add(target.PaneId);
                swarm.Assignments.Add(new MatrixSpaceSessionStore.RoleAssignment
                {
                    RoleId = role.Id,
                    PaneId = target.PaneId,
                });
                target.Send(SwarmMission.BuildPrompt(role, mission, roles, target.DisplayName));
            }

            MatrixSpaceSessionStore.instance.SchedulePersist();
        }

        // ── Matrix rain background ───────────────────────────────────────

        private void SetupMatrixRain(VisualElement root)
        {
            _matrixRain ??= new MatrixRainRenderer();

            if (_matrixRainContainer == null)
            {
                var container = new IMGUIContainer(DrawMatrixRain)
                {
                    name = "matrix-rain-background",
                    pickingMode = PickingMode.Ignore,
                };
                container.style.position = Position.Absolute;
                container.style.top = 0;
                container.style.left = 0;
                container.style.right = 0;
                container.style.bottom = 0;
                container.schedule
                    .Execute(() => container.MarkDirtyRepaint())
                    .Every(MatrixRainRepaintIntervalMs);
                _matrixRainContainer = container;
                _lastMatrixRainStep = EditorApplication.timeSinceStartup;
            }

            if (_matrixRainContainer.parent != root)
                root.Insert(0, _matrixRainContainer);
            else
                _matrixRainContainer.SendToBack();
        }

        private void DrawMatrixRain()
        {
            if (_matrixRain == null || _matrixRainContainer == null)
                return;
            if (Event.current.type != EventType.Repaint)
                return;

            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastMatrixRainStep), 0f, 0.1f);
            _lastMatrixRainStep = now;

            var rect = _matrixRainContainer.contentRect;
            if (rect.width < 1f || rect.height < 1f)
                return;

            _matrixRain.Step(dt);
            _matrixRain.Draw(rect.width, rect.height);
        }

        // ── Lifecycle ────────────────────────────────────────────────────

        private void CaptureSession(AgentSession session)
        {
            var store = MatrixSpaceSessionStore.instance;
            var record = store.Panes.Find(p => p.PaneId == session.PaneId);
            if (record != null)
                store.CaptureSession(record, session);
        }

        private void CaptureAllSessions()
        {
            foreach (var session in _sessions.Values)
                CaptureSession(session);
        }

        /// <summary>
        /// UI Toolkit can call CreateGUI repeatedly without destroying the EditorWindow.
        /// Remove view subscriptions while deliberately retaining the live sessions.
        /// </summary>
        private void TearDownUiBindings()
        {
            if (_grid != null)
            {
                foreach (var pane in _grid.Panes)
                    pane.Unbind();
            }

            _swarmBoard?.UnbindSessions();
            _grid = null;
            _paneTemplate = null;
            _presetControl = null;
            _board = null;
            _memoryPanel = null;
            _swarmBoard = null;
            _usageView = null;
            _router = null;
            _sidebar = null;
            _breadcrumb = null;
            _reloadStatus = null;
            _toasts = null;
        }

        private void ShowRecoveryPanel(Exception exception)
        {
            Debug.LogException(exception);
            var root = rootVisualElement;
            root.Clear();
            root.style.backgroundColor = MatrixWindowBackground;
            ApplyStyleSheets(root);

            var panel = new VisualElement { name = "matrix-recovery-panel" };
            panel.style.flexGrow = 1;
            panel.style.justifyContent = Justify.Center;
            panel.style.alignItems = Align.Center;
            panel.style.paddingLeft = 32;
            panel.style.paddingRight = 32;

            var title = new Label("MATRIX SPACE RECOVERY");
            title.style.fontSize = 18;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.2f, 1f, 0.45f);
            panel.Add(title);

            var message = new Label($"The interface could not be rebuilt. Your sessions were preserved.\n\n{exception.Message}");
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.maxWidth = 720;
            message.style.marginTop = 12;
            message.style.marginBottom = 18;
            panel.Add(message);

            var retry = new Button(CreateGUI) { text = "RETRY UI" };
            retry.AddToClassList("btn-primary");
            panel.Add(retry);
            root.Add(panel);
        }

        private void OnReloadStateChanged(int activeTurns, bool reloadPending)
        {
            if (_reloadStatus == null)
                return;

            var visible = activeTurns > 0 || reloadPending;
            _reloadStatus.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!visible)
                return;

            var noun = activeTurns == 1 ? "AGENT" : "AGENTS";
            _reloadStatus.text = $"RELOAD DEFERRED · {activeTurns} {noun}";
            _reloadStatus.tooltip = reloadPending
                ? "Compilation succeeded. Unity will reload after every active agent process exits."
                : "Assembly reload is locked while active Matrix Space turns finish.";
        }

        private void OnDestroy()
        {
            if (_dormant)
                return;

            ModelCatalogService.Updated -= RefreshAllModelDropdowns;
            MatrixSpaceReloadCoordinator.StateChanged -= OnReloadStateChanged;
            CaptureAllSessions();
            MatrixSpaceSessionStore.instance.PersistNow();
            TearDownUiBindings();

            foreach (var session in _sessions.Values)
                session.Dispose();
            _sessions.Clear();
        }
    }
}

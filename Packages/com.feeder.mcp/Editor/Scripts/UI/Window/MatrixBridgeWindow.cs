#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Feeder.ToolHost;
using Feeder.ToolHost.Common.Model;
using Feeder.MCP.Editor.MatrixSpace;
using Feeder.MCP.Editor.Utils;
using Feeder.MCP.Runtime.Utils;
using Feeder.Fmp;
using Feeder.Logging;
using Feeder.Rx;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using AgentConfig = Feeder.ToolHost.AgentConfig;
using LogLevel = Feeder.MCP.Runtime.Utils.LogLevel;

namespace Feeder.MCP.Editor.UI
{
    public class MatrixBridgeWindow : McpWindowBase
    {
        private static readonly string[] _windowUxmlPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uxml/MatrixBridge.uxml");
        private static readonly string[] _chipUxmlPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uxml/BridgeChip.uxml");
        private static readonly string[] _windowUssPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uss/MatrixBridge.uss");
        private static readonly string[] _phaseClasses =
        {
            "mb-state--offline", "mb-state--linking", "mb-state--online", "mb-state--working", "mb-state--playtest", "mb-state--fault",
        };
        private static readonly LogLevel[] _menuLogLevels =
        {
            LogLevel.Trace, LogLevel.Debug, LogLevel.Info, LogLevel.Warning, LogLevel.Error,
        };

        private const long RainIntervalMs = 50;
        private const float EnergyEase = 6f;
        private const float RainCenterClear = 0.93f;
        private const int MinPort = 1024;
        private const int MaxPort = 65535;
        private const int MaxNoteLength = 72;

        protected override string WindowTitle => "Matrix Bridge";
        protected override string[] WindowUxmlPaths => _windowUxmlPaths;
        protected override string[] WindowUssPaths => _windowUssPaths;

        private readonly CompositeDisposable _subscriptions = new();
        private readonly CompositeDisposable _titleSubscription = new();
        private IDisposable? _clientsSubscription;
        private readonly List<VisualElement> _chipViews = new();
        private readonly List<string> _clientNames = new();

        private Label? _state;
        private Label? _address;
        private Label? _note;
        private Label? _menu;
        private VisualElement? _core;
        private VisualElement? _chips;
        private TextField? _portField;

        private MatrixRainRenderer? _rain;
        private IMGUIContainer? _rainView;
        private IVisualElementScheduledItem? _rainTick;
        private IVisualElementScheduledItem? _workingExpiry;
        private double _lastEnergyStep;
        private float _energy = 0.08f;
        private float _energyTarget = 0.08f;

        private BridgePhase _phase = BridgePhase.Offline;
        private bool _previewActive;
        private BridgePhase _previewPhase;
        private int _previewClients;
        private string? _previewFault;
        private bool _dormant;
        private bool _titleDim;

        public static MatrixBridgeWindow ShowWindow()
        {
            MatrixActivation.RequestWindow();
            var isNew = !HasOpenInstances<MatrixBridgeWindow>();
            var window = GetWindow<MatrixBridgeWindow>("Matrix Bridge");
            window.SetupWindowWithIcon(dim: window._titleDim);
            window.minSize = new Vector2(380, 300);
            if (isNew)
                window.position = new Rect(window.position.x, window.position.y, 460, 340);
            window.Focus();
            return window;
        }

        public static void ShowWindowVoid() => ShowWindow();

        internal void Preview(BridgePhase phase, int clients, string? fault)
        {
            _previewActive = true;
            _previewPhase = phase;
            _previewClients = clients;
            _previewFault = fault;
            Refresh();
        }

        internal void ClearPreview()
        {
            _previewActive = false;
            Refresh();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _dormant = MatrixActivation.CloseIfDormant(this);
            if (_dormant)
                return;
            _titleDim = false;
            SetupWindowWithIcon();
            MatrixPluginEditor.ConnectionState
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnTitleState)
                .AddTo(_titleSubscription);
        }

        private void OnTitleState(FmpState state)
        {
            var dim = state != FmpState.Connected;
            if (dim == _titleDim)
                return;
            _titleDim = dim;
            SetupWindowWithIcon(dim: dim);
        }

        private void OnDisable()
        {
            _titleSubscription.Clear();
            Unbind();
            if (_rain != null)
            {
                _rain.Dispose();
                _rain = null;
            }
        }

        private void OnFocus() => RefreshChips();

        private void OnBecameVisible() => _rainTick?.Resume();

        private void OnBecameInvisible() => _rainTick?.Pause();

        public override void CreateGUI()
        {
            if (_dormant)
                return;
            Unbind();
            base.CreateGUI();
        }

        protected override void OnGUICreated(VisualElement root)
        {
            var bridgeRoot = root.Q<VisualElement>("mb-root");
            _core = root.Q<VisualElement>("mb-core");
            _state = root.Q<Label>("mb-state");
            _address = root.Q<Label>("mb-address");
            _note = root.Q<Label>("mb-note");
            _menu = root.Q<Label>("mb-menu");
            _chips = root.Q<VisualElement>("mb-chips");

            if (bridgeRoot == null || _state == null || _address == null || _chips == null)
            {
                Logger.LogError("{method} MatrixBridge.uxml is missing mb-root, mb-state, mb-address or mb-chips.", nameof(OnGUICreated));
                return;
            }

            if (MatrixSpaceSettings.RainBackground.Value)
                SetupRain(bridgeRoot);

            _state.UnregisterCallback<ClickEvent>(OnStateClicked);
            _state.RegisterCallback<ClickEvent>(OnStateClicked);
            _address.UnregisterCallback<ClickEvent>(OnAddressClicked);
            _address.RegisterCallback<ClickEvent>(OnAddressClicked);
            if (_menu != null)
            {
                _menu.UnregisterCallback<ClickEvent>(OnMenuClicked);
                _menu.RegisterCallback<ClickEvent>(OnMenuClicked);
            }

            BuildChips();
            BridgeFont.Apply(bridgeRoot);
            Bind();
            Refresh();
        }

        private void Bind()
        {
            MatrixPluginEditor.ConnectionState
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnConnectionState)
                .AddTo(_subscriptions);
            McpServerManager.ServerStatus
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnServerStatus)
                .AddTo(_subscriptions);
            Observable.Merge(McpServerManager.PendingNote, McpServerManager.ServerFault, McpServerManager.PortMoveNote)
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnServerNote)
                .AddTo(_subscriptions);
            McpServerManager.LastInstallError
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnInstallError)
                .AddTo(_subscriptions);
            AgentActivity.Changed
                .Subscribe(OnAgentActivity)
                .AddTo(_subscriptions);
            _subscriptions.Add(MatrixPluginEditor.SubscribeOnChanged(OnConfigChanged, invokeImmediately: false));
            MatrixPluginEditor.PluginProperty
                .WhereNotNull()
                .Subscribe(OnPlugin)
                .AddTo(_subscriptions);
        }

        private void Unbind()
        {
            _subscriptions.Clear();
            _clientsSubscription?.Dispose();
            _clientsSubscription = null;
            _rainTick?.Pause();
            _rainTick = null;
            _workingExpiry?.Pause();
            _workingExpiry = null;
            _rainView = null;
            _chipViews.Clear();
            _portField = null;
        }

        private void OnConnectionState(FmpState state) => Refresh();

        private void OnServerStatus(McpServerStatus status) => Refresh();

        private void OnInstallError(string? error) => Refresh();

        private void OnServerNote(string? note) => Refresh();

        private void OnConfigChanged(MatrixPlugin.UnityConnectionConfig config) => Refresh();

        private void OnAgentActivity(Unit _) => Refresh();

        private void OnPlugin(IToolHost plugin)
        {
            _clientsSubscription?.Dispose();
            _clientsSubscription = plugin.McpManager.OnClientsChanged
                .ObserveOnCurrentSynchronizationContext()
                .Subscribe(OnClients);
            OnClients(plugin.McpManager.ActiveClients);
        }

        private void OnClients(IEnumerable<McpClientData> clients)
        {
            _clientNames.Clear();
            foreach (var client in clients.Where(c => c.IsConnected))
            {
                var name = string.IsNullOrEmpty(client.ClientTitle) ? client.ClientName : client.ClientTitle;
                _clientNames.Add($"{name} {client.ClientVersion}".Trim());
            }
            Refresh();
        }

        private BridgePhase ResolvePhase()
        {
            var connection = MatrixPluginEditor.ConnectionState.CurrentValue;
            return BridgeStatus.Resolve(
                MatrixActivation.IsEnabled && McpServerManager.PendingNote.CurrentValue != null,
                connection == FmpState.Connected,
                connection == FmpState.Connecting || connection == FmpState.Reconnecting,
                McpServerManager.ServerStatus.CurrentValue,
                CurrentFault,
                AgentActivity.IsAgentPlay,
                AgentActivity.IsWorking);
        }

        private void Refresh()
        {
            if (_state == null || _address == null)
                return;

            var phase = _previewActive ? _previewPhase : ResolvePhase();
            var clients = _previewActive ? _previewClients : _clientNames.Count;
            var fault = _previewActive ? _previewFault : CurrentFault;
            _phase = phase;

            _state.text = BridgeStatus.Word(phase);
            var phaseClass = _phaseClasses[(int)phase];
            foreach (var stateClass in _phaseClasses)
                _state.EnableInClassList(stateClass, stateClass == phaseClass);

            var host = MatrixPluginEditor.Host.Replace("https://", string.Empty).Replace("http://", string.Empty).TrimEnd('/');
            var count = clients == 0 ? "no agents" : clients == 1 ? "1 agent" : $"{clients} agents";
            _address.text = $"{host}  -  {count}";
            _address.tooltip = _clientNames.Count > 0 && !_previewActive
                ? "Connected agents:\n" + string.Join("\n", _clientNames)
                : "No agent is connected to the bridge.";

            if (_note != null)
            {
                _note.text = NoteFor(phase, fault);
                _note.EnableInClassList("mb-note--visible", _note.text.Length > 0);
            }

            if (phase == BridgePhase.Working && !_previewActive)
                ScheduleWorkingExpiry();

            _energyTarget = BridgeStatus.RainEnergy(phase);
            _rainTick?.Resume();
        }

        private string NoteFor(BridgePhase phase, string? fault) => phase switch
        {
            BridgePhase.Fault => FirstLine(fault),
            BridgePhase.Working => FirstLine("> " + (_previewActive ? "preview" :AgentActivity.LastWriteTool)),
            BridgePhase.PlayTesting => "agent is play testing the game",
            BridgePhase.Linking => _previewActive ? string.Empty : FirstLine(LinkingNote()),
            _ => _previewActive ? string.Empty : FirstLine(McpServerManager.PortMoveNote.CurrentValue),
        };

        private static string? CurrentFault
            => McpServerManager.LastInstallError.CurrentValue ?? McpServerManager.ServerFault.CurrentValue;

        private static string LinkingNote()
        {
            var pending = McpServerManager.PendingNote.CurrentValue;
            if (!string.IsNullOrEmpty(pending))
                return pending!;
            return McpServerManager.ServerStatus.CurrentValue switch
            {
                McpServerStatus.Installing => "installing server",
                McpServerStatus.Starting => "starting server",
                _ => $"waiting for server on :{MatrixPluginEditor.Port}",
            };
        }

        private void ScheduleWorkingExpiry()
        {
            if (_state == null)
                return;
            _workingExpiry?.Pause();
            var delayMs = (long)(AgentActivity.WorkingSecondsLeft * 1000) + 50;
            _workingExpiry = _state.schedule.Execute(Refresh).StartingIn(delayMs);
        }

        private static string FirstLine(string? text)
        {
            if (text == null || text.Length == 0)
                return string.Empty;
            var end = text.IndexOfAny(new[] { '\r', '\n' });
            var line = end >= 0 ? text.Substring(0, end) : text;
            return line.Length > MaxNoteLength ? line.Substring(0, MaxNoteLength) + "..." : line;
        }

        private void OnStateClicked(ClickEvent evt)
        {
            switch (_phase)
            {
                case BridgePhase.Offline:
                    MatrixActivation.Enable();
                    break;
                case BridgePhase.Linking:
                case BridgePhase.Online:
                case BridgePhase.Working:
                case BridgePhase.PlayTesting:
                    MatrixActivation.Disable();
                    break;
                case BridgePhase.Fault:
                    Retry();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_phase), _phase, null);
            }
        }

        private static async void Retry()
        {
            try
            {
                McpServerManager.ResetFault();
                if (await McpServerManager.InstallServerBinaryIfNeeded())
                    MatrixActivation.Enable();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private void BuildChips()
        {
            if (_chips == null)
                return;

            _chips.Clear();
            _chipViews.Clear();

            var template = EditorAssetLoader.LoadAssetAtPath<VisualTreeAsset>(_chipUxmlPaths, Logger);
            if (template == null)
            {
                Logger.LogError("{method} BridgeChip.uxml could not be loaded.", nameof(BuildChips));
                return;
            }

            foreach (var agent in BridgeAgents.Chips())
            {
                var chip = CreateChip(template, BridgeAgents.ShortName(agent));
                if (chip == null)
                    continue;
                chip.userData = agent;
                chip.RegisterCallback<ClickEvent>(OnChipClicked);
                _chips.Add(chip);
                _chipViews.Add(chip);
            }

            var overflow = BridgeAgents.Overflow().Count;
            if (overflow > 0)
            {
                var more = CreateChip(template, $"+{overflow}");
                if (more != null)
                {
                    more.AddToClassList("mb-chip--more");
                    more.RegisterCallback<ClickEvent>(OnMoreClicked);
                    _chips.Add(more);
                }
            }

            RefreshChips();
        }

        private VisualElement? CreateChip(VisualTreeAsset template, string text)
        {
            var chip = template.Instantiate().Q<VisualElement>(className: "mb-chip");
            var label = chip?.Q<Label>("mb-chip-text");
            if (chip == null || label == null)
            {
                Logger.LogError("{method} BridgeChip.uxml is missing .mb-chip or #mb-chip-text.", nameof(CreateChip));
                return null;
            }
            label.text = text;
            return chip;
        }

        private void RefreshChips()
        {
            foreach (var chip in _chipViews)
            {
                if (chip.userData is not AgentConfig.AiAgentConfigurator agent)
                    continue;
                try
                {
                    var wiring = BridgeAgents.GetState(agent);
                    chip.EnableInClassList("mb-chip--wired", wiring == AgentWiring.Wired);
                    chip.EnableInClassList("mb-chip--stale", wiring == AgentWiring.Stale);
                    chip.tooltip = BridgeAgents.Describe(agent);
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "{method} could not read the wiring state of '{agent}'.", nameof(RefreshChips), agent.AgentId);
                }
            }
        }

        private void OnChipClicked(ClickEvent evt)
        {
            if (evt.currentTarget is VisualElement chip && chip.userData is AgentConfig.AiAgentConfigurator agent)
                ToggleAgent(agent);
        }

        private void ToggleAgent(AgentConfig.AiAgentConfigurator agent)
        {
            try
            {
                BridgeAgents.Toggle(agent);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "{method} could not toggle '{agent}'.", nameof(ToggleAgent), agent.AgentId);
            }
            RefreshChips();
        }

        private void ToggleAgentFromMenu(object agent)
        {
            if (agent is AgentConfig.AiAgentConfigurator configurator)
                ToggleAgent(configurator);
        }

        private void OnMoreClicked(ClickEvent evt)
        {
            if (evt.currentTarget is not VisualElement chip)
                return;

            var menu = new GenericMenu();
            foreach (var agent in BridgeAgents.Overflow())
            {
                var wired = false;
                try { wired = BridgeAgents.GetState(agent) == AgentWiring.Wired; }
                catch (Exception ex) { Logger.LogError(ex, "{method} could not read '{agent}'.", nameof(OnMoreClicked), agent.AgentId); }
                menu.AddItem(new GUIContent(agent.AgentName), wired, ToggleAgentFromMenu, agent);
            }
            menu.DropDown(chip.worldBound);
        }

        private void OnMenuClicked(ClickEvent evt)
        {
            if (_menu == null)
                return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy endpoint"), false, CopyEndpoint);
            if (_phase == BridgePhase.Offline)
                menu.AddItem(new GUIContent("Change port"), false, BeginPortEdit);
            else
                menu.AddDisabledItem(new GUIContent("Change port"));
            foreach (var level in _menuLogLevels)
                menu.AddItem(new GUIContent("Logging/" + level), MatrixPluginEditor.LogLevel == level, SetLogLevel, level);
            menu.AddItem(new GUIContent("Skills"), false, MenuItems.ShowSkills);
            menu.AddItem(new GUIContent("Rain"), MatrixSpaceSettings.RainBackground.Value, ToggleRain);
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Open logs"), false, MenuItems.OpenServerLogs);
            menu.AddItem(new GUIContent("Open config file"), false, RevealConfigFile);
            menu.AddItem(new GUIContent("Reinstall server"), false, ReinstallServer);
            menu.DropDown(_menu.worldBound);
        }

        private static void CopyEndpoint() => GUIUtility.systemCopyBuffer = McpServerManager.GetMcpEndpointUrl(MatrixPluginEditor.Host);

        private static void SetLogLevel(object level)
        {
            if (level is not LogLevel value)
                return;
            MatrixPluginEditor.LogLevel = value;
            MatrixPluginEditor.Instance.Save();
        }

        private void ToggleRain()
        {
            MatrixSpaceSettings.RainBackground.Value = !MatrixSpaceSettings.RainBackground.Value;
            CreateGUI();
        }

        private static void RevealConfigFile() => EditorUtility.RevealInFinder(MatrixPluginEditor.AssetsFileAbsolutePath);

        private static void ReinstallServer() => _ = McpServerManager.InstallServerBinaryIfNeeded(force: true);

        private void OnAddressClicked(ClickEvent evt)
        {
            if (evt.clickCount == 2)
                BeginPortEdit();
        }

        private void BeginPortEdit()
        {
            if (_phase != BridgePhase.Offline || _address == null || _core == null || _portField != null)
                return;

            var field = new TextField { value = MatrixPluginEditor.Port.ToString() };
            field.AddToClassList("mb-port");
            BridgeFont.Apply(field);
            field.RegisterCallback<KeyDownEvent>(OnPortKey);
            field.RegisterCallback<FocusOutEvent>(OnPortFocusOut);
            _core.Insert(_core.IndexOf(_address), field);
            _address.style.display = DisplayStyle.None;
            _portField = field;
            field.Focus();
            field.SelectAll();
        }

        private void OnPortKey(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                CommitPort();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                EndPortEdit();
                evt.StopPropagation();
            }
        }

        private void OnPortFocusOut(FocusOutEvent evt) => EndPortEdit();

        private void CommitPort()
        {
            var text = _portField?.value;
            EndPortEdit();
            if (!int.TryParse(text, out var port) || port < MinPort || port > MaxPort)
            {
                Logger.LogWarning("{method} port must be a number from {min} to {max}.", nameof(CommitPort), MinPort, MaxPort);
                return;
            }
            if (port == MatrixPluginEditor.Port)
                return;

            McpServerManager.StopServer();
            MatrixPluginEditor.LocalHost = $"http://localhost:{port}";
            MatrixPluginEditor.Instance.Save();
            if (MatrixPluginEditor.Instance.HasToolHostInstance)
            {
                MatrixPluginEditor.Instance.DisposeToolHostInstance();
                MatrixPluginEditor.Instance.BuildToolHostIfNeeded();
            }
            Refresh();
            RefreshChips();
        }

        private void EndPortEdit()
        {
            var field = _portField;
            if (field == null)
                return;
            _portField = null;
            field.UnregisterCallback<KeyDownEvent>(OnPortKey);
            field.UnregisterCallback<FocusOutEvent>(OnPortFocusOut);
            field.RemoveFromHierarchy();
            if (_address != null)
                _address.style.display = DisplayStyle.Flex;
        }

        private void SetupRain(VisualElement bridgeRoot)
        {
            _rain ??= new MatrixRainRenderer { Energy = _energy, CenterClear = RainCenterClear };

            var container = new IMGUIContainer(PaintRain)
            {
                name = "mb-rain",
                pickingMode = PickingMode.Ignore,
            };
            container.AddToClassList("mb-rain");
            bridgeRoot.Insert(0, container);
            _rainView = container;

            _lastEnergyStep = EditorApplication.timeSinceStartup;
            _rainTick = container.schedule.Execute(TickRain).Every(RainIntervalMs);
        }

        private void TickRain(TimerState timer)
        {
            if (_rain == null || _rainView == null)
                return;

            var now = EditorApplication.timeSinceStartup;
            var dt = Mathf.Clamp((float)(now - _lastEnergyStep), 0f, 0.25f);
            _lastEnergyStep = now;
            _energy += (_energyTarget - _energy) * (1f - Mathf.Exp(-EnergyEase * dt));
            _rain.Energy = _energy;
            _rainView.MarkDirtyRepaint();

            if (Mathf.Abs(_energy - _energyTarget) < 0.004f && _energyTarget < 0.1f)
                _rainTick?.Pause();
        }

        private void PaintRain()
        {
            if (_rain != null && _rainView != null)
                _rain.Paint(_rainView.contentRect);
        }
    }
}

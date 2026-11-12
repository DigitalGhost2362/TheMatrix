#nullable enable
using System;
using System.IO;
using System.Linq;
using Feeder.MCP.Editor.Utils;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Feeder.MCP.Editor.UI
{
    public class GettingStartedWindow : McpWindowBase
    {
        public const string Title = "Getting Started";

        static readonly string[] _windowUssPaths = EditorAssetLoader.GetEditorAssetPaths("Editor/UI/uss/GettingStarted.uss");

        static readonly (string group, (string file, string title, string text)[] pages)[] DocGroups =
        {
            ("Start here", new[]
            {
                ("install.html", "Install and update", "Add the package and keep it current."),
                ("setup.html", "Turn the bridge on", "What Setup changes and how to undo it."),
                ("connect.html", "Connect your AI client", "Claude Code, Codex, Cursor and others."),
                ("first-session.html", "Your first session", "Ask an agent for real Editor work."),
            }),
            ("Daily use", new[]
            {
                ("bridge-window.html", "The Bridge window", "Status, agents, lock and play test frame."),
                ("skills-window.html", "Choose which tools are on", "Token cost per tool, groups and presets."),
                ("queries.html", "One query, many objects", "Read and change scenes with one pipeline."),
                ("tools.html", "Tool catalog", "Every tool and what it does."),
            }),
            ("Teams and automation", new[]
            {
                ("editor-lock.html", "Several agents, one Editor", "The editor lock and the queue."),
                ("team.html", "In a team project", "What to commit and what to ignore."),
                ("batch-ci.html", "Batch mode and CI", "Run tools without an open Editor window."),
                ("runtime-tools.html", "Runtime tools", "Tools that work inside a running build."),
            }),
            ("Help", new[]
            {
                ("troubleshooting.html", "Troubleshooting", "Offline, timeouts, missing tools."),
                ("health.html", "Health and timeouts", "editor-health, tool-call and slow calls."),
                ("reference.html", "Menus, files and settings", "Every menu item and file the bridge uses."),
                ("changelog.html", "What's new", "Changes in each release."),
            }),
        };

        protected override string WindowTitle => Title;
        protected override string[] WindowUxmlPaths => Array.Empty<string>();
        protected override string[] WindowUssPaths => _windowUssPaths;

        [SerializeField] string[] _results = Array.Empty<string>();

        public static GettingStartedWindow ShowWindow()
        {
            var isNew = !HasOpenInstances<GettingStartedWindow>();
            var window = GetWindow<GettingStartedWindow>(Title);
            window.SetupWindowWithIcon();
            window.minSize = new Vector2(640, 480);
            if (isNew)
            {
                var main = EditorGUIUtility.GetMainWindowPosition();
                window.position = new Rect(main.x + (main.width - 820) / 2f, main.y + (main.height - 720) / 2f, 820, 720);
            }
            window.Focus();
            return window;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            SetupWindowWithIcon();
        }

        public override void CreateGUI()
        {
            rootVisualElement.Clear();
            ApplyStyleSheets(rootVisualElement);
            rootVisualElement.AddToClassList("gs-root");

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.AddToClassList("gs-scroll");
            rootVisualElement.Add(scroll);

            scroll.Add(BuildHeader());
            scroll.Add(BuildSetupSection());
            scroll.Add(BuildDocsSection());
            scroll.Add(BuildNextSection());
        }

        #region Header

        VisualElement BuildHeader()
        {
            var header = Element("gs-header");

            var logo = Element("gs-header__logo");
            var icon = EditorAssetLoader.LoadAssetAtPath<Texture2D>(EditorAssetLoader.PackageLogoIcon);
            if (icon != null)
                logo.style.backgroundImage = icon;
            header.Add(logo);

            var titles = Element("gs-header__titles");
            titles.Add(Text("Matrix Bridge", "gs-header__title"));
            titles.Add(Text("Getting Started  -  turn the bridge on, then learn it from the docs", "gs-header__sub"));
            header.Add(titles);

            var side = Element("gs-header__side");
            var on = MatrixActivation.IsEnabled;
            var pill = Text(on ? "ONLINE" : "OFF", "gs-pill");
            pill.AddToClassList(on ? "gs-pill--on" : "gs-pill--off");
            side.Add(pill);
            side.Add(Text("v" + (Package?.version ?? "?"), "gs-header__version"));
            header.Add(side);

            var wrap = Element("gs-header-wrap");
            wrap.Add(header);

            var links = Element("gs-links");
            links.Add(LinkButton("Documentation", () => OpenDoc("index.html"), primary: true));
            links.Add(LinkButton("What's new", () => OpenDoc("changelog.html")));
            links.Add(LinkButton("Troubleshooting", () => OpenDoc("troubleshooting.html")));
            links.Add(LinkButton("Bridge window", () => MatrixBridgeWindow.ShowWindow()));
            wrap.Add(links);
            return wrap;
        }

        #endregion

        #region Setup

        VisualElement BuildSetupSection()
        {
            var done = _results.Length == MatrixSetup.Steps.Length;
            var section = Section("1", "Set up this machine",
                "Starts the local server, connects the Editor and prepares this project. Matrix does nothing until this runs. " +
                "Every change is listed here before you press Run.");

            var card = Element("gs-card", "gs-card--setup");
            if (done)
            {
                var failed = _results.Count(result => result.Contains("FAILED"));
                var banner = Element("gs-banner", failed == 0 ? "gs-banner--ok" : "gs-banner--bad");
                banner.Add(Text(failed == 0 ? "Setup finished" : $"Setup finished with {failed} problem(s)", "gs-banner__title"));
                banner.Add(Text($"Next: ask your agent to run {MatrixSetup.NextStep} for faster play mode and a Windows Defender exclusion.", "gs-banner__text"));
                card.Add(banner);
            }

            for (var i = 0; i < MatrixSetup.Steps.Length; i++)
            {
                var (name, plan) = MatrixSetup.Steps[i];
                var result = done ? StripName(_results[i], name) : null;
                card.Add(StepRow(i + 1, name, result ?? plan, result, i == 0 && !done));
            }

            var foot = Element("gs-card__foot");
            foot.Add(Text("The values from before the first Setup are kept. Revert puts them back and turns the bridge off.", "gs-card__note"));
            var revert = LinkButton("Revert", MatrixSetup.Revert);
            revert.SetEnabled(MatrixActivation.IsEnabled || MatrixEditorSettings.HasSnapshot);
            foot.Add(revert);
            foot.Add(LinkButton(done || MatrixActivation.IsEnabled ? "Run Setup again" : "Run Setup", RunSetup, primary: true, big: true));
            card.Add(foot);

            section.Add(card);
            return section;
        }

        VisualElement StepRow(int number, string name, string detail, string? result, bool showSettings)
        {
            var row = Element("gs-step");
            var badge = Text(number.ToString(), "gs-step__badge");
            if (result != null)
                badge.AddToClassList(result.Contains("FAILED") ? "gs-step__badge--bad" : "gs-step__badge--ok");
            row.Add(badge);

            var body = Element("gs-step__body");
            body.Add(Text(name, "gs-step__name"));
            body.Add(Text(detail, "gs-step__detail"));
            if (showSettings)
                foreach (var label in MatrixSetup.EditorSettingLabels)
                    body.Add(Text(label, "gs-step__item"));
            row.Add(body);
            return row;
        }

        static string StripName(string result, string name)
        {
            var text = result.StartsWith(name + ":", StringComparison.Ordinal) ? result.Substring(name.Length + 1) : result;
            return string.Join("\n", text.Trim().Split('\n').Select(line => line.Trim().TrimStart('-', ' ')));
        }

        void RunSetup()
        {
            _results = MatrixSetup.Execute();
            CreateGUI();
        }

        #endregion

        #region Docs

        VisualElement BuildDocsSection()
        {
            var section = Section("2", "Read the docs",
                "The full Matrix Bridge manual ships inside the package and opens in your browser, no internet needed.");
            section.AddToClassList("gs-section--docs");

            var hero = Element("gs-card", "gs-hero");
            hero.AddManipulator(new Clickable(() => OpenDoc("index.html")));
            var heroText = Element("gs-hero__text");
            heroText.Add(Text("Matrix Bridge Manual", "gs-hero__title"));
            heroText.Add(Text("Start here: what the bridge is, how a session goes, and where every page lives.", "gs-hero__sub"));
            hero.Add(heroText);
            hero.Add(LinkButton("Open the manual", () => OpenDoc("index.html"), primary: true, big: true));
            section.Add(hero);

            foreach (var (group, pages) in DocGroups)
            {
                section.Add(Text(group.ToUpperInvariant(), "gs-group"));
                var grid = Element("gs-grid");
                foreach (var (file, title, text) in pages)
                {
                    var card = Element("gs-card", "gs-doc");
                    card.AddManipulator(new Clickable(() => OpenDoc(file)));
                    card.tooltip = file;
                    card.Add(Text(title, "gs-doc__title"));
                    card.Add(Text(text, "gs-doc__text"));
                    card.Add(Text("Read  >", "gs-doc__more"));
                    grid.Add(card);
                }
                section.Add(grid);
            }

            var foot = Element("gs-docs-foot");
            foot.Add(LinkButton("Show docs folder", () => EditorUtility.RevealInFinder(DocPath("index.html"))));
            section.Add(foot);
            return section;
        }

        static PackageInfo? Package => PackageInfo.FindForAssembly(typeof(GettingStartedWindow).Assembly);

        static string DocPath(string file)
            => Path.Combine(Package?.resolvedPath ?? Path.GetFullPath("Packages/com.feeder.mcp"), "Documentation~", file);

        static void OpenDoc(string file)
        {
            var path = DocPath(file);
            if (!File.Exists(path))
            {
                Debug.LogWarning($"[{Title}] Doc page not found: {path}");
                return;
            }
            Application.OpenURL("file:///" + path.Replace('\\', '/'));
        }

        #endregion

        #region Next steps

        VisualElement BuildNextSection()
        {
            var section = Section("3", "Next steps", "Once the bridge is on, these are the places you will come back to.");
            var grid = Element("gs-grid");
            var on = MatrixActivation.IsEnabled;
            grid.Add(ActionCard("Bridge window", "Live status, connected agents, the editor lock and play tests.", "Open", () => MatrixBridgeWindow.ShowWindow(), on));
            grid.Add(ActionCard("Skills window", "Turn tools on or off and watch the token cost per request.", "Open", () => MatrixSkillsWindow.ShowWindow(), on));
            grid.Add(ActionCard("Setup checklist", $"Ask your agent to run {MatrixSetup.NextStep}: faster play mode and a Defender exclusion.", "Copy command",
                () => EditorGUIUtility.systemCopyBuffer = MatrixSetup.NextStep, true));
            section.Add(grid);
            return section;
        }

        VisualElement ActionCard(string title, string text, string action, Action onClick, bool enabled)
        {
            var card = Element("gs-card", "gs-doc", "gs-action");
            card.Add(Text(title, "gs-doc__title"));
            card.Add(Text(enabled ? text : "Run Setup first.", "gs-doc__text"));
            var button = LinkButton(action, onClick);
            button.SetEnabled(enabled);
            card.Add(button);
            return card;
        }

        #endregion

        #region Elements

        static VisualElement Section(string number, string title, string description)
        {
            var section = Element("gs-section");
            var head = Element("gs-section__head");
            head.Add(Text(number, "gs-section__num"));
            var titles = Element("gs-section__titles");
            titles.Add(Text(title, "gs-section__title"));
            titles.Add(Text(description, "gs-section__desc"));
            head.Add(titles);
            section.Add(head);
            return section;
        }

        static Button LinkButton(string text, Action onClick, bool primary = false, bool big = false)
        {
            var button = new Button(onClick) { text = text };
            button.AddToClassList("gs-btn");
            if (primary)
                button.AddToClassList("gs-btn--primary");
            if (big)
                button.AddToClassList("gs-btn--big");
            return button;
        }

        static VisualElement Element(params string[] classes)
        {
            var element = new VisualElement();
            foreach (var name in classes)
                element.AddToClassList(name);
            return element;
        }

        static Label Text(string text, string className)
        {
            var label = new Label(text);
            label.AddToClassList(className);
            return label;
        }

        #endregion
    }
}

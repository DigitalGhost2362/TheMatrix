#nullable enable
using System.Globalization;
using UnityEditor;
using UnityEngine;

namespace Feeder.MCP.Editor.Vfx
{
    // the play button + scrubber the inspector uses for animation previews
    sealed class VfxTimeline
    {
        public const float Height = 21f;
        const float PlayButtonWidth = 33f;
        const float StepTime = 0.01f;
        const int FramesPerSecond = 60;
        static readonly int ScrubberHash = "ScrubberIDHash".GetHashCode();

        static GUIContent? playIcon;
        static GUIContent? pauseIcon;
        static GUIStyle? playButton;
        static GUIStyle? scrubber;
        static GUIStyle? playhead;

        public float CurrentTime;
        public float StartTime;
        public float StopTime = 1f;
        public bool Loop = true;
        public float Speed = 1f;

        float deltaTime;
        bool deltaSet;
        bool nextSet;
        bool playing;
        bool resetOnPlay;
        bool wrapForward;
        double lastFrame;
        float mouseDrag;

        public float DeltaTime
        {
            get => deltaTime;
            set
            {
                deltaTime = value;
                deltaSet = true;
            }
        }

        public bool Playing
        {
            get => playing;
            set
            {
                if (value && !playing)
                {
                    lastFrame = EditorApplication.timeSinceStartup;
                    if (resetOnPlay)
                    {
                        NextTime = StartTime;
                        resetOnPlay = false;
                    }
                }
                playing = value;
            }
        }

        public float Normalized
        {
            get => StopTime != StartTime ? (CurrentTime - StartTime) / (StopTime - StartTime) : 0f;
            set => CurrentTime = StartTime * (1f - value) + StopTime * value;
        }

        public string Label
        {
            get
            {
                var time = CurrentTime - StartTime;
                var frame = Mathf.FloorToInt(time * FramesPerSecond);
                return string.Format(CultureInfo.InvariantCulture.NumberFormat, "{0,2}:{1:00} ({2:000.0%})", (int)time, (frame % FramesPerSecond + FramesPerSecond) % FramesPerSecond, Normalized);
            }
        }

        float NextTime
        {
            set
            {
                DeltaTime = value - CurrentTime;
                nextSet = true;
            }
        }

        public void Update()
        {
            if (!deltaSet)
            {
                if (playing)
                {
                    var now = EditorApplication.timeSinceStartup;
                    DeltaTime = (float)(now - lastFrame) * Speed;
                    lastFrame = now;
                }
                else DeltaTime = 0f;
            }
            CurrentTime += deltaTime;
            if (Loop && playing && !nextSet) Normalized = Mathf.Repeat(Normalized, 1f);
            else
            {
                if (Normalized > 1f)
                {
                    Playing = false;
                    resetOnPlay = true;
                }
                Normalized = Mathf.Clamp01(Normalized);
            }
            deltaSet = false;
            nextSet = false;
        }

        public void Draw(Rect rect)
        {
            playIcon ??= EditorGUIUtility.IconContent("PlayButton");
            pauseIcon ??= EditorGUIUtility.IconContent("PauseButton");
            playButton ??= "TimeScrubberButton";
            scrubber ??= "TimeScrubber";
            playhead ??= "AnimationPlayHead";
            var e = Event.current;
            var id = GUIUtility.GetControlID(ScrubberHash, FocusType.Keyboard);
            var bar = rect;
            bar.height = Height;
            var track = bar;
            track.xMin += PlayButtonWidth;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown:
                    if (rect.Contains(e.mousePosition)) GUIUtility.keyboardControl = id;
                    if (track.Contains(e.mousePosition))
                    {
                        EditorGUIUtility.SetWantsMouseJumping(1);
                        GUIUtility.hotControl = id;
                        mouseDrag = e.mousePosition.x - track.xMin;
                        NextTime = mouseDrag * (StopTime - StartTime) / track.width + StartTime;
                        wrapForward = false;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl != id) break;
                    mouseDrag += e.delta.x * Speed;
                    if (Loop && ((mouseDrag < 0f && wrapForward) || mouseDrag > track.width))
                    {
                        if (mouseDrag > track.width) CurrentTime -= StopTime - StartTime;
                        else if (mouseDrag < 0f) CurrentTime += StopTime - StartTime;
                        wrapForward = true;
                        mouseDrag = Mathf.Repeat(mouseDrag, track.width);
                    }
                    NextTime = Mathf.Clamp(mouseDrag, 0f, track.width) * (StopTime - StartTime) / track.width + StartTime;
                    e.Use();
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl != id) break;
                    EditorGUIUtility.SetWantsMouseJumping(0);
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
                case EventType.KeyDown:
                    if (GUIUtility.keyboardControl != id) break;
                    if (e.keyCode == KeyCode.LeftArrow)
                    {
                        if (CurrentTime - StartTime > StepTime) DeltaTime = -StepTime;
                        e.Use();
                    }
                    if (e.keyCode == KeyCode.RightArrow)
                    {
                        if (StopTime - CurrentTime > StepTime) DeltaTime = StepTime;
                        e.Use();
                    }
                    break;
            }
            GUI.Box(bar, GUIContent.none, scrubber);
            Playing = GUI.Toggle(bar, playing, playing ? pauseIcon : playIcon, playButton);
            if (e.type != EventType.Repaint) return;
            var x = Mathf.Lerp(track.x, track.xMax, Normalized);
            var color = playhead.normal.textColor;
            color.a *= GUIUtility.keyboardControl == id ? 1f : 0.5f;
            EditorGUI.DrawRect(Rect.MinMaxRect(x - 1f, track.yMin, x + 1f, track.yMax), color);
        }
    }
}

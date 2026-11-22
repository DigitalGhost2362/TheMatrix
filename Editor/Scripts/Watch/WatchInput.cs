#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
#if MATRIX_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;
#endif

namespace Feeder.MCP.Editor.Watch
{
    // polls whichever input backend the project runs and turns it into key edges, one pointer and a move vector
    internal sealed class WatchInput
    {
        public const string MarkerKey = "F9";

        static readonly KeyCode[] _legacyKeys = ((KeyCode[])Enum.GetValues(typeof(KeyCode)))
            .Where(key => key != KeyCode.None && key != KeyCode.Mouse0 && key < KeyCode.JoystickButton0)
            .Distinct()
            .ToArray();

        static readonly string[] _moveUp = { "W", "UpArrow" };
        static readonly string[] _moveDown = { "S", "DownArrow" };
        static readonly string[] _moveLeft = { "A", "LeftArrow" };
        static readonly string[] _moveRight = { "D", "RightArrow" };

        HashSet<string> _down = new HashSet<string>();
        HashSet<string> _prevDown = new HashSet<string>();
        readonly List<string> _pressed = new List<string>();
        readonly List<string> _released = new List<string>();
        readonly HashSet<string> _taps = new HashSet<string>();
        bool _legacy;
        bool _pointerWasDown;
        Vector2 _stick;
#if MATRIX_INPUT_SYSTEM
        bool _inputSystem;
        InputEventTrace? _trace;
#endif

        public string Backend { get; private set; } = "none";
        public IReadOnlyList<string> Pressed => _pressed;
        public IReadOnlyList<string> Released => _released;
        public bool PointerDown { get; private set; }
        public bool PointerUp { get; private set; }
        public bool PointerHeld { get; private set; }
        public Vector2 PointerPosition { get; private set; }
        public Vector2 Move { get; private set; }

        public void Begin()
        {
            var handler = ActiveInputHandler();
#if MATRIX_INPUT_SYSTEM
            _inputSystem = handler != 0;
            if (_inputSystem)
            {
                _trace = new InputEventTrace(1024 * 1024, growBuffer: true, maxBufferSizeInBytes: 64L * 1024 * 1024);
                _trace.Enable();
            }
            _legacy = !_inputSystem && LegacyWorks();
            Backend = _inputSystem ? "InputSystem" : _legacy ? "Legacy" : "none";
#else
            _legacy = handler != 1 && LegacyWorks();
            Backend = _legacy ? "Legacy" : "none (Input System package missing)";
#endif
        }

        // returns the trace file name when one was written
        public string End(string folder)
        {
#if MATRIX_INPUT_SYSTEM
            if (_trace == null)
                return string.Empty;
            try
            {
                _trace.Disable();
                if (_trace.eventCount == 0)
                    return string.Empty;
                var name = "input.inputtrace";
                for (var i = 2; System.IO.File.Exists(System.IO.Path.Combine(folder, name)); i++)
                    name = $"input-{i}.inputtrace";
                _trace.WriteTo(System.IO.Path.Combine(folder, name));
                return name;
            }
            finally
            {
                _trace.Dispose();
                _trace = null;
            }
#else
            return string.Empty;
#endif
        }

        public void Poll()
        {
            (_prevDown, _down) = (_down, _prevDown);
            _down.Clear();
            _pressed.Clear();
            _released.Clear();
            _stick = Vector2.zero;
            _taps.Clear();
            var pointerDown = false;
            var pointerTap = false;
            var pointer = PointerPosition;

#if MATRIX_INPUT_SYSTEM
            if (_inputSystem)
                PollInputSystem(ref pointerDown, ref pointerTap, ref pointer);
#endif
            if (_legacy)
                PollLegacy(ref pointerDown, ref pointerTap, ref pointer);

            foreach (var name in _down)
                if (!_prevDown.Contains(name))
                    _pressed.Add(name);
            foreach (var name in _prevDown)
                if (!_down.Contains(name))
                    _released.Add(name);
            // pressed and released inside one frame: report both edges
            foreach (var name in _taps)
            {
                if (_down.Contains(name) || _prevDown.Contains(name))
                    continue;
                _pressed.Add(name);
                _released.Add(name);
            }

            var tap = pointerTap && !pointerDown && !_pointerWasDown;
            PointerDown = (pointerDown && !_pointerWasDown) || tap;
            PointerUp = (!pointerDown && _pointerWasDown) || tap;
            PointerHeld = pointerDown;
            PointerPosition = pointer;
            _pointerWasDown = pointerDown;

            var keys = new Vector2(Axis(_moveRight) - Axis(_moveLeft), Axis(_moveUp) - Axis(_moveDown));
            Move = _stick.sqrMagnitude > keys.sqrMagnitude ? _stick : Vector2.ClampMagnitude(keys, 1f);
        }

        public static bool IsMoveKey(string name) =>
            _moveUp.Contains(name) || _moveDown.Contains(name) || _moveLeft.Contains(name) || _moveRight.Contains(name);

        float Axis(string[] names)
        {
            foreach (var name in names)
                if (_down.Contains(name))
                    return 1f;
            return 0f;
        }

        void PollLegacy(ref bool pointerDown, ref bool pointerTap, ref Vector2 pointer)
        {
            var anyDown = Input.anyKeyDown;
            foreach (var key in _legacyKeys)
            {
                if (Input.GetKey(key))
                    _down.Add(key.ToString());
                else if (anyDown && Input.GetKeyDown(key))
                    _taps.Add(key.ToString());
            }
            pointerTap |= Input.GetMouseButtonDown(0);

            if (Input.touchCount > 0)
            {
                pointerDown = true;
                pointer = Input.GetTouch(0).position;
            }
            else
            {
                pointerDown |= Input.GetMouseButton(0);
                pointer = Input.mousePosition;
            }
        }

#if MATRIX_INPUT_SYSTEM
        void PollInputSystem(ref bool pointerDown, ref bool pointerTap, ref Vector2 pointer)
        {
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                foreach (var key in keyboard.allKeys)
                {
                    if (key == null)
                        continue;
                    if (key.isPressed)
                        _down.Add(key.keyCode.ToString());
                    else if (key.wasPressedThisFrame)
                        _taps.Add(key.keyCode.ToString());
                }
            }

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (mouse.rightButton.isPressed) _down.Add("Mouse1");
                if (mouse.middleButton.isPressed) _down.Add("Mouse2");
            }

            var gamepad = Gamepad.current;
            if (gamepad != null)
            {
                Pad(gamepad.buttonSouth, "Pad.South");
                Pad(gamepad.buttonEast, "Pad.East");
                Pad(gamepad.buttonWest, "Pad.West");
                Pad(gamepad.buttonNorth, "Pad.North");
                Pad(gamepad.leftShoulder, "Pad.LB");
                Pad(gamepad.rightShoulder, "Pad.RB");
                Pad(gamepad.leftTrigger, "Pad.LT");
                Pad(gamepad.rightTrigger, "Pad.RT");
                Pad(gamepad.startButton, "Pad.Start");
                Pad(gamepad.selectButton, "Pad.Select");
                Pad(gamepad.dpad.up, "Pad.Up");
                Pad(gamepad.dpad.down, "Pad.Down");
                Pad(gamepad.dpad.left, "Pad.Left");
                Pad(gamepad.dpad.right, "Pad.Right");
                var stick = gamepad.leftStick.ReadValue();
                if (stick.sqrMagnitude > 0.04f)
                    _stick = stick;
            }

            var touch = Touchscreen.current;
            if (touch != null && (touch.primaryTouch.press.isPressed || touch.primaryTouch.press.wasPressedThisFrame))
            {
                pointerDown = touch.primaryTouch.press.isPressed;
                pointerTap |= touch.primaryTouch.press.wasPressedThisFrame;
                pointer = touch.primaryTouch.position.ReadValue();
            }
            else if (mouse != null)
            {
                pointerDown |= mouse.leftButton.isPressed;
                pointerTap |= mouse.leftButton.wasPressedThisFrame;
                pointer = mouse.position.ReadValue();
            }
        }

        void Pad(ButtonControl? button, string name)
        {
            if (button == null)
                return;
            if (button.isPressed)
                _down.Add(name);
            else if (button.wasPressedThisFrame)
                _taps.Add(name);
        }
#endif

        static bool LegacyWorks()
        {
            try
            {
                Input.GetKey(KeyCode.A);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        // 0 = Input Manager, 1 = Input System, 2 = both
        static int ActiveInputHandler()
        {
            var settings = Unsupported.GetSerializedAssetInterfaceSingleton("PlayerSettings");
            if (settings == null)
                return 0;
            using var serialized = new SerializedObject(settings);
            return serialized.FindProperty("activeInputHandler")?.intValue ?? 0;
        }
    }
}

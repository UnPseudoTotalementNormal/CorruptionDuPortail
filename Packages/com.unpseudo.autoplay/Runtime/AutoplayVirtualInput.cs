#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// Virtual Input System mouse + keyboard for the "real input" autoplay mode: the bot moves a pointer to the real
    /// element and clicks, and the game's real EventSystem / raycasters / UI Toolkit decide what is hit.
    /// <list type="bullet">
    /// <item>the project's input settings asset is never touched: a runtime clone with
    /// <see cref="InputSettings.BackgroundBehavior.IgnoreFocus"/> is installed (players run without focus);</item>
    /// <item>every real mouse / keyboard is disabled while installed, including devices added later, so the user's
    /// own mouse never reaches the game;</item>
    /// <item><see cref="Dispose"/> restores the settings and re-enables the real devices.</item>
    /// </list>
    /// Coroutine API (the runner's convention): <c>yield return input.MoveTo(...)</c>.
    /// </summary>
    public sealed class AutoplayVirtualInput : IDisposable
    {
        /// <summary>Usage tag on the virtual devices (tells them apart from real ones in logs and filters).</summary>
        public const string VirtualUsage = "AutoplayVirtual";

        public Mouse Mouse { get; private set; }
        public Keyboard Keyboard { get; private set; }

        /// <summary>Last position pushed to the virtual mouse, in screen pixels (origin bottom-left).</summary>
        public Vector2 Position { get; private set; }

        /// <summary>Input events from any non-virtual device that reached the input system since install (should stay 0).</summary>
        public int RealDeviceEvents { get; private set; }

        private InputSettings previousSettings;
        private InputSettings clonedSettings;
        private readonly List<InputDevice> disabledDevices = new();
        private bool installed;

        private bool keepRealDevices;
        private bool addingVirtual;

        /// <param name="_keepRealDevices">Diagnostic control only: leave the real mouse / keyboard enabled, so
        /// <see cref="RealDeviceEvents"/> shows whether the user's input reaches the unfocused player at all.</param>
        public static AutoplayVirtualInput Install(bool _keepRealDevices = false)
        {
            var _input = new AutoplayVirtualInput { keepRealDevices = _keepRealDevices };
            _input.DoInstall();
            return _input;
        }

        private void DoInstall()
        {
            // From the first change on, Dispose undoes whatever was done, even if a later step throws.
            installed = true;
            Application.quitting += Dispose;
            try
            {
                InstallSteps();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private void InstallSteps()
        {
            previousSettings = InputSystem.settings;
            clonedSettings = UnityEngine.Object.Instantiate(previousSettings);
            clonedSettings.name = "AutoplayInputSettings";
            clonedSettings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings = clonedSettings;

            foreach (InputDevice _device in InputSystem.devices.ToArray())
            {
                DisableIfReal(_device);
            }
            InputSystem.onDeviceChange += OnDeviceChange;
            InputSystem.onEvent += OnEvent;

            // onDeviceChange fires inside AddDevice, before Mouse / Keyboard are assigned: without this flag the
            // virtual devices would be taken for real ones and disabled.
            addingVirtual = true;
            try
            {
                Mouse = InputSystem.AddDevice<Mouse>("AutoplayMouse");
                Keyboard = InputSystem.AddDevice<Keyboard>("AutoplayKeyboard");
            }
            finally
            {
                addingVirtual = false;
            }
            InputSystem.SetDeviceUsage(Mouse, VirtualUsage);
            InputSystem.SetDeviceUsage(Keyboard, VirtualUsage);
            Mouse.MakeCurrent();
            Keyboard.MakeCurrent();

            Position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            QueueMouse(Position, Vector2.zero, false);
        }

        public void Dispose()
        {
            if (!installed)
            {
                return;
            }

            installed = false;
            Application.quitting -= Dispose;
            InputSystem.onDeviceChange -= OnDeviceChange;
            InputSystem.onEvent -= OnEvent;
            if (Mouse != null && Mouse.added) InputSystem.RemoveDevice(Mouse);
            if (Keyboard != null && Keyboard.added) InputSystem.RemoveDevice(Keyboard);
            foreach (InputDevice _device in disabledDevices)
            {
                if (_device.added) InputSystem.EnableDevice(_device);
            }
            disabledDevices.Clear();
            if (InputSystem.settings == clonedSettings && previousSettings != null)
            {
                InputSystem.settings = previousSettings;
            }
            if (clonedSettings != null) UnityEngine.Object.Destroy(clonedSettings);
            heldKeys.Clear();
            leftDown = false;
        }

        // Events only go to live virtual devices (a click still in flight when the run is torn down is dropped).
        private bool CanQueue(InputDevice _device) => installed && _device != null && _device.added;

        private bool IsVirtual(InputDevice _device) => _device == Mouse || _device == Keyboard;

        // Pointer-ish devices and keyboards only: a gamepad or a touch screen is not what the user works with.
        private static bool IsRealPointerOrKeyboard(InputDevice _device) => _device is Mouse || _device is Keyboard;

        private void DisableIfReal(InputDevice _device)
        {
            if (keepRealDevices || addingVirtual || IsVirtual(_device) || !IsRealPointerOrKeyboard(_device) || !_device.enabled)
            {
                return;
            }

            InputSystem.DisableDevice(_device);
            disabledDevices.Add(_device);
        }

        private void OnDeviceChange(InputDevice _device, InputDeviceChange _change)
        {
            if (_change == InputDeviceChange.Added || _change == InputDeviceChange.Reconnected || _change == InputDeviceChange.Enabled)
            {
                DisableIfReal(_device);
            }
        }

        private void OnEvent(InputEventPtr _event, InputDevice _device)
        {
            if (_device != null && !IsVirtual(_device) && IsRealPointerOrKeyboard(_device))
            {
                RealDeviceEvents++;
            }
        }

        private void QueueMouse(Vector2 _position, Vector2 _delta, bool _leftDown)
        {
            if (!CanQueue(Mouse))
            {
                return;
            }
            var _state = new MouseState { position = _position, delta = _delta }.WithButton(MouseButton.Left, _leftDown);
            InputSystem.QueueStateEvent(Mouse, _state);
            Position = _position;
        }

        private bool leftDown;

        /// <summary>Glides the pointer to <paramref name="_screen"/> (pixels, origin bottom-left) over
        /// <paramref name="_seconds"/> real seconds, one event per frame, so hover / enter / exit fire on the way.</summary>
        public IEnumerator MoveTo(Vector2 _screen, float _seconds = 0.25f)
        {
            Vector2 _from = Position;
            float _start = Time.realtimeSinceStartup;
            while (true)
            {
                float _t = _seconds <= 0f ? 1f : Mathf.Clamp01((Time.realtimeSinceStartup - _start) / _seconds);
                Vector2 _next = Vector2.Lerp(_from, _screen, Mathf.SmoothStep(0f, 1f, _t));
                QueueMouse(_next, _next - Position, leftDown);
                yield return null;
                if (_t >= 1f)
                {
                    break;
                }
            }
            // One still frame on the target so the UI module settles its hover before a click.
            QueueMouse(_screen, Vector2.zero, leftDown);
            yield return null;
        }

        /// <summary>One frame of relative mouse motion (what turns a first-person camera while the cursor is locked).
        /// The pointer position does not change.</summary>
        public IEnumerator Look(Vector2 _delta)
        {
            QueueMouse(Position, _delta, leftDown);
            yield return null;
        }

        /// <summary>One frame of mouse wheel at the current position (y &gt; 0 scrolls up). Units are the Input System's
        /// scroll value; how far one unit scrolls is the receiving UI's business (measure it, do not assume a notch).</summary>
        public IEnumerator Scroll(Vector2 _wheel)
        {
            if (CanQueue(Mouse))
            {
                var _state = new MouseState { position = Position, scroll = _wheel }.WithButton(MouseButton.Left, leftDown);
                InputSystem.QueueStateEvent(Mouse, _state);
            }
            yield return null;
            QueueMouse(Position, Vector2.zero, leftDown);
            yield return null;
        }

                /// <summary>Left button down for one frame, then up, at the current position.</summary>
        public IEnumerator Click()
        {
            leftDown = true;
            QueueMouse(Position, Vector2.zero, true);
            yield return null;
            leftDown = false;
            QueueMouse(Position, Vector2.zero, false);
            yield return null;
        }

        private readonly HashSet<Key> heldKeys = new();

        private void QueueKeyboard()
        {
            if (!CanQueue(Keyboard))
            {
                return;
            }
            var _keys = new Key[heldKeys.Count];
            heldKeys.CopyTo(_keys);
            InputSystem.QueueStateEvent(Keyboard, new KeyboardState(_keys));
        }

        /// <summary>Types <paramref name="_text"/> as text input of the virtual keyboard (one character per frame), what a
        /// focused text field receives from a real keyboard.</summary>
        public IEnumerator TypeText(string _text)
        {
            foreach (char _character in _text ?? string.Empty)
            {
                if (CanQueue(Keyboard))
                {
                    InputSystem.QueueTextEvent(Keyboard, _character);
                }
                yield return null;
            }
        }

        /// <summary>Press then release <paramref name="_key"/> one frame later (other held keys stay down).</summary>
        public IEnumerator PressKey(Key _key)
        {
            yield return KeyDown(_key);
            yield return KeyUp(_key);
        }

        /// <summary>Holds <paramref name="_key"/> down until <see cref="KeyUp"/> (e.g. a hold-to-open wheel).</summary>
        public IEnumerator KeyDown(Key _key)
        {
            heldKeys.Add(_key);
            QueueKeyboard();
            yield return null;
        }

        public IEnumerator KeyUp(Key _key)
        {
            ReleaseKey(_key);
            yield return null;
        }

        /// <summary>Releases <paramref name="_key"/> now (no frame wait): for finally blocks, so a cancelled hold never
        /// leaves a key down.</summary>
        public void ReleaseKey(Key _key)
        {
            if (heldKeys.Remove(_key))
            {
                QueueKeyboard();
            }
        }

        /// <summary>Releases every held key and the mouse button now (end of a run, cancelled action).</summary>
        public void ReleaseAll()
        {
            bool _keys = heldKeys.Count > 0;
            heldKeys.Clear();
            if (_keys)
            {
                QueueKeyboard();
            }
            if (leftDown)
            {
                leftDown = false;
                QueueMouse(Position, Vector2.zero, false);
            }
        }

        // Disabled devices' events are dropped before InputSystem.onEvent: realEvents means something only with
        // keepRealDevices (the -autoplay-real-input-control diagnostic); otherwise it stays 0 by construction.
        public string Describe() => string.Format(CultureInfo.InvariantCulture,
            "background={0} disabled={1}[{6}] keepReal={2} realEvents={3} focused={4} pointer={5}", InputSystem.settings.backgroundBehavior,
            disabledDevices.Count, keepRealDevices, RealDeviceEvents, Application.isFocused, Pointer.current?.name ?? "none",
            string.Join(",", disabledDevices.ConvertAll(_d => _d.name)));
    }
}
#endif

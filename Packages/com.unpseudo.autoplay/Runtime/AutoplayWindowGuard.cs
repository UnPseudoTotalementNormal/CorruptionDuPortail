#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unpseudo.Autoplay
{
    /// <summary>
    /// Keeps an autoplay player out of the user's way so they can keep working while games play:
    /// <list type="bullet">
    /// <item>runs and renders without focus (<see cref="Application.runInBackground"/>);</item>
    /// <item>if Windows / Unity activates the window anyway (boot, scene loads), hands focus back to the window that had it at launch
    /// (<c>-autoplay-restore-hwnd</c>, passed by tools/autoplay/launch-background.ps1) and sends the player window to
    /// the bottom of the z-order — never minimized, since a minimized player stops rendering (no screenshots);</item>
    /// <item>never locks or hides the mouse cursor;</item>
    /// <item>mutes the game (adapter callback) unless <c>-autoplay-sound</c> is passed.</item>
    /// </list>
    /// Added by <see cref="AutoplayPlayerBootstrap"/> only when the player runs with <c>-autoplay</c>. Before that (splash
    /// screen, first scene load: no script runs yet), <see cref="EarlyGuard"/> does the same from a native thread.
    /// </summary>
    public sealed class AutoplayWindowGuard : MonoBehaviour
    {
        private const float WatchSeconds = 20f;
        private const float WatchInterval = 0.25f;

        private IntPtr restoreTo = IntPtr.Zero;
        private Action muteAudio;
        private float nextMute;

        /// <param name="_restoreHwnd">Window to hand the focus back to (0 = none).</param>
        /// <param name="_muteAudio">Idempotent mute call, repeated every second (audio middleware may start late); null keeps sound.</param>
        public void Configure(long _restoreHwnd, Action _muteAudio)
        {
            restoreTo = new IntPtr(_restoreHwnd);
            muteAudio = _muteAudio;
        }

        private void Awake() => Application.runInBackground = true;

        private void OnEnable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;

        // Unity activates its window at boot and may do so again around scene loads. Only those windows are watched:
        // a deliberate click by the user later (to look at the game) is respected.
        private void Start() => StartCoroutine(WatchFor(WatchSeconds));

        private void OnSceneLoaded(UnityEngine.SceneManagement.Scene _scene, UnityEngine.SceneManagement.LoadSceneMode _mode)
            => StartCoroutine(WatchFor(3f));

        private IEnumerator WatchFor(float _seconds)
        {
            float _until = Time.realtimeSinceStartup + _seconds;
            while (Time.realtimeSinceStartup < _until)
            {
                StepOutOfTheWay();
                yield return new WaitForSecondsRealtime(WatchInterval);
            }
        }

        private void LateUpdate()
        {
            if (Cursor.lockState != CursorLockMode.None)
            {
                Cursor.lockState = CursorLockMode.None;
            }
            if (!Cursor.visible)
            {
                Cursor.visible = true;
            }

            if (muteAudio != null && Time.realtimeSinceStartup >= nextMute)
            {
                nextMute = Time.realtimeSinceStartup + 1f;
                try
                {
                    muteAudio();
                }
                catch (Exception _exception)
                {
                    Debug.LogWarning($"{AutoplayJournal.LogTag} mute failed: {_exception.Message}");
                }
            }
        }

        private void StepOutOfTheWay() => StepOutOfTheWay(restoreTo);

        private static void StepOutOfTheWay(IntPtr _restoreTo)
        {
#if UNITY_STANDALONE_WIN
            IntPtr _own = FindOwnWindow();
            if (_own == IntPtr.Zero)
            {
                return;
            }

            if (GetForegroundWindow() == _own && _restoreTo != IntPtr.Zero && IsWindow(_restoreTo))
            {
                // Allowed: the foreground process may hand the foreground to another window.
                SetForegroundWindow(_restoreTo);
            }

            SetWindowPos(_own, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
#endif
        }

        /// <summary>
        /// The window exists, and Unity brings it to the front, before any scene script runs (splash screen, first
        /// scene load). As early as the engine allows, an autoplay player skips the splash and starts a background
        /// thread that keeps handing the focus back and the window to the bottom until the guard component has long
        /// taken over (plain Win32 calls, no Unity API on that thread).
        /// </summary>
        private static class EarlyGuard
        {
            private const int WatchMilliseconds = 30000;
            private const int IntervalMilliseconds = 100;

            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
            private static void Begin()
            {
                string[] _args = Environment.GetCommandLineArgs();
                if (!AutoplayCommandLine.IsRequested(_args))
                {
                    return;
                }

                UnityEngine.Rendering.SplashScreen.Stop(UnityEngine.Rendering.SplashScreen.StopBehavior.StopImmediate);

                int _at = Array.IndexOf(_args, "-autoplay-restore-hwnd");
                long _restore = 0;
                if (_at >= 0 && _at + 1 < _args.Length)
                {
                    long.TryParse(_args[_at + 1], out _restore);
                }

                var _thread = new Thread(() => Watch(new IntPtr(_restore))) { IsBackground = true, Name = "AutoplayEarlyGuard" };
                _thread.Start();
            }

            private static void Watch(IntPtr _restoreTo)
            {
                var _clock = Stopwatch.StartNew();
                while (_clock.ElapsedMilliseconds < WatchMilliseconds)
                {
                    try
                    {
                        StepOutOfTheWay(_restoreTo);
                    }
                    catch (Exception)
                    {
                        // best effort: a failed Win32 call must never take the player down
                    }
                    Thread.Sleep(IntervalMilliseconds);
                }
            }
        }

#if UNITY_STANDALONE_WIN
        private static readonly IntPtr HwndBottom = new IntPtr(1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;

        private static IntPtr ownWindow = IntPtr.Zero;

        private static IntPtr FindOwnWindow()
        {
            if (ownWindow != IntPtr.Zero && IsWindow(ownWindow))
            {
                return ownWindow;
            }

            uint _pid = (uint)Process.GetCurrentProcess().Id;
            IntPtr _found = IntPtr.Zero;
            EnumWindows((_hwnd, _) =>
            {
                GetWindowThreadProcessId(_hwnd, out uint _windowPid);
                if (_windowPid == _pid && IsWindowVisible(_hwnd))
                {
                    _found = _hwnd;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            ownWindow = _found;
            return _found;
        }

        private delegate bool EnumWindowsProc(IntPtr _hwnd, IntPtr _param);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc _callback, IntPtr _param);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr _hwnd, out uint _pid);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr _hwnd);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr _hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr _hwnd);
        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr _hwnd, IntPtr _after, int _x, int _y, int _cx, int _cy, uint _flags);
#endif
    }
}
#endif

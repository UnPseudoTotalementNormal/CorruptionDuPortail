#if DEVELOPMENT_BUILD && !UNITY_EDITOR
using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Autoplay
{
    /// <summary>
    /// Keeps an autoplay player out of the user's way so they can keep working while games play:
    /// <list type="bullet">
    /// <item>runs and renders without focus (<see cref="Application.runInBackground"/>);</item>
    /// <item>if Windows / Unity activates the window anyway (boot, scene loads), hands focus back to the window that had it at launch
    /// (<c>-autoplay-restore-hwnd</c>, passed by tools/autoplay/launch-background.ps1) and sends the player window to
    /// the bottom of the z-order — never minimized, since a minimized player stops rendering (no screenshots);</item>
    /// <item>never locks or hides the mouse cursor;</item>
    /// <item>mutes FMOD unless <c>-autoplay-sound</c> is passed.</item>
    /// </list>
    /// Added by <see cref="AutoplayBootstrap"/> only when the player runs with <c>-autoplay</c>.
    /// </summary>
    public sealed class AutoplayWindowGuard : MonoBehaviour
    {
        private const float WatchSeconds = 20f;
        private const float WatchInterval = 0.25f;

        private IntPtr restoreTo = IntPtr.Zero;
        private bool muteSound = true;
        private bool muted;

        public void Configure(long _restoreHwnd, bool _muteSound)
        {
            restoreTo = new IntPtr(_restoreHwnd);
            muteSound = _muteSound;
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

            if (muteSound && !muted && FMODUnity.RuntimeManager.IsInitialized)
            {
                FMODUnity.RuntimeManager.MuteAllEvents(true);
                muted = true;
            }
        }

        private void StepOutOfTheWay()
        {
#if UNITY_STANDALONE_WIN
            IntPtr _own = FindOwnWindow();
            if (_own == IntPtr.Zero)
            {
                return;
            }

            if (GetForegroundWindow() == _own && restoreTo != IntPtr.Zero && IsWindow(restoreTo))
            {
                // Allowed: the foreground process may hand the foreground to another window.
                SetForegroundWindow(restoreTo);
            }

            SetWindowPos(_own, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
#endif
        }

#if UNITY_STANDALONE_WIN
        private static readonly IntPtr HwndBottom = new IntPtr(1);
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoMove = 0x0002;
        private const uint SwpNoActivate = 0x0010;

        private IntPtr ownWindow = IntPtr.Zero;

        private IntPtr FindOwnWindow()
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

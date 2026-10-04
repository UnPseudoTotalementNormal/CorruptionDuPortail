<#
.SYNOPSIS
  Launch an autoplay player WITHOUT stealing focus, wait for it (bounded), always kill it at the end.

.DESCRIPTION
  - CreateProcess with SW_SHOWNOACTIVATE: the window appears without taking the foreground.
  - The currently focused window handle is passed to the player (-autoplay-restore-hwnd) so that, if Unity
    activates itself anyway, the player hands focus straight back and sends its window to the bottom
    (see AutoplayWindowGuard). The user keeps working while games play.
  - Exit code = the player's exit code (0 = GameEnding), 124 = timeout (player killed).

.EXAMPLE
  powershell -File tools/autoplay/launch-background.ps1 -Exe Builds/Autoplay/CorruptionDuPortail.exe -TimeoutSeconds 900 -PlayerArgs '-autoplay -autoplay-seed 4'
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$PlayerArgs = "",
    [int]$TimeoutSeconds = 900
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class NoActivateLauncher
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string lpReserved; public string lpDesktop; public string lpTitle;
        public int dwX; public int dwY; public int dwXSize; public int dwYSize;
        public int dwXCountChars; public int dwYCountChars; public int dwFillAttribute;
        public int dwFlags; public short wShowWindow; public short cbReserved2;
        public IntPtr lpReserved2; public IntPtr hStdInput; public IntPtr hStdOutput; public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess; public IntPtr hThread; public int dwProcessId; public int dwThreadId; }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcess(string app, string cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags,
        IntPtr env, string dir, ref STARTUPINFO si, out PROCESS_INFORMATION pi);

    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();

    const int STARTF_USESHOWWINDOW = 0x1;
    const short SW_SHOWNOACTIVATE = 4;

    public static int Start(string exe, string args, string dir)
    {
        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        si.dwFlags = STARTF_USESHOWWINDOW;
        si.wShowWindow = SW_SHOWNOACTIVATE;
        PROCESS_INFORMATION pi;
        if (!CreateProcess(null, "\"" + exe + "\" " + args, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, dir, ref si, out pi))
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return pi.dwProcessId;
    }
}
"@

$exePath = (Resolve-Path $Exe).Path
$foreground = [NoActivateLauncher]::GetForegroundWindow().ToInt64()
$fullArgs = "$PlayerArgs -autoplay-restore-hwnd $foreground"
$playerPid = [NoActivateLauncher]::Start($exePath, $fullArgs, (Split-Path $exePath))
Write-Output "player pid=$playerPid (focus handed back to hwnd $foreground)"

$proc = Get-Process -Id $playerPid -ErrorAction SilentlyContinue
if ($null -eq $proc) { Write-Output "player exited immediately"; exit 1 }
$null = $proc.Handle  # cache the handle now, otherwise ExitCode is empty once the process has exited
if ($proc.WaitForExit($TimeoutSeconds * 1000)) {
    Write-Output "player exit=$($proc.ExitCode)"
    exit $proc.ExitCode
}
Write-Output "timeout after $TimeoutSeconds s: killing player $playerPid"
Stop-Process -Id $playerPid -Force -ErrorAction SilentlyContinue
exit 124

<#
.SYNOPSIS
  Launch an autoplay player WITHOUT stealing focus, wait for it (bounded), always kill it at the end.

.DESCRIPTION
  - CreateProcess with SW_SHOWNOACTIVATE: the window appears without taking the foreground.
  - The currently focused window handle is passed to the player (-autoplay-restore-hwnd) so that, if Unity
    activates itself anyway, the player hands focus straight back and sends its window to the bottom
    (see AutoplayWindowGuard in this package). The user keeps working while games play.
  - -AlertsRoot <runs root>: the run folder the player creates there is watched; its watchdog alerts (journal
    watchdog.* / run.fail, see AutoplayWatchdog) are copied as they happen to <run>/alerts.log and
    <AlertsRoot>/alerts.log, with the folder to write watchdog-control.txt into.
  - Exit code = the player's exit code (0 = GameEnding), 124 = timeout (player killed).

.EXAMPLE
  powershell -File Tools~/launch-background.ps1 -Exe Builds/Autoplay/Game.exe -TimeoutSeconds 900 -PlayerArgs '-autoplay -autoplay-seed 4'
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [string]$PlayerArgs = "",
    [int]$TimeoutSeconds = 900,
    [string]$AlertsRoot = ""
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

# One autoplay run at a time on the machine, first come first served (AUTOPLAY_MAX_PARALLEL=N: N at once).
. (Join-Path $PSScriptRoot 'launcher-queue.ps1')
Wait-AutoplayLauncherTurn

$exePath = (Resolve-Path $Exe).Path
$foreground = [NoActivateLauncher]::GetForegroundWindow().ToInt64()
$fullArgs = "$PlayerArgs -autoplay-restore-hwnd $foreground"
$playerPid = [NoActivateLauncher]::Start($exePath, $fullArgs, (Split-Path $exePath))
Write-Output "player pid=$playerPid (focus handed back to hwnd $foreground)"

$proc = Get-Process -Id $playerPid -ErrorAction SilentlyContinue
if ($null -eq $proc) { Write-Output "player exited immediately"; exit 1 }
$null = $proc.Handle  # cache the handle now, otherwise ExitCode is empty once the process has exited

# Watchdog relay: the run folder this player creates under -AlertsRoot, read incrementally.
$startedAt = Get-Date
$runDir = $null
$offset = 0L
function Write-AlertLine([string]$path, [string]$line) {
    # Shared append with retries: a reader (tail -F, a monitor) may hold the file for a moment.
    for ($try = 0; $try -lt 20; $try++) {
        try {
            $fs = [System.IO.File]::Open($path, 'Append', 'Write', 'ReadWrite')
            $bytes = [System.Text.Encoding]::UTF8.GetBytes($line + "`n")
            $fs.Write($bytes, 0, $bytes.Length)
            $fs.Close()
            return
        } catch { Start-Sleep -Milliseconds 50 }
    }
    Write-Output "alert write failed: $path"
}
function Publish-WatchdogAlerts {
    if ($AlertsRoot -eq "") { return }
    if ($null -eq $script:runDir) {
        $script:runDir = Get-ChildItem -Path $AlertsRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.CreationTime -ge $startedAt.AddSeconds(-2) -and (Test-Path (Join-Path $_.FullName "events.ndjson")) } |
            Sort-Object CreationTime | Select-Object -First 1
        if ($null -eq $script:runDir) { return }
    }
    $events = Join-Path $script:runDir.FullName "events.ndjson"
    try {
        $fs = [System.IO.File]::Open($events, 'Open', 'Read', 'ReadWrite')
        if ($fs.Length -le $script:offset) { $fs.Close(); return }
        $fs.Position = $script:offset
        $bytes = New-Object byte[] ($fs.Length - $script:offset)
        $read = $fs.Read($bytes, 0, $bytes.Length)
        $fs.Close()
    } catch { return }
    $lastNewline = [Array]::LastIndexOf($bytes, [byte]10, $read - 1)
    if ($lastNewline -lt 0) { return }
    $script:offset += $lastNewline + 1
    foreach ($line in ([System.Text.Encoding]::UTF8.GetString($bytes, 0, $lastNewline + 1) -split "`n")) {
        if ($line -match '"kind":"(watchdog\.[a-z]+|run\.fail)","detail":"((?:[^"\\]|\\.)*)"') {
            $text = "$(Get-Date -Format HH:mm:ss) $($script:runDir.Name) $($Matches[1].ToUpper()) $($Matches[2]) [folder $($script:runDir.FullName)]"
            Write-Output $text
            Write-AlertLine (Join-Path $script:runDir.FullName "alerts.log") $text
            Write-AlertLine (Join-Path $AlertsRoot "alerts.log") $text
        }
    }
}

$deadline = $startedAt.AddSeconds($TimeoutSeconds)
while (-not $proc.WaitForExit(2000)) {
    Publish-WatchdogAlerts
    if ((Get-Date) -ge $deadline) {
        Publish-WatchdogAlerts
        Write-Output "timeout after $TimeoutSeconds s: killing player $playerPid"
        Stop-Process -Id $playerPid -Force -ErrorAction SilentlyContinue
        exit 124
    }
}
Publish-WatchdogAlerts
Write-Output "player exit=$($proc.ExitCode)"
exit $proc.ExitCode

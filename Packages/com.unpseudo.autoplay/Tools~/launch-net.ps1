<#
.SYNOPSIS
  Multi-process autoplay run: 1 host + N real network clients of the same player build, all WITHOUT focus.

.DESCRIPTION
  - The host gets a normal window (screenshots on); clients get small windows and -autoplay-no-png (state files only).
  - -ClientArgs go to every client, -FirstClientArgs to client1 only (e.g. one client that leaves mid-game).
  - Everyone shares the agreed port (-autoplay-port-strict) and one parent run folder (one sub-folder per process),
    so tools can compare their traces (compare_runs.py: desync detector).
  - -RelaunchArgs: when client1's game crashes on purpose (lever crash-at: journal "crash", process killed) or ends
    before the host's, the game is
    started again for that player after -RelaunchDelaySeconds, with the same command line plus -RelaunchArgs
    (e.g. -autoplay-relaunched), like a player relaunching his game. Once only.
  - Waits for the host (bounded), gives clients a short grace period, then kills every process it started.
  - Exit code = host exit code (0 = completed), 124 = host timeout.

.EXAMPLE
  powershell -File Tools~/launch-net.ps1 -Exe Builds/Autoplay/Game.exe -Clients 3 -Seed 7 -Port 7870 -OutRoot AutoplayRuns/net-7
#>
param(
    [Parameter(Mandatory = $true)][string]$Exe,
    [int]$Clients = 3,
    [int]$Seed = 1,
    [int]$Port = 7870,
    [Parameter(Mandatory = $true)][string]$OutRoot,
    [int]$TimeoutSeconds = 900,
    [string]$CommonArgs = "",
    [string]$ClientArgs = "",
    [string]$FirstClientArgs = "",
    [string]$RelaunchArgs = "",
    [int]$RelaunchDelaySeconds = 5,
    [string]$LogDir = ""
)

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class NoActivateLauncherNet
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

    public static int Start(string exe, string args, string dir)
    {
        var si = new STARTUPINFO();
        si.cb = Marshal.SizeOf(typeof(STARTUPINFO));
        si.dwFlags = 0x1;          // STARTF_USESHOWWINDOW
        si.wShowWindow = 4;        // SW_SHOWNOACTIVATE
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
$exeDir = Split-Path $exePath
New-Item -ItemType Directory -Force -Path $OutRoot | Out-Null
$out = (Resolve-Path $OutRoot).Path
if ($LogDir -eq "") { $LogDir = $out }
$foreground = [NoActivateLauncherNet]::GetForegroundWindow().ToInt64()
$shared = "-autoplay -autoplay-seed $Seed -autoplay-port $Port -autoplay-port-strict -autoplay-out `"$out`" -autoplay-restore-hwnd $foreground $CommonArgs"

$procs = @()
$client1Proc = $null
$client1CmdLine = $null
$hostArgs = "$shared -autoplay-role host -autoplay-clients $Clients -autoplay-scenario host -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile `"$LogDir\host.log`""
$hostPid = [NoActivateLauncherNet]::Start($exePath, $hostArgs, $exeDir)
$hostProc = Get-Process -Id $hostPid; $null = $hostProc.Handle
$procs += $hostProc
Write-Output "host pid=$hostPid port=$Port"

# Clients start once the host session is ready (game scene loaded): a client that joins while the host is still
# loading gets a broken scene synchronization ("Server Scene Handle already exist") and hangs.
$readyDeadline = (Get-Date).AddSeconds(120)
$hostReady = $false
while (-not $hostReady -and (Get-Date) -lt $readyDeadline -and -not $hostProc.HasExited) {
    $hostDir = Get-ChildItem -Path $out -Directory -Filter "*-host-*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -Last 1
    if ($hostDir) {
        $events = Join-Path $hostDir.FullName "events.ndjson"
        if ((Test-Path $events) -and (Select-String -Path $events -Pattern '"kind":"session.ready"' -SimpleMatch -Quiet)) {
            $hostReady = $true
        }
    }
    if (-not $hostReady) { Start-Sleep -Milliseconds 500 }
}
Write-Output ("host ready=" + $hostReady)
# Relay runs (lever relay): the host's journal holds the lobby code ("relay.lobby"), the clients join by it.
$joinCodeArg = ""
if ($hostReady -and $hostDir) {
    $lobbyLine = Select-String -Path (Join-Path $hostDir.FullName "events.ndjson") -Pattern '"kind":"relay.lobby"' -SimpleMatch | Select-Object -Last 1
    if ($lobbyLine -and $lobbyLine.Line -match '"detail":"([^"]+)"') {
        $joinCodeArg = "-autoplay-join-code $($Matches[1])"
        Write-Output "relay lobby code=$($Matches[1])"
    }
}
for ($i = 1; $i -le $Clients; $i++) {
    $extra = $ClientArgs
    if ($i -eq 1) { $extra = "$extra $FirstClientArgs" }
    $cmdLine = "$shared $extra $joinCodeArg -autoplay-role client -autoplay-connect 127.0.0.1 -autoplay-scenario client$i -autoplay-no-png -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$LogDir\client$i.log`""
    $clientPid = [NoActivateLauncherNet]::Start($exePath, $cmdLine, $exeDir)
    $p = Get-Process -Id $clientPid; $null = $p.Handle
    $procs += $p
    if ($i -eq 1) { $client1Proc = $p; $client1CmdLine = $cmdLine }
    Write-Output "client$i pid=$clientPid"
}

# client1's game crashed on purpose (lever crash-at): its journal says "crash", then the process is killed.
function Test-Crashed([string]$root) {
    $dir = Get-ChildItem -Path $root -Directory -Filter "*-client1-*" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime | Select-Object -First 1
    if (-not $dir) { return $false }
    $events = Join-Path $dir.FullName "events.ndjson"
    return (Test-Path $events) -and (Select-String -Path $events -Pattern '"kind":"crash"' -SimpleMatch -Quiet)
}

# Wait for the host; meanwhile relaunch client1 once if its game died or crashed first (RelaunchArgs).
$code = 124
$hostDeadline = (Get-Date).AddSeconds($TimeoutSeconds)
$relaunchAt = $null
$relaunched = $false
while (-not $hostProc.HasExited -and (Get-Date) -lt $hostDeadline) {
    if ($RelaunchArgs -ne "" -and -not $relaunched -and $client1Proc -ne $null -and ($client1Proc.HasExited -or (Test-Crashed $out))) {
        if ($relaunchAt -eq $null) {
            $relaunchAt = (Get-Date).AddSeconds($RelaunchDelaySeconds)
            Write-Output "client1 crashed or ended: relaunch in $RelaunchDelaySeconds s"

        } elseif ((Get-Date) -ge $relaunchAt) {
            $relaunchCmd = ($client1CmdLine -replace '-logFile "[^"]*"', "-logFile `"$LogDir\client1-relaunch.log`"") + " $RelaunchArgs"
            $relaunchPid = [NoActivateLauncherNet]::Start($exePath, $relaunchCmd, $exeDir)
            $rp = Get-Process -Id $relaunchPid; $null = $rp.Handle
            $procs += $rp
            $relaunched = $true
            Write-Output "client1 relaunched pid=$relaunchPid"
        }
    }
    Start-Sleep -Milliseconds 500
}
if ($hostProc.HasExited) {
    $code = $hostProc.ExitCode
    Write-Output "host exit=$code"
} else {
    Write-Output "host timeout after $TimeoutSeconds s"
}

# Clients end on their own once the host session closes; give them a moment, then clean up whatever is left.
$deadline = (Get-Date).AddSeconds(20)
foreach ($p in $procs) {
    $left = [Math]::Max(0, ($deadline - (Get-Date)).TotalMilliseconds)
    if (-not $p.WaitForExit([int]$left)) {
        Write-Output "killing pid=$($p.Id)"
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    } elseif ($p.Id -ne $hostPid) {
        Write-Output "pid=$($p.Id) exit=$($p.ExitCode)"
    }
}
exit $code

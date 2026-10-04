<#
.SYNOPSIS
  Multi-process autoplay run: 1 host + N real network clients of the same player build, all WITHOUT focus.

.DESCRIPTION
  - The host gets a normal window (screenshots on); clients get small windows and -autoplay-no-png (state files only).
  - Everyone shares the agreed port (-autoplay-port-strict) and one parent run folder (one sub-folder per process),
    so tools can compare their traces (compare_runs.py: desync detector).
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
$hostArgs = "$shared -autoplay-role host -autoplay-clients $Clients -autoplay-scenario host -screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile `"$LogDir\host.log`""
$hostPid = [NoActivateLauncherNet]::Start($exePath, $hostArgs, $exeDir)
$hostProc = Get-Process -Id $hostPid; $null = $hostProc.Handle
$procs += $hostProc
Write-Output "host pid=$hostPid port=$Port"

Start-Sleep -Seconds 3
for ($i = 1; $i -le $Clients; $i++) {
    $clientArgs = "$shared -autoplay-role client -autoplay-connect 127.0.0.1 -autoplay-scenario client$i -autoplay-no-png -screen-fullscreen 0 -screen-width 640 -screen-height 360 -logFile `"$LogDir\client$i.log`""
    $clientPid = [NoActivateLauncherNet]::Start($exePath, $clientArgs, $exeDir)
    $p = Get-Process -Id $clientPid; $null = $p.Handle
    $procs += $p
    Write-Output "client$i pid=$clientPid"
}

$code = 124
if ($hostProc.WaitForExit($TimeoutSeconds * 1000)) {
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

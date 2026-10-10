# Shared by launch-background.ps1 and launch-net.ps1 (dot-sourced): one autoplay run at a time on the machine, in order.
#
# Two runs share ports, CPU and GPU (seen: a second campaign on the same port, a client crashed in the graphics driver),
# so a launcher waits for its turn. AUTOPLAY_MAX_PARALLEL=N lets N runs share the machine (each on its own port).
#
# FIFO: a launcher counts the ACTIVE launchers (a game process of theirs is alive) and the launchers still WAITING that
# started before it (tie: lower pid first), and goes when that count is below the limit. The oldest waiter always goes
# first. The previous rule counted every other launcher, waiting or not: two launchers waiting at the same time (two
# lanes of a sweep, or two sessions) each waited for the other until the 60 min deadline (2026-10-09).
# Safety net for launchers of an older version of this rule (other checkouts) that still wait for any launcher: when no
# launcher has been active for 15 s, the waiters go one by one, 10 s apart in age order.

function Wait-AutoplayLauncherTurn {
    $maxParallel = 1
    if ($env:AUTOPLAY_MAX_PARALLEL -match '^\d+$') { $maxParallel = [Math]::Max(1, [int]$env:AUTOPLAY_MAX_PARALLEL) }
    $self = Get-CimInstance Win32_Process -Filter "ProcessId=$PID" -ErrorAction SilentlyContinue
    $selfStart = if ($self) { $self.CreationDate } else { Get-Date }
    $waitedFor = $null
    $idleSince = $null
    $queueDeadline = (Get-Date).AddMinutes(60)
    while ((Get-Date) -lt $queueDeadline) {
        $all = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue)
        $launchers = @($all | Where-Object {
            $_.ProcessId -ne $PID -and ($_.Name -eq 'powershell.exe' -or $_.Name -eq 'pwsh.exe') -and
            $_.CommandLine -match 'launch-(net|background)\.ps1' })
        $parents = @{}
        foreach ($p in $all) {
            if ($p.Name -ne 'conhost.exe') { $parents[[int]$p.ParentProcessId] = $true }
        }
        $active = @($launchers | Where-Object { $parents.ContainsKey([int]$_.ProcessId) })
        $olderWaiting = @($launchers | Where-Object {
            -not $parents.ContainsKey([int]$_.ProcessId) -and
            ($_.CreationDate -lt $selfStart -or ($_.CreationDate -eq $selfStart -and $_.ProcessId -lt $PID)) })
        if ($active.Count + $olderWaiting.Count -lt $maxParallel) { break }

        # Nobody plays: the waiters ahead may be stuck on the older rule. Go after 15 s, 10 s more per waiter ahead.
        if ($active.Count -eq 0) {
            if ($null -eq $idleSince) { $idleSince = Get-Date }
            $grace = 15 + 10 * $olderWaiting.Count
            if (((Get-Date) - $idleSince).TotalSeconds -ge $grace) {
                Write-Output ("queue: nothing played for {0}s, going ahead of waiting launcher(s) {1}" -f $grace,
                    (($olderWaiting | ForEach-Object { $_.ProcessId }) -join ','))
                break
            }
        } else {
            $idleSince = $null
        }
        if ($null -eq $waitedFor) {
            $waitedFor = Get-Date
            $ahead = @($active + $olderWaiting)
            Write-Output "BUSY another autoplay run is in flight or queued first (launcher pid $($ahead[0].ProcessId)): waiting for it"
        }
        Start-Sleep -Seconds 5
    }
    if ($null -ne $waitedFor) { Write-Output ("queue wait {0:0}s" -f ((Get-Date) - $waitedFor).TotalSeconds) }
}

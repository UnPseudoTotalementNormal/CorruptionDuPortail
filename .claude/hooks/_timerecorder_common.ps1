# TimeRecorder - shared helpers for the start/stop hooks.
# Dot-sourced by claude_time_start.ps1 and claude_time_stop.ps1.
#
# Model: wall-clock ACTIVE working time (not just Claude compute time).
# Each hook fire (prompt submit OR response end) stamps a per-session
# "last activity" timestamp. The elapsed gap since the previous stamp is
# accrued into a per-day ledger:
#   - response-end (Stop)      -> gap = submit..response = compute, accrued
#                                 clamped to the max-compute cap.
#   - next prompt (Start)      -> gap = read + think + type between turns,
#                                 accrued ONLY if <= the idle threshold. A
#                                 longer gap means the dev walked away, so it
#                                 is dropped (never over-counts idle time).
# Net effect: continuous working time is captured, away-from-keyboard gaps
# are not.
#
# The ledger decides pay, so accuracy matters both ways. Ledger writes are
# serialized behind an exclusive lock and committed atomically (temp + Replace)
# so parallel sessions can't clobber each other and a reader (Unity) never sees
# a half-written file. On lock timeout an accrual is queued to a per-GUID
# pending file instead of racing, and flushed by the next lock holder - nothing
# is lost. A corrupt ledger is left untouched (queued to pending + stderr warn),
# never silently reset, so paid history is never destroyed.

# Stop-path compute cap. A single submit..response gap longer than this is
# clamped - guards against a machine sleeping or a permission prompt left open
# mid-turn (which would otherwise book hours of phantom pay). PAY POLICY KNOB:
# a genuine >30min single agent turn is under-counted by design. Tune with the
# payer.
$script:TR_MaxComputeSeconds = 1800      # 30 min

# Start-path idle gate. Between-turn gap (reading / thinking / typing) up to
# this is counted as work; a longer gap is treated as away-from-keyboard and
# contributes nothing. PAY POLICY KNOB - tune with the payer.
$script:TR_IdleThresholdSeconds = 900    # 15 min

# Age past which an activity stamp from another session is deemed dead and
# swept. Decoupled from (and far above) the compute cap so a genuinely long
# live turn in a concurrent session is never swept out from under it.
$script:TR_StaleActivitySeconds = 86400  # 24 h

function Get-TimeRecorderDir {
    $projectDir = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }
    $dir = Join-Path $projectDir '.claude/timerecorder'
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null
    return $dir
}

# Per-session activity stamp path. The session id is sanitized to a safe file
# name charset so a malformed id can never throw on I/O or escape the folder.
function Get-ActivityPath {
    param([string]$Dir, [string]$Session)
    $safe = $Session -replace '[^A-Za-z0-9._-]', '_'
    if ([string]::IsNullOrWhiteSpace($safe)) { $safe = 'default' }
    return (Join-Path $Dir ("activity_{0}.txt" -f $safe))
}

# Atomically write $Content to $Path: write a temp sibling, then swap it in with
# File.Replace (atomic on NTFS). A reader never observes a partial file.
function Write-FileAtomic {
    param([string]$Path, [string]$Content)
    $tmp = "$Path.tmp." + [Guid]::NewGuid().ToString('N')
    [System.IO.File]::WriteAllText($tmp, $Content)
    try {
        if (Test-Path $Path) { [System.IO.File]::Replace($tmp, $Path, $null) }
        else                 { [System.IO.File]::Move($tmp, $Path) }
    } catch {
        # Fallback for the rare case Replace/Move is blocked (AV, reader lock).
        Remove-Item -Path $Path -Force -ErrorAction SilentlyContinue
        [System.IO.File]::Move($tmp, $Path)
    } finally {
        if (Test-Path $tmp) { Remove-Item -Path $tmp -Force -ErrorAction SilentlyContinue }
    }
}

# Acquire an exclusive OS lock on the ledger lock file. Retries for up to ~5s,
# then returns $null so the caller can queue to pending rather than hang the
# hook (a hanging hook would stall Claude).
function Get-LedgerLock {
    param([string]$Dir)
    $lockPath = Join-Path $Dir 'claude_time.json.lock'
    for ($i = 0; $i -lt 100; $i++) {
        try {
            return [System.IO.File]::Open(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
        } catch {
            Start-Sleep -Milliseconds 50
        }
    }
    return $null
}

# Queue an accrual to a per-GUID pending file (no write contention, atomic-ish
# single writer). Flushed into the ledger by the next lock holder. Format:
# "y-m-d seconds".
function Add-Pending {
    param([string]$Dir, [string]$DayKey, [int]$Seconds)
    if ($Seconds -le 0) { return }
    $f = Join-Path $Dir ("pending_{0}.txt" -f ([Guid]::NewGuid().ToString('N')))
    [System.IO.File]::WriteAllText($f, ("{0} {1}" -f $DayKey, $Seconds))
}

# Read the ledger into a "y-m-d" -> seconds hashtable. Returns an empty table
# for a missing or blank ledger (fresh start), or $null if the file exists but
# is unparseable (corrupt) so the caller can avoid overwriting it. Duplicate
# day entries are summed, not overwritten.
function Read-Ledger {
    param([string]$Dir)
    $ledgerPath = Join-Path $Dir 'claude_time.json'
    $byKey = @{}
    if (-not (Test-Path $ledgerPath)) { return $byKey }

    $raw = $null
    try { $raw = [System.IO.File]::ReadAllText($ledgerPath) } catch { return $null }
    if ([string]::IsNullOrWhiteSpace($raw)) { return $byKey }

    $ledger = $null
    try { $ledger = $raw | ConvertFrom-Json } catch { return $null }

    foreach ($e in @($ledger.days)) {
        if ($null -eq $e) { continue }
        $k = "{0}-{1}-{2}" -f [int]$e.year, [int]$e.month, [int]$e.day
        if ($byKey.ContainsKey($k)) { $byKey[$k] += [int]$e.seconds } else { $byKey[$k] = [int]$e.seconds }
    }
    return $byKey
}

# Serialize the hashtable back to the ledger JSON, atomically. Built by hand so
# a single day still serializes as an array (PowerShell's ConvertTo-Json
# collapses a 1-element collection to an object).
function Write-Ledger {
    param([string]$Dir, [hashtable]$ByKey)
    $parts = foreach ($k in $ByKey.Keys) {
        $kp = $k.Split('-')
        '{{"year":{0},"month":{1},"day":{2},"seconds":{3}}}' -f [int]$kp[0], [int]$kp[1], [int]$kp[2], [int]$ByKey[$k]
    }
    $total = 0
    foreach ($v in $ByKey.Values) { $total += [int]$v }
    $json = '{"days":[' + ($parts -join ',') + '],"totalSeconds":' + $total + '}'
    Write-FileAtomic -Path (Join-Path $Dir 'claude_time.json') -Content $json
}

# Flush all pending files plus the caller's extra accrual into the ledger.
# MUST run while holding the ledger lock. Pending files are consumed (deleted)
# before the write; if the write fails, everything is re-queued so nothing is
# lost. A corrupt ledger is left untouched and the accrual is queued instead.
function Commit-Ledger {
    param([string]$Dir, [string]$ExtraDayKey, [int]$ExtraSeconds)

    $byKey = Read-Ledger -Dir $Dir
    if ($null -eq $byKey) {
        [Console]::Error.WriteLine("[timerecorder] ledger unreadable/corrupt; queuing accrual to pending, ledger left intact")
        Add-Pending -Dir $Dir -DayKey $ExtraDayKey -Seconds $ExtraSeconds
        return
    }

    # Consume pending files into an in-memory list (delete now; re-queue on
    # write failure). Bias to under-count over over-count on a hard kill.
    $queued = New-Object System.Collections.ArrayList
    Get-ChildItem -Path $Dir -File -Filter 'pending_*.txt' -ErrorAction SilentlyContinue | ForEach-Object {
        $line = ''
        try { $line = [System.IO.File]::ReadAllText($_.FullName).Trim() } catch { $line = '' }
        $seg = $line.Split(' ')
        if ($seg.Count -eq 2) {
            $s = 0
            if ([int]::TryParse($seg[1], [ref]$s) -and $s -gt 0) { [void]$queued.Add(@($seg[0], $s)) }
        }
        Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue
    }

    foreach ($q in $queued) {
        $dk = [string]$q[0]; $s = [int]$q[1]
        if ($byKey.ContainsKey($dk)) { $byKey[$dk] += $s } else { $byKey[$dk] = $s }
    }
    if ($ExtraSeconds -gt 0) {
        if ($byKey.ContainsKey($ExtraDayKey)) { $byKey[$ExtraDayKey] += $ExtraSeconds } else { $byKey[$ExtraDayKey] = $ExtraSeconds }
    }

    try {
        Write-Ledger -Dir $Dir -ByKey $byKey
    } catch {
        [Console]::Error.WriteLine("[timerecorder] ledger write failed, re-queuing: $_")
        foreach ($q in $queued) { Add-Pending -Dir $Dir -DayKey ([string]$q[0]) -Seconds ([int]$q[1]) }
        if ($ExtraSeconds -gt 0) { Add-Pending -Dir $Dir -DayKey $ExtraDayKey -Seconds $ExtraSeconds }
    }
}

# Accrue $Seconds into today's bucket. Takes the lock and commits (flushing any
# pending), or queues to pending if the lock can't be had.
function Persist-Accrual {
    param([string]$Dir, [int]$Seconds)
    if ($Seconds -le 0) { return }
    $now    = Get-Date
    $dayKey = "{0}-{1}-{2}" -f $now.Year, $now.Month, $now.Day

    $fs = Get-LedgerLock -Dir $Dir
    if ($null -ne $fs) {
        try { Commit-Ledger -Dir $Dir -ExtraDayKey $dayKey -ExtraSeconds $Seconds }
        finally { $fs.Close() }
    } else {
        Add-Pending -Dir $Dir -DayKey $dayKey -Seconds $Seconds
    }
}

# Stamp this session's activity time and accrue the elapsed gap since the last
# stamp. $IdleGated true (prompt submit) drops gaps longer than the idle
# threshold; false (response end) accrues the gap clamped to the compute cap.
# A missing / empty / non-numeric stamp is treated as "no baseline" (accrue 0)
# and always re-stamped, so a corrupt stamp can never brick a session.
# Returns the seconds accrued.
function Update-Activity {
    param([string]$Dir, [string]$Session, [bool]$IdleGated)

    $path  = Get-ActivityPath -Dir $Dir -Session $Session
    $nowMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()

    # A stamp must be all-digits. Note [long]'' is 0 (not an error) in PowerShell,
    # so an empty/blank stamp would otherwise yield a giant gap - the regex
    # rejects it. A gap beyond the stale window means a broken/truncated baseline
    # (never a real turn), so it accrues nothing instead of clamping to a cap.
    $accrue = 0
    if (Test-Path $path) {
        try {
            $txt = [System.IO.File]::ReadAllText($path).Trim()
            if ($txt -match '^\d{1,18}$') {
                $lastMs = [long]$txt
                $gap    = [long][Math]::Floor(($nowMs - $lastMs) / 1000.0)
                if ($gap -gt 0 -and $gap -le $script:TR_StaleActivitySeconds) {
                    if ($IdleGated) {
                        if ($gap -le $script:TR_IdleThresholdSeconds) { $accrue = [int]$gap }
                    } else {
                        $accrue = [int][Math]::Min($gap, $script:TR_MaxComputeSeconds)
                    }
                }
            }
        } catch { $accrue = 0 }
    }

    Write-FileAtomic -Path $path -Content ([string]$nowMs)

    if ($accrue -gt 0) { Persist-Accrual -Dir $Dir -Seconds $accrue }
    return $accrue
}

# Delete a session's activity stamp (used on pause / teardown).
function Clear-Activity {
    param([string]$Dir, [string]$Session)
    $path = Get-ActivityPath -Dir $Dir -Session $Session
    Remove-Item -Path $path -Force -ErrorAction SilentlyContinue
}

# Housekeeping. Delete dead activity stamps from other sessions (older than the
# stale threshold, never the current session), plus legacy start_*.txt /
# completed_turn_*.txt markers and orphaned atomic-write temp files.
function Remove-StaleActivity {
    param([string]$Dir, [string]$Session)

    $keep   = Get-ActivityPath -Dir $Dir -Session $Session
    $cutoff = (Get-Date).AddSeconds(-1 * $script:TR_StaleActivitySeconds)

    Get-ChildItem -Path $Dir -File -Filter 'activity_*.txt' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $keep -and $_.LastWriteTime -lt $cutoff } |
        ForEach-Object { Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue }

    foreach ($pat in @('start_*.txt', 'completed_turn_*.txt')) {
        Get-ChildItem -Path $Dir -File -Filter $pat -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue }
    }

    Get-ChildItem -Path $Dir -File -Filter '*.tmp.*' -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -lt $cutoff } |
        ForEach-Object { Remove-Item -Path $_.FullName -Force -ErrorAction SilentlyContinue }
}

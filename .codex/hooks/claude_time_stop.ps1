# TimeRecorder - AI working-time tracker (STOP / accumulate)
# Hook: Stop. Computes the elapsed time for the matching Codex turn and adds it
# to the per-day ledger that the Unity TimeRecorder calendar reads.
#
# Codex can have multiple turns in the same session. The start hook therefore
# keys markers by turn_id, while keeping session_id as a compatibility fallback.

$ErrorActionPreference = 'Stop'

# A single turn longer than this is clamped - protects the ledger against idle
# app/background waits or a session left open for hours.
$MaxTurnSeconds = 14400   # 4h

function Read-HookPayload {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [pscustomobject]@{}
    }

    try {
        return $raw | ConvertFrom-Json
    } catch {
        [Console]::Error.WriteLine("[timerecorder stop hook] Invalid hook payload: $_")
        return [pscustomobject]@{}
    }
}

function Get-ProjectDirectory {
    param([object]$Payload)

    if ($Payload.cwd -and (Test-Path -LiteralPath ([string]$Payload.cwd) -PathType Container)) {
        return [System.IO.Path]::GetFullPath([string]$Payload.cwd)
    }

    if (-not [string]::IsNullOrWhiteSpace($env:CLAUDE_PROJECT_DIR)) {
        return [System.IO.Path]::GetFullPath($env:CLAUDE_PROJECT_DIR)
    }

    return (Get-Location).Path
}

function Get-SafeMarkerKey {
    param([object]$Payload)

    $id = if (-not [string]::IsNullOrWhiteSpace([string]$Payload.turn_id)) {
        "turn_{0}" -f [string]$Payload.turn_id
    } elseif (-not [string]::IsNullOrWhiteSpace([string]$Payload.session_id)) {
        "session_{0}" -f [string]$Payload.session_id
    } else {
        'default'
    }

    return ($id -replace '[^A-Za-z0-9._-]', '_')
}

function Acquire-LedgerLock {
    param([string]$LockPath)

    # Stop hooks can arrive close together (for example with subagents). Keep
    # the read/modify/write ledger operation serialized and bounded.
    for ($attempt = 0; $attempt -lt 100; $attempt++) {
        try {
            return [System.IO.File]::Open(
                $LockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::None)
        } catch {
            Start-Sleep -Milliseconds 25
        }
    }

    throw "Could not acquire time ledger lock: $LockPath"
}

try {
    $payload = Read-HookPayload
    $projectDir = Get-ProjectDirectory $payload
    $dir = Join-Path $projectDir '.claude/timerecorder'
    $marker = Join-Path $dir ("start_{0}.txt" -f (Get-SafeMarkerKey $payload))

    # AI tracking paused from the Unity calendar window -> drop any in-flight
    # marker and accrue nothing.
    $pauseFlag = Join-Path $dir 'ai_paused.flag'
    if (Test-Path -LiteralPath $pauseFlag) {
        Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
        exit 0
    }

    # No start recorded, or another Stop hook already claimed this marker.
    if (-not (Test-Path -LiteralPath $marker)) { exit 0 }

    # Claim the marker atomically. This prevents two near-simultaneous Stop
    # hooks from adding the same turn twice.
    $claimedMarker = "{0}.claiming_{1}_{2}" -f $marker, $PID, ([guid]::NewGuid().ToString('N'))
    try {
        [System.IO.File]::Move($marker, $claimedMarker)
    } catch {
        exit 0
    }

    $ledgerPath = Join-Path $dir 'claude_time.json'
    $lockPath = "$ledgerPath.lock"
    $lockStream = $null
    $tempLedgerPath = $null
    $backupLedgerPath = $null
    $ledgerWritten = $false

    try {
        $startMs = [long]([System.IO.File]::ReadAllText($claimedMarker).Trim())
        $nowMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
        $seconds = [int][Math]::Floor(($nowMs - $startMs) / 1000.0)

        if ($seconds -le 0) {
            $ledgerWritten = $true
            exit 0
        }
        if ($seconds -gt $MaxTurnSeconds) { $seconds = $MaxTurnSeconds }

        $lockStream = Acquire-LedgerLock $lockPath

        # Load existing entries into a hashtable keyed by "y-m-d".
        $byKey = @{}
        if (Test-Path -LiteralPath $ledgerPath) {
            $existing = [System.IO.File]::ReadAllText($ledgerPath)
            if (-not [string]::IsNullOrWhiteSpace($existing)) {
                $ledger = $existing | ConvertFrom-Json
                foreach ($entry in @($ledger.days)) {
                    if ($null -eq $entry) { continue }
                    $key = "{0}-{1}-{2}" -f [int]$entry.year, [int]$entry.month, [int]$entry.day
                    $byKey[$key] = [int64]$entry.seconds
                }
            }
        }

        $now = Get-Date
        $key = "{0}-{1}-{2}" -f $now.Year, $now.Month, $now.Day
        if ($byKey.ContainsKey($key)) { $byKey[$key] += $seconds } else { $byKey[$key] = $seconds }

        # Build JSON by hand so a single entry remains an array.
        $parts = foreach ($entryKey in ($byKey.Keys | Sort-Object)) {
            $keyParts = $entryKey.Split('-')
            '{{"year":{0},"month":{1},"day":{2},"seconds":{3}}}' -f `
                [int]$keyParts[0], [int]$keyParts[1], [int]$keyParts[2], [int64]$byKey[$entryKey]
        }

        $total = 0L
        foreach ($value in $byKey.Values) { $total += [int64]$value }
        $json = '{"days":[' + ($parts -join ',') + '],"totalSeconds":' + $total + '}'

        # Write a complete file and replace the old ledger so Unity never reads
        # a partially-written JSON document.
        $tempLedgerPath = "{0}.{1}.{2}.tmp" -f $ledgerPath, $PID, ([guid]::NewGuid().ToString('N'))
        [System.IO.File]::WriteAllText($tempLedgerPath, $json)
        if (Test-Path -LiteralPath $ledgerPath) {
            $backupLedgerPath = "{0}.{1}.{2}.bak" -f $ledgerPath, $PID, ([guid]::NewGuid().ToString('N'))
            [System.IO.File]::Replace($tempLedgerPath, $ledgerPath, $backupLedgerPath)
            Remove-Item -LiteralPath $backupLedgerPath -Force -ErrorAction SilentlyContinue
            $backupLedgerPath = $null
        } else {
            [System.IO.File]::Move($tempLedgerPath, $ledgerPath)
        }
        $tempLedgerPath = $null
        $ledgerWritten = $true
    } finally {
        if ($lockStream) { $lockStream.Dispose() }
        if ($tempLedgerPath -and (Test-Path -LiteralPath $tempLedgerPath)) {
            Remove-Item -LiteralPath $tempLedgerPath -Force -ErrorAction SilentlyContinue
        }
        if ($backupLedgerPath -and (Test-Path -LiteralPath $backupLedgerPath)) {
            Remove-Item -LiteralPath $backupLedgerPath -Force -ErrorAction SilentlyContinue
        }
        if ($ledgerWritten -and (Test-Path -LiteralPath $claimedMarker)) {
            Remove-Item -LiteralPath $claimedMarker -Force -ErrorAction SilentlyContinue
        } elseif (Test-Path -LiteralPath $claimedMarker) {
            # Preserve the turn for a later retry if ledger IO failed.
            if (-not (Test-Path -LiteralPath $marker)) {
                [System.IO.File]::Move($claimedMarker, $marker)
            } else {
                Remove-Item -LiteralPath $claimedMarker -Force -ErrorAction SilentlyContinue
            }
        }
    }
} catch {
    [Console]::Error.WriteLine("[timerecorder stop hook] $_")
}

exit 0

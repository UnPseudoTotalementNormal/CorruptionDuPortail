# TimeRecorder - AI working-time tracker (START marker)
# Hook: UserPromptSubmit. Records one marker per Codex turn.
#
# Codex exposes both a session_id and a turn_id. A session can contain several
# turns (and background prompts), so turn_id must be preferred when available.
#
# IMPORTANT: this hook must not write anything to stdout. For UserPromptSubmit,
# stdout is injected into the prompt context. Diagnostics go to stderr only.

$ErrorActionPreference = 'Stop'

function Read-HookPayload {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return [pscustomobject]@{}
    }

    try {
        return $raw | ConvertFrom-Json
    } catch {
        [Console]::Error.WriteLine("[timerecorder start hook] Invalid hook payload: $_")
        return [pscustomobject]@{}
    }
}

function Get-ProjectDirectory {
    param([object]$Payload)

    # Codex includes the event cwd in its hook payload. Prefer it over the
    # process cwd so app-server/background hook execution stays project-local.
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

function Test-TurnClaimed {
    param([string]$Marker)

    $directory = [System.IO.Path]::GetDirectoryName($Marker)
    $pattern = "{0}.claiming_*" -f [System.IO.Path]::GetFileName($Marker)
    return $null -ne (Get-ChildItem -LiteralPath $directory -Filter $pattern -File -ErrorAction SilentlyContinue |
        Select-Object -First 1)
}

function Remove-StaleCompletedMarkers {
    param([string]$Directory)

    # Completed ids only guard against delayed hook replays. Keep a generous
    # window, then prune them so one small file per turn does not grow forever.
    $cutoff = [DateTime]::UtcNow.AddDays(-7)
    Get-ChildItem -LiteralPath $Directory -Filter 'completed_*.txt' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -lt $cutoff } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

try {
    $payload = Read-HookPayload
    $projectDir = Get-ProjectDirectory $payload
    $dir = Join-Path $projectDir '.claude/timerecorder'
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null
    Remove-StaleCompletedMarkers $dir

    # AI tracking paused from the Unity calendar window -> record no start marker.
    $pauseFlag = Join-Path $dir 'ai_paused.flag'
    if (Test-Path -LiteralPath $pauseFlag) { exit 0 }

    $markerKey = Get-SafeMarkerKey $payload
    $marker = Join-Path $dir ("start_{0}.txt" -f $markerKey)
    $completedMarker = Join-Path $dir ("completed_{0}.txt" -f $markerKey)

    # Codex can replay a prompt event for the same turn. A Stop hook may have
    # moved the start marker while it updates the ledger, so all three states
    # participate in the idempotency check.
    if ((Test-Path -LiteralPath $marker) -or
        (Test-Path -LiteralPath $completedMarker) -or
        (Test-TurnClaimed $marker)) {
        exit 0
    }

    $startMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $tempMarker = "{0}.{1}.{2}.tmp" -f $marker, $PID, ([guid]::NewGuid().ToString('N'))

    try {
        # Publish a complete marker with an exclusive move. Concurrent replayed
        # Start hooks cannot truncate or reset the original timestamp.
        [System.IO.File]::WriteAllText($tempMarker, [string]$startMs)
        try {
            [System.IO.File]::Move($tempMarker, $marker)
        } catch {
            if (Test-Path -LiteralPath $marker) { exit 0 }
            throw
        }

        # Close the race where Stop claims or completes the turn after the
        # initial state check but before this Start publishes its marker.
        if ((Test-Path -LiteralPath $completedMarker) -or (Test-TurnClaimed $marker)) {
            Remove-Item -LiteralPath $marker -Force -ErrorAction SilentlyContinue
        }
    } finally {
        if (Test-Path -LiteralPath $tempMarker) {
            Remove-Item -LiteralPath $tempMarker -Force -ErrorAction SilentlyContinue
        }
    }
} catch {
    [Console]::Error.WriteLine("[timerecorder start hook] $_")
}

exit 0

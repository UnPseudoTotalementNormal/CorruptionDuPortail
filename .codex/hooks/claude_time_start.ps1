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

try {
    $payload = Read-HookPayload
    $projectDir = Get-ProjectDirectory $payload
    $dir = Join-Path $projectDir '.claude/timerecorder'
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null

    # AI tracking paused from the Unity calendar window -> record no start marker.
    $pauseFlag = Join-Path $dir 'ai_paused.flag'
    if (Test-Path -LiteralPath $pauseFlag) { exit 0 }

    $marker = Join-Path $dir ("start_{0}.txt" -f (Get-SafeMarkerKey $payload))

    # Codex can replay a prompt event for the same turn. Do not reset the clock
    # when that happens.
    if (Test-Path -LiteralPath $marker) { exit 0 }

    $startMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    [System.IO.File]::WriteAllText($marker, [string]$startMs)
} catch {
    [Console]::Error.WriteLine("[timerecorder start hook] $_")
}

exit 0

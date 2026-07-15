# TimeRecorder - Claude Code working-time tracker (turn START).
# Hook: UserPromptSubmit. Stamps this session's activity time and accrues the
# between-turn gap (read + think + type since the last response) as working
# time, provided it is under the idle threshold.
#
# IMPORTANT: this hook must NOT write anything to stdout. For UserPromptSubmit,
# a hook's stdout is injected into the prompt context - any output would pollute
# every prompt. Only touch files; send diagnostics to stderr.

$ErrorActionPreference = 'Stop'

try {
    . (Join-Path $PSScriptRoot '_timerecorder_common.ps1')

    $raw = [Console]::In.ReadToEnd()

    $sessionId = 'default'
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $payload = $raw | ConvertFrom-Json
        if ($payload.session_id) { $sessionId = [string]$payload.session_id }
    }

    $dir = Get-TimeRecorderDir

    # AI tracking paused from the Unity calendar window -> accrue nothing and
    # drop any stale stamp so the gap across the pause is never counted.
    $pauseFlag = Join-Path $dir 'ai_paused.flag'
    if (Test-Path $pauseFlag) {
        Clear-Activity -Dir $dir -Session $sessionId
        exit 0
    }

    # Accrue the between-turn gap (idle-gated), then stamp now as last activity.
    [void](Update-Activity -Dir $dir -Session $sessionId -IdleGated $true)

    # Housekeeping: drop dead stamps from abandoned sessions and legacy markers.
    Remove-StaleActivity -Dir $dir -Session $sessionId
} catch {
    [Console]::Error.WriteLine("[timerecorder start hook] $_")
}

exit 0

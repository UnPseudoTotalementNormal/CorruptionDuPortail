# TimeRecorder - Claude Code working-time tracker (turn END / accumulate).
# Hook: Stop. Accrues the compute gap (submit..response) as working time and
# re-stamps the session's activity time so the following prompt can measure the
# read/think/type gap from here.
#
# Day bucket = local date at turn end. The ledger JSON is owned entirely by
# these hooks (machine-local, gitignored); Unity only reads it. All ledger
# writes go through the lock in _timerecorder_common.ps1 so parallel sessions
# can't clobber each other's accruals.

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
    # drop the in-flight stamp (turn that began before the pause).
    $pauseFlag = Join-Path $dir 'ai_paused.flag'
    if (Test-Path $pauseFlag) {
        Clear-Activity -Dir $dir -Session $sessionId
        exit 0
    }

    # Accrue the compute gap (not idle-gated, clamped to the max turn length)
    # and re-stamp now as the last activity for the next between-turn measure.
    [void](Update-Activity -Dir $dir -Session $sessionId -IdleGated $false)
} catch {
    [Console]::Error.WriteLine("[timerecorder stop hook] $_")
}

exit 0

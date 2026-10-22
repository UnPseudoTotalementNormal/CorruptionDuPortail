# TimeRecorder - Claude Code working-time tracker (turn END / accumulate).
# Hook: Stop. Accrues the compute gap (submit..response) as working time and
# re-stamps the session's activity time so the following prompt can measure the
# read/think/type gap from here. The turn is journaled with what it did
# (skills, tools, commands, files, tags) read from the session transcript, and
# whether a human prompt opened it or a background wake-up (task notification).
#
# Day bucket = local date at turn end. The ledger JSON is owned entirely by
# these hooks (machine-local, gitignored); Unity only reads it. All ledger
# writes go through the lock in _timerecorder_common.ps1 so parallel sessions
# can't clobber each other's accruals.

$ErrorActionPreference = 'Stop'

try {
    . (Join-Path $PSScriptRoot '_timerecorder_common.ps1')

    $raw = Read-HookStdin

    $sessionId = 'default'
    $payload = $null
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $payload = $raw | ConvertFrom-Json
        if ($payload.session_id) { $sessionId = [string]$payload.session_id }
    }

    $dir = Get-TimeRecorderDir

    # AI tracking paused from the Unity calendar window, or a session that is
    # not work on this project (Clawd view edit, see Test-OffProjectPrompt) ->
    # accrue nothing and drop the in-flight stamp.
    $pauseFlag = Join-Path $dir 'ai_paused.flag'
    $state = Read-TurnState -Dir $dir -Session $sessionId
    if ((Test-Path $pauseFlag) -or $state.offProject) {
        Clear-Activity -Dir $dir -Session $sessionId
        # Skip the skipped turn's tool calls so they never label a later turn.
        if ($payload -and $payload.transcript_path -and (Test-Path -LiteralPath ([string]$payload.transcript_path))) {
            $state.offset = (Get-Item -LiteralPath ([string]$payload.transcript_path)).Length
        }
        $state.promptPending = $false
        Write-TurnState -Dir $dir -Session $sessionId -State $state
        exit 0
    }

    # Accrue the compute gap (not idle-gated, clamped to the max turn length)
    # and re-stamp now as the last activity for the next between-turn measure.
    $acc = Update-Activity -Dir $dir -Session $sessionId -IdleGated $false

    # Advance the transcript cursor every turn, booked or not, so the next turn
    # only sees its own tool calls.
    $transcript = if ($payload -and $payload.transcript_path) { [string]$payload.transcript_path } else { '' }
    $read = Read-TranscriptSince -Path $transcript -Offset $state.offset

    if ($acc.seconds -gt 0) {
        $sessionDir = $env:CLAUDE_PROJECT_DIR
        if ([string]::IsNullOrWhiteSpace($sessionDir)) { $sessionDir = (Get-Location).Path }
        $git = Get-GitContext $sessionDir
        $activity = Get-TurnActivity -Entries $read.entries -Root $git.root
        $trigger = if ($state.promptPending -and $state.trigger -ne 'background') { 'prompt' } else { 'background' }
        $prompt = if ($state.promptPending) { $state.prompt } else { '' }
        Add-IntervalRecord -Dir $dir -Json (New-IntervalRecord -Source 'claude' -Kind 'turn' -Trigger $trigger `
            -EndMs $acc.nowMs -Seconds $acc.seconds -Clamped $acc.clamped -Session $sessionId -Git $git `
            -SessionDir $sessionDir -Prompt $prompt -Activity $activity)
    }

    # Remember the autoplay runs this turn left going in the background, so the
    # start hook recognises their notification and drops the wait before it.
    $launched = @(Get-AutoplayBackgroundIds -Entries $read.entries)
    if ($launched.Count -gt 0) {
        $all = @($state.autoplayTasks) + $launched
        $state.autoplayTasks = @($all | Select-Object -Last $script:TR_MaxAutoplayTasks)
    }

    $state.offset = $read.offset
    $state.promptPending = $false
    Write-TurnState -Dir $dir -Session $sessionId -State $state
} catch {
    [Console]::Error.WriteLine("[timerecorder stop hook] $_")
}

exit 0

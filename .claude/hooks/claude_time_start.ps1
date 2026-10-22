# TimeRecorder - Claude Code working-time tracker (turn START).
# Hook: UserPromptSubmit. Stamps this session's activity time and accrues the
# between-turn gap (read + think + type since the last response) as working
# time, provided it is under the idle threshold. The gap is journaled as a
# "gap" span carrying the prompt it led to, and the prompt is kept in the turn
# state so the Stop hook can label the turn it opens.
# A wait that ends on the notification of an autoplay run (background command or
# Monitor the Stop hook remembered) is autoplay time: journaled with the tag
# "autoplay-wait" and dropped at once by a factor-0 correction (tr.py undo
# restores it), so watching games play is never billed.
#
# IMPORTANT: this hook must NOT write anything to stdout. For UserPromptSubmit,
# a hook's stdout is injected into the prompt context - any output would pollute
# every prompt. Only touch files; send diagnostics to stderr.

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
    $prompt = if ($payload -and $payload.prompt) { [string]$payload.prompt } else { '' }
    # A harness-submitted prompt (task notification...) is no human at the
    # keyboard: its turn is "background", the wait before it is tagged "waiting".
    $systemLabel = Get-SystemPromptLabel $prompt
    $trigger = if ($systemLabel) { 'background' } else { 'prompt' }
    if ($systemLabel) { $prompt = $systemLabel }

    $dir = Get-TimeRecorderDir

    # AI tracking paused from the Unity calendar window, or a session that is
    # not work on this project (Clawd view edit) -> accrue nothing and drop any
    # stale stamp so the gap before it is never counted.
    $state = Read-TurnState -Dir $dir -Session $sessionId
    if (-not $state.offProject -and (Test-OffProjectPrompt $prompt)) {
        $state.offProject = $true
        Write-TurnState -Dir $dir -Session $sessionId -State $state
    }
    $pauseFlag = Join-Path $dir 'ai_paused.flag'
    if ((Test-Path $pauseFlag) -or $state.offProject) {
        Clear-Activity -Dir $dir -Session $sessionId
        exit 0
    }

    # Accrue the between-turn gap (idle-gated), then stamp now as last activity.
    $acc = Update-Activity -Dir $dir -Session $sessionId -IdleGated $true

    $sessionDir = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($sessionDir)) { $sessionDir = (Get-Location).Path }
    if ($acc.seconds -gt 0) {
        $git = Get-GitContext $sessionDir
        $gapActivity = $null
        $autoplayWait = $false
        if ($systemLabel) {
            $toolUseId = Get-NotificationToolUseId ([string]$payload.prompt)
            $autoplayWait = $toolUseId -and (@($state.autoplayTasks) -contains $toolUseId)
            $gapTags = if ($autoplayWait) { @('waiting', 'autoplay-wait') } else { @('waiting') }
            $gapActivity = @{ model = ''; skills = @(); tools = @(); commands = @(); files = @(); tags = $gapTags }
        }
        $gapId = [Guid]::NewGuid().ToString('N')
        Add-IntervalRecord -Dir $dir -Json (New-IntervalRecord -Source 'claude' -Kind 'gap' -Trigger $trigger `
            -EndMs $acc.nowMs -Seconds $acc.seconds -Clamped $false -Session $sessionId -Git $git `
            -SessionDir $sessionDir -Prompt $prompt -Activity $gapActivity -Id $gapId)
        if ($autoplayWait) {
            Add-AdjustmentRecord -Dir $dir -Factor 0 -Reason 'autoplay wait, not billed (hook)' `
                -Filter 'tag=autoplay-wait' -Ids @($gapId)
        }
    }

    # Remember the prompt for the turn it opens (the Stop hook journals it).
    $state.prompt = Get-OneLine $prompt $script:TR_PromptChars
    $state.promptPending = $true
    $state.trigger = $trigger
    Write-TurnState -Dir $dir -Session $sessionId -State $state

    # Housekeeping: drop dead stamps from abandoned sessions and legacy markers.
    Remove-StaleActivity -Dir $dir -Session $sessionId
} catch {
    [Console]::Error.WriteLine("[timerecorder start hook] $_")
}

exit 0

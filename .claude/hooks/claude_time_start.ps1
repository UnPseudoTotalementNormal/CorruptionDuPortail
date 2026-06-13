# TimeRecorder - Claude Code working-time tracker (START marker)
# Hook: UserPromptSubmit. Records the turn-start timestamp, keyed by session id.
#
# IMPORTANT: this hook must NOT write anything to stdout. For UserPromptSubmit,
# a hook's stdout is injected into the prompt context - any output would pollute
# every prompt. Only touch files; send diagnostics to stderr.

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()

    $sessionId = 'default'
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $payload = $raw | ConvertFrom-Json
        if ($payload.session_id) { $sessionId = [string]$payload.session_id }
    }

    $projectDir = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }

    $dir = Join-Path $projectDir '.claude/timerecorder'
    [System.IO.Directory]::CreateDirectory($dir) | Out-Null

    $startMs = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $marker  = Join-Path $dir ("start_{0}.txt" -f $sessionId)

    [System.IO.File]::WriteAllText($marker, [string]$startMs)
} catch {
    [Console]::Error.WriteLine("[timerecorder start hook] $_")
}

exit 0

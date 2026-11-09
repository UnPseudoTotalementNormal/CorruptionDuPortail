# Media reminder - a finished visible feature proven by autoplay goes to Poyo unasked (clip + capture).
# Hook: PostToolUse (Bash | PowerShell). When a command's output shows an autoplay scenario that PASSED and was filmed
# (`VERDICT: PASS` + a `video:` line), adds a reminder to the session, once per run folder. Never blocks.
# Why: the rule lived only in memory and was forgotten (tooltip rework, 2026-11-09: « Tu m'as envoyé un clip ? »).
#
# Any failure of the hook itself stays silent: a broken reminder must never disturb work.

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
    $response = $payload.tool_response
    $text = if ($response -is [string]) { $response } else { (@($response.stdout, $response.output, $response.stderr) | Where-Object { $_ }) -join "`n" }
    if ($text -notmatch 'VERDICT:\s*PASS' -or $text -notmatch '(?m)^\s*video:\s*\S') { exit 0 }

    $run = if ($text -match 'VERDICT:\s*PASS\s*\(([^)]+)\)') { $Matches[1].Trim() } else { 'unknown' }
    $seen = Join-Path ([IO.Path]::GetTempPath()) 'cdp_media_reminder_seen.txt'
    if ((Test-Path $seen) -and ((Get-Content $seen -ErrorAction SilentlyContinue) -contains $run)) { exit 0 }
    Add-Content -Path $seen -Value $run

    $message = "Autoplay PASS filmed ($run). If this run proves a finished visible feature (not a work / tuning / " +
        "network run): BEFORE answering, send Poyo a short clip cut on the useful moment (Discord DM via Robot, it " +
        "also lands in the Clawd Medias tab) and the key capture (Medias tab send.py), one sentence each. Recipe: " +
        "autoplay skill section 5. Otherwise ignore."
    @{ hookSpecificOutput = @{ hookEventName = 'PostToolUse'; additionalContext = $message } } | ConvertTo-Json -Compress
    exit 0
} catch {
    exit 0
}

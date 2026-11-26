# Attribution guard - no Claude / AI attribution in commits or PRs (CLAUDE.md "Commits").
# Hook: PreToolUse (Bash | PowerShell). On a `git commit`, `gh pr create|edit|merge`, if the command text or the
# message file it passes (-F / --file / --body-file) carries a Claude co-author trailer or a "Generated with Claude
# Code" line, the call is blocked (exit 2, reason on stderr). The harness asked for those lines by default and the
# CLAUDE.md rule alone kept losing to that request (T26, 2026-10-27; ~7 blocks by 2026-11-09). Since 2026-11-09,
# `"attribution": {"commit": "", "pr": ""}` in .claude/settings.json turns the request off at the source; this
# hook stays as the safety net if that setting is ever lost or renamed.
#
# Any failure of the hook itself lets the call through: a broken guard must never block work.

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
    $cmd = [string]$payload.tool_input.command
    if ($cmd -notmatch '\bgit\b(\s+-C\s+\S+)?(\s+-c\s+\S+)*\s+commit\b|\bgh\s+pr\s+(create|edit|merge)\b') { exit 0 }

    # Only this repository's commits and PRs: a session here may commit to another repository (`cd <other> &&`,
    # `git -C <other>`) whose own rules ask for the attribution (the skill marketplace does).
    $repo = [string]$payload.cwd
    if ($cmd -match '\bgit\s+-C\s+("([^"]+)"|''([^'']+)''|(\S+))') {
        $repo = @($Matches[2], $Matches[3], $Matches[4]) | Where-Object { $_ } | Select-Object -First 1
    } elseif ($cmd -match '(?:^|[;&|]\s*)(?:cd|Set-Location)\s+("([^"]+)"|''([^'']+)''|([^\s;&|]+))') {
        $repo = @($Matches[2], $Matches[3], $Matches[4]) | Where-Object { $_ } | Select-Object -First 1
    }
    if ($repo -match '^/([a-zA-Z])/(.*)$') { $repo = "$($Matches[1]):/$($Matches[2])" }
    $project = $env:CLAUDE_PROJECT_DIR
    if ($repo -and $project -and (Test-Path $repo)) {
        $mine = & git -C $project rev-parse --path-format=absolute --git-common-dir 2>$null
        $target = & git -C $repo rev-parse --path-format=absolute --git-common-dir 2>$null
        if ($mine -and $target -and ([IO.Path]::GetFullPath($mine) -ne [IO.Path]::GetFullPath($target))) { exit 0 }
    }

    $pattern = '(?i)Co-Authored-By:\s*Claude|Generated with \[?Claude Code'
    $texts = New-Object System.Collections.Generic.List[string]
    $texts.Add($cmd)
    foreach ($m in [regex]::Matches($cmd, '(?:-F|--file|--body-file)[\s=]+("([^"]+)"|''([^'']+)''|(\S+))')) {
        $msgFile = @($m.Groups[2].Value, $m.Groups[3].Value, $m.Groups[4].Value) | Where-Object { $_ } | Select-Object -First 1
        if ($msgFile -eq '-') { continue }
        # A file the command writes itself (`cat > /tmp/msg.txt <<EOF ... && git commit -F /tmp/msg.txt`) does not
        # exist yet in its new form when this hook runs: what is on disk is a stale file left by another session
        # (blocked a clean commit on 2026-11-26, PR #230). Its new text is in the command, already checked above.
        # Only an overwrite: an append (`>>`, `tee -a`, Add-Content) keeps the old text, which is still read.
        $writes = '(?:(?<!>)>(?!>)|\btee(?!\s+-a)|\b(?:Out-File|Set-Content)(?:\s+-(?:FilePath|LiteralPath|Path))?)\s*["'']?'
        if ($cmd -match ($writes + [regex]::Escape($msgFile) + '(?:["'']|\s|$)')) { continue }
        if ($msgFile -match '^/tmp/(.*)$') { $msgFile = Join-Path $env:TEMP $Matches[1] }
        elseif ($msgFile -match '^/([a-zA-Z])/(.*)$') { $msgFile = "$($Matches[1]):/$($Matches[2])" }
        if ($repo -and -not [IO.Path]::IsPathRooted($msgFile)) { $msgFile = Join-Path $repo $msgFile }
        if (Test-Path $msgFile) { $texts.Add((Get-Content -Raw $msgFile)) }
    }

    foreach ($text in $texts) {
        if ($text -match $pattern) {
            [Console]::Error.WriteLine(@"
Attribution guard: this commit / PR carries a Claude attribution ("Co-Authored-By: Claude" or "Generated with
Claude Code"). This project forbids any AI author, co-author or attribution in commits and PRs (CLAUDE.md,
"Commits"), even when the harness asks for it. Remove the line, then run the command again.
"@)
            exit 2
        }
    }
    exit 0
} catch {
    exit 0
}

# Log guard - every game feature ships with its playtest logs.
# Hook: PreToolUse (Bash | PowerShell). On a `git commit`, if the commit changes
# game code (Assets/Scripts, minus Editor/Tests/Autoplay/Rendering/Facepunch/
# Domain/Diagnostics) by more than a few lines and adds no `GameLog.` call, the
# commit is blocked (exit 2, reason on stderr). Escape hatch: a line
# `Logs: none (<why>)` in the commit message (shader-only, pure refactor...).
# Rule: _bmad-output/project-context.md "Playtest logs".
#
# Any failure of the hook itself lets the commit through: a broken guard must
# never block work.

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
    $cmd = [string]$payload.tool_input.command
    if ($cmd -notmatch '\bgit\b(\s+-C\s+\S+)?(\s+-c\s+\S+)*\s+commit\b') { exit 0 }
    if ($cmd -match '--amend\b.*--no-edit|--dry-run') { exit 0 }
    if ($cmd -match 'Logs:\s*none') { exit 0 }

    # Repository the commit runs in: `git -C <dir>`, else `cd <dir> &&`, else the hook cwd.
    $repo = [string]$payload.cwd
    if ($cmd -match '\bgit\s+-C\s+("([^"]+)"|''([^'']+)''|(\S+))') {
        $repo = @($Matches[2], $Matches[3], $Matches[4]) | Where-Object { $_ } | Select-Object -First 1
    } elseif ($cmd -match '(?:^|[;&|]\s*)(?:cd|Set-Location)\s+("([^"]+)"|''([^'']+)''|([^\s;&|]+))') {
        $repo = @($Matches[2], $Matches[3], $Matches[4]) | Where-Object { $_ } | Select-Object -First 1
    }
    if ($repo -match '^/([a-zA-Z])/(.*)$') { $repo = "$($Matches[1]):/$($Matches[2])" }
    if (-not $repo -or -not (Test-Path $repo)) { exit 0 }

    # Message given as a file (-F / --file): look for the escape hatch there too.
    if ($cmd -match '(?:-F|--file)[\s=]+("([^"]+)"|''([^'']+)''|(\S+))') {
        $msgFile = @($Matches[2], $Matches[3], $Matches[4]) | Where-Object { $_ } | Select-Object -First 1
        if ($msgFile -match '^/([a-zA-Z])/(.*)$') { $msgFile = "$($Matches[1]):/$($Matches[2])" }
        if (-not [IO.Path]::IsPathRooted($msgFile)) { $msgFile = Join-Path $repo $msgFile }
        if ((Test-Path $msgFile) -and ((Get-Content -Raw $msgFile) -match 'Logs:\s*none')) { exit 0 }
    }

    # `-a` / `--all` commits tracked changes too: diff against HEAD instead of the index.
    $all = $cmd -match '\scommit\b.*\s(-a\b|--all\b|-[a-zA-Z]*a[a-zA-Z]*\s)'
    $diffArgs = if ($all) { @('diff', 'HEAD') } else { @('diff', '--cached') }
    $diff = & git -C $repo @diffArgs --unified=0 -- 'Assets/Scripts/*.cs' 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $diff) { exit 0 }

    $excluded = '/(Editor|Tests?|Autoplay|Rendering|Facepunch|Domain|Diagnostics)/'
    $file = $null
    $codeLines = 0
    $logLines = 0
    $files = New-Object System.Collections.Generic.List[string]
    foreach ($line in $diff) {
        if ($line -match '^\+\+\+ b/(.+)$') {
            $file = if ($Matches[1] -match $excluded) { $null } else { $Matches[1] }
            continue
        }
        if (-not $file -or -not $line.StartsWith('+')) { continue }
        $text = $line.Substring(1).Trim()
        if ($text -eq '' -or $text -match '^(//|/\*|\*|[{}\[\]();,]+$|using\s)') { continue }
        $codeLines++
        if ($text -match 'GameLog\.') { $logLines++ }
        if (-not $files.Contains($file)) { $files.Add($file) }
    }

    if ($codeLines -lt 5 -or $logLines -gt 0) { exit 0 }

    $list = ($files | Select-Object -First 8 | ForEach-Object { "  - $_" }) -join "`n"
    [Console]::Error.WriteLine(@"
Log guard: this commit changes game code ($codeLines lines) without adding any GameLog call.
$list

Every feature ships with its playtest logs (_bmad-output/project-context.md, "Playtest logs"):
each player action with its target, each silent refusal (early return on an input) with its reason,
each step of a sequence, each server decision; GameLog.Guarded(...) when the arguments read game state.
Add them, then commit again. If this change really has nothing to log (shader, pure refactor,
visual-only), add a line "Logs: none (<why>)" to the commit message body.
"@)
    exit 2
} catch {
    exit 0
}

# FMOD guard - the FMOD project and the banks built from it are committed together.
# Hook: PreToolUse (Bash | PowerShell). On a `git commit` whose changes touch Assets/FMODBanks or FMODProject,
# runs `tools/fmod/banks.py diff` (banks vs project, event by event) on the working tree and blocks the commit
# (exit 2) if they disagree. Same check as the CI job .github/workflows/fmod-sync.yml, caught before the push.
# Why: banks were once built from a project copy never committed; rebuilding from the repo then lost the menu music.
#
# Any failure of the hook itself lets the commit through: a broken guard must never block work.

$ErrorActionPreference = 'Stop'

try {
    $raw = [Console]::In.ReadToEnd()
    if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }
    $payload = $raw | ConvertFrom-Json
    $cmd = [string]$payload.tool_input.command
    if ($cmd -notmatch '\bgit\b(\s+-C\s+\S+)?(\s+-c\s+\S+)*\s+commit\b') { exit 0 }
    if ($cmd -match '--dry-run') { exit 0 }

    # Repository the commit runs in: `git -C <dir>`, else `cd <dir> &&`, else the hook cwd.
    $repo = [string]$payload.cwd
    if ($cmd -match '\bgit\s+-C\s+("([^"]+)"|''([^'']+)''|(\S+))') {
        $repo = @($Matches[2], $Matches[3], $Matches[4]) | Where-Object { $_ } | Select-Object -First 1
    } elseif ($cmd -match '(?:^|[;&|]\s*)(?:cd|Set-Location)\s+("([^"]+)"|''([^'']+)''|([^\s;&|]+))') {
        $repo = @($Matches[2], $Matches[3], $Matches[4]) | Where-Object { $_ } | Select-Object -First 1
    }
    if ($repo -match '^/([a-zA-Z])/(.*)$') { $repo = "$($Matches[1]):/$($Matches[2])" }
    if (-not $repo -or -not (Test-Path $repo)) { exit 0 }

    $all = $cmd -match '\scommit\b.*\s(-a\b|--all\b|-[a-zA-Z]*a[a-zA-Z]*\s)'
    $diffArgs = if ($all) { @('diff', 'HEAD', '--name-only') } else { @('diff', '--cached', '--name-only') }
    $changed = & git -C $repo @diffArgs -- 'Assets/FMODBanks' 'FMODProject' 2>$null
    if ($LASTEXITCODE -ne 0 -or -not $changed) { exit 0 }

    $script = Join-Path $repo 'tools/fmod/banks.py'
    if (-not (Test-Path $script)) { exit 0 }
    $out = & python $script diff 2>&1
    if ($LASTEXITCODE -eq 0) { exit 0 }
    if ($LASTEXITCODE -ne 1) { exit 0 }  # the checker itself broke: let the commit through

    [Console]::Error.WriteLine(@"
FMOD guard: the banks in Assets/FMODBanks and the project in FMODProject/ disagree (tools/fmod/banks.py diff):
$($out -join "`n")

banks_only / renamed: the banks were built from a project that is not the committed one. project_only: the
project has events never built. Fix: build every bank from this project (tools/fmod/fmod.sh build), then commit
FMODProject/ and Assets/FMODBanks/ together. See tools/fmod/README.md.
"@)
    exit 2
} catch {
    exit 0
}

# TimeRecorder - interval journal: what each booked span of time was spent on.
# Dot-sourced by _timerecorder_common.ps1 (Claude hooks) and through it by the
# Codex hooks.
#
# Every accrual the hooks book is also appended as one JSON line to
# claude_intervals.jsonl (start/end, branch, worktree, prompt, skills, tools,
# commands, files, tags). The journal is append-only raw data: corrections go to
# claude_adjustments.jsonl (tools/timerecorder/tr.py), never into this file, so
# every booked second stays auditable. The day totals of claude_time.json keep
# being written as before; readers count a day as
#   (ledger day - raw seconds journaled that day) + union of journaled spans,
# so the days booked before the journal existed keep their totals.

$script:TR_IntervalFile = 'claude_intervals.jsonl'
# First read of a session's transcript: skip anything older than this many bytes
# so a long session joined mid-way never stalls the hook on a huge file.
$script:TR_TranscriptFirstReadBytes = 2MB
$script:TR_MaxCommands = 10
$script:TR_MaxFiles = 20
$script:TR_PromptChars = 400

# Hook stdin is UTF-8; Windows PowerShell 5.1 decodes it with the console code
# page by default, which mangles accented prompts.
function Read-HookStdin {
    try { [Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }
    return [Console]::In.ReadToEnd()
}

function Get-OneLine {
    param([string]$Text, [int]$Max)
    if ([string]::IsNullOrEmpty($Text)) { return '' }
    $t = ($Text -replace '\s+', ' ').Trim()
    if ($t.Length -gt $Max) { $t = $t.Substring(0, $Max) + '...' }
    return $t
}

# Prompts the harness submits on its own (a background task finished, a
# reminder): not typed by a human. Returns $null for a human prompt, else a
# readable one-line label ("task notification: <summary>").
function Get-SystemPromptLabel {
    param([string]$Prompt)
    if ([string]::IsNullOrWhiteSpace($Prompt)) { return $null }
    if ($Prompt -match '^\s*<task-notification>') {
        $summary = if ($Prompt -match '<summary>([\s\S]*?)</summary>') { $Matches[1] } else { 'background task' }
        return 'task notification: ' + (Get-OneLine $summary 200)
    }
    if ($Prompt -match '^\s*<system-reminder>') { return 'system reminder' }
    return $null
}

# Branch and worktree of the directory a session runs in. Reads .git directly
# (no git process: hooks must stay fast). Detached HEAD -> "detached@<sha>".
function Get-GitContext {
    param([string]$SessionDir)
    $ctx = @{ branch = ''; worktree = ''; root = $SessionDir }
    try {
        $root = [System.IO.Path]::GetFullPath($SessionDir)
        while ($root -and -not (Test-Path -LiteralPath (Join-Path $root '.git'))) {
            $root = [System.IO.Path]::GetDirectoryName($root)
        }
        if (-not $root) { return $ctx }
        $ctx.root = $root

        $dotGit = Join-Path $root '.git'
        $gitDir = $dotGit
        $ctx.worktree = 'main'
        if (Test-Path -LiteralPath $dotGit -PathType Leaf) {
            $line = ([System.IO.File]::ReadAllText($dotGit)).Trim()
            if ($line -match '^gitdir:\s*(.+)$') {
                $gitDir = $Matches[1].Trim()
                if (-not [System.IO.Path]::IsPathRooted($gitDir)) { $gitDir = Join-Path $root $gitDir }
                $ctx.worktree = [System.IO.Path]::GetFileName($root)
            }
        }

        $head = ([System.IO.File]::ReadAllText((Join-Path $gitDir 'HEAD'))).Trim()
        if ($head -match '^ref:\s*refs/heads/(.+)$') { $ctx.branch = $Matches[1] }
        elseif ($head.Length -ge 10) { $ctx.branch = 'detached@' + $head.Substring(0, 10) }
    } catch { }
    return $ctx
}

# Per-session state between hooks: transcript read offset and the prompt that
# opened the current turn (so the Stop hook knows whether a human started it).
function Get-TurnStatePath {
    param([string]$Dir, [string]$Session)
    $safe = $Session -replace '[^A-Za-z0-9._-]', '_'
    if ([string]::IsNullOrWhiteSpace($safe)) { $safe = 'default' }
    return (Join-Path $Dir ("turn_{0}.json" -f $safe))
}

function Read-TurnState {
    param([string]$Dir, [string]$Session)
    $state = @{ offset = -1; prompt = ''; promptPending = $false; trigger = 'prompt' }
    $path = Get-TurnStatePath -Dir $Dir -Session $Session
    if (-not (Test-Path -LiteralPath $path)) { return $state }
    try {
        $j = [System.IO.File]::ReadAllText($path) | ConvertFrom-Json
        if ($null -ne $j.offset) { $state.offset = [long]$j.offset }
        if ($null -ne $j.prompt) { $state.prompt = [string]$j.prompt }
        if ($null -ne $j.promptPending) { $state.promptPending = [bool]$j.promptPending }
        if ($null -ne $j.trigger) { $state.trigger = [string]$j.trigger }
    } catch { }
    return $state
}

function Write-TurnState {
    param([string]$Dir, [string]$Session, [hashtable]$State)
    $path = Get-TurnStatePath -Dir $Dir -Session $Session
    $json = ConvertTo-Json -InputObject $State -Compress
    Write-FileAtomic -Path $path -Content $json
}

# Parse the transcript lines appended since $Offset (complete lines only; a line
# still being written is left for the next read). Returns the new offset and the
# assistant entries that matter (tool calls, model).
function Read-TranscriptSince {
    param([string]$Path, [long]$Offset)
    $result = @{ offset = $Offset; entries = @() }
    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path)) { return $result }

    $fs = $null
    try {
        $fs = [System.IO.File]::Open($Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::ReadWrite)
        $len = $fs.Length
        if ($Offset -lt 0) { $Offset = [Math]::Max(0, $len - $script:TR_TranscriptFirstReadBytes) }
        if ($Offset -gt $len) { $Offset = 0 }   # transcript replaced / truncated
        $count = [int]($len - $Offset)
        if ($count -le 0) { $result.offset = $Offset; return $result }

        $buf = New-Object byte[] $count
        [void]$fs.Seek($Offset, [System.IO.SeekOrigin]::Begin)
        $read = 0
        while ($read -lt $count) {
            $n = $fs.Read($buf, $read, $count - $read)
            if ($n -le 0) { break }
            $read += $n
        }
        $lastNl = [Array]::LastIndexOf($buf, [byte]10, $read - 1)
        if ($lastNl -lt 0) { $result.offset = $Offset; return $result }
        $result.offset = $Offset + $lastNl + 1

        $text = [System.Text.Encoding]::UTF8.GetString($buf, 0, $lastNl + 1)
        $entries = New-Object System.Collections.ArrayList
        foreach ($line in $text.Split("`n")) {
            # Cheap filter before parsing: only assistant entries carry tool calls.
            if ($line.IndexOf('"assistant"') -lt 0) { continue }
            try {
                $entry = $line | ConvertFrom-Json
                if ($entry.type -eq 'assistant') { [void]$entries.Add($entry) }
            } catch { }
        }
        $result.entries = $entries.ToArray()
    } catch {
        [Console]::Error.WriteLine("[timerecorder] transcript read failed: $_")
    } finally {
        if ($null -ne $fs) { $fs.Close() }
    }
    return $result
}

function Get-RelativePath {
    param([string]$Path, [string]$Root)
    try {
        $full = [System.IO.Path]::GetFullPath($Path)
        $r = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + '\'
        if ($full.StartsWith($r, [StringComparison]::OrdinalIgnoreCase)) { return $full.Substring($r.Length).Replace('\', '/') }
        return $full.Replace('\', '/')
    } catch { return $Path }
}

# Summarize what the assistant did in a turn: skills, tool counts, notable shell
# commands, edited files, model, and coarse activity tags to filter on later
# ("autoplay", "unity-tests", "code", ...).
function Get-TurnActivity {
    param([object[]]$Entries, [string]$Root)
    $skills = New-Object System.Collections.Generic.List[string]
    $toolCounts = [ordered]@{}
    $commands = New-Object System.Collections.Generic.List[string]
    $files = New-Object System.Collections.Generic.List[string]
    $tags = New-Object System.Collections.Generic.HashSet[string]
    $model = ''

    foreach ($e in $Entries) {
        $msg = $e.message
        if ($null -eq $msg) { continue }
        if ($msg.model -and $msg.model -ne '<synthetic>') { $model = [string]$msg.model }
        foreach ($b in @($msg.content)) {
            if ($null -eq $b -or $b.type -ne 'tool_use') { continue }
            $name = [string]$b.name
            $in = $b.input
            if ($toolCounts.Contains($name)) { $toolCounts[$name]++ } else { $toolCounts[$name] = 1 }

            switch -Regex ($name) {
                '^Skill$' {
                    $s = [string]$in.skill
                    if ($s -and -not $skills.Contains($s)) { $skills.Add($s) }
                    if ($s -match 'autoplay') { [void]$tags.Add('autoplay') }
                    if ($s -match 'review') { [void]$tags.Add('review') }
                }
                '^(Bash|PowerShell|Monitor)$' {
                    $cmd = [string]$in.command
                    if (-not $cmd) { break }
                    # Running games, not merely touching tools/autoplay/ (unityctl.sh compile lives there)
                    if ($cmd -match 'unityctl\.sh\s+(play-build|play-net|last-run)|run_scenario|launch-background\.ps1|-autoplay(-|\s|$)') { [void]$tags.Add('autoplay') }
                    if ($cmd -match 'unityctl\.sh\s+(editmode|playmode)|run_tests') { [void]$tags.Add('unity-tests') }
                    if ($cmd -match 'unityctl\.sh\s+build|unity(\.exe)?\s+build|-buildTarget') { [void]$tags.Add('unity-build') }
                    if ($cmd -match 'Unity\.exe|unity\s+command|unityctl') { [void]$tags.Add('unity-editor') }
                    if ($cmd -match '(^|[;&|(]\s*)git\s|\bgh\s+(pr|api|run|issue)') { [void]$tags.Add('git') }
                    if ($cmd -match 'discord\.com/api') { [void]$tags.Add('discord') }
                    if ($commands.Count -lt $script:TR_MaxCommands) { $commands.Add((Get-OneLine $cmd 160)) }
                }
                '^(Edit|Write|NotebookEdit)$' {
                    $p = [string]$in.file_path
                    if (-not $p) { $p = [string]$in.notebook_path }
                    if (-not $p) { break }
                    $rel = Get-RelativePath $p $Root
                    if (-not $files.Contains($rel) -and $files.Count -lt $script:TR_MaxFiles) { $files.Add($rel) }
                    switch -Regex ($p) {
                        '\.cs$'                    { [void]$tags.Add('code') }
                        '\.(uxml|uss)$'            { [void]$tags.Add('ui') }
                        '\.md$'                    { [void]$tags.Add('docs') }
                        '\.(ps1|sh|py|ya?ml|json)$' { [void]$tags.Add('tooling') }
                        '\.(unity|prefab|asset)$'  { [void]$tags.Add('assets') }
                    }
                }
                '^mcp__UnityMCP__' {
                    [void]$tags.Add('unity-editor')
                    if ($name -match 'run_tests|get_test_job') { [void]$tags.Add('unity-tests') }
                }
                '^(Agent|Task)$'            { [void]$tags.Add('subagents') }
                '^(WebSearch|WebFetch)$'    { [void]$tags.Add('web') }
            }
        }
    }

    if ($toolCounts.Count -eq 0) { [void]$tags.Add('chat') }
    elseif ($files.Count -eq 0 -and -not ($tags.Contains('autoplay') -or $tags.Contains('unity-tests') -or $tags.Contains('unity-build') -or $tags.Contains('git'))) {
        [void]$tags.Add('research')
    }

    $tools = foreach ($k in $toolCounts.Keys) { "{0}:{1}" -f $k, $toolCounts[$k] }
    return @{
        model    = $model
        skills   = @($skills)
        tools    = @($tools)
        commands = @($commands)
        files    = @($files)
        tags     = @($tags | Sort-Object)
    }
}

# Build one journal line. Times are Unix ms; the span covers the booked seconds
# only (a clamped turn shows its last $Seconds, flagged clamped).
function New-IntervalRecord {
    param(
        [string]$Source, [string]$Kind, [string]$Trigger, [long]$EndMs, [int]$Seconds, [bool]$Clamped,
        [string]$Session, [hashtable]$Git, [string]$SessionDir, [string]$Prompt, [hashtable]$Activity
    )
    if ($null -eq $Activity) { $Activity = @{ model = ''; skills = @(); tools = @(); commands = @(); files = @(); tags = @() } }
    $rec = [ordered]@{
        v        = 1
        id       = [Guid]::NewGuid().ToString('N')
        src      = $Source
        kind     = $Kind
        trigger  = $Trigger
        startMs  = $EndMs - ([long]$Seconds * 1000)
        endMs    = $EndMs
        sec      = $Seconds
        clamped  = $Clamped
        session  = $Session
        branch   = [string]$Git.branch
        worktree = [string]$Git.worktree
        cwd      = $SessionDir
        prompt   = (Get-OneLine $Prompt $script:TR_PromptChars)
        model    = [string]$Activity.model
        skills   = @($Activity.skills)
        tags     = @($Activity.tags)
        tools    = @($Activity.tools)
        commands = @($Activity.commands)
        files    = @($Activity.files)
    }
    return (ConvertTo-Json -InputObject $rec -Compress -Depth 3)
}

# Append a journal line under the ledger lock (flushing queued lines first). If
# the lock can't be had, queue it to a per-GUID pending file: never lost, never
# blocking the hook.
function Add-IntervalRecord {
    param([string]$Dir, [string]$Json)
    if ([string]::IsNullOrWhiteSpace($Json)) { return }
    $fs = Get-LedgerLock -Dir $Dir
    if ($null -eq $fs) {
        [System.IO.File]::WriteAllText((Join-Path $Dir ("pendingiv_{0}.jsonl" -f [Guid]::NewGuid().ToString('N'))), $Json + "`n", (New-Object System.Text.UTF8Encoding($false)))
        return
    }
    try {
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        $sb = New-Object System.Text.StringBuilder
        $queued = @(Get-ChildItem -Path $Dir -File -Filter 'pendingiv_*.jsonl' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime)
        foreach ($q in $queued) {
            try { [void]$sb.Append([System.IO.File]::ReadAllText($q.FullName, $utf8)) } catch { }
        }
        [void]$sb.Append($Json + "`n")
        [System.IO.File]::AppendAllText((Join-Path $Dir $script:TR_IntervalFile), $sb.ToString(), $utf8)
        foreach ($q in $queued) { Remove-Item -LiteralPath $q.FullName -Force -ErrorAction SilentlyContinue }
    } catch {
        [Console]::Error.WriteLine("[timerecorder] interval append failed, queuing: $_")
        [System.IO.File]::WriteAllText((Join-Path $Dir ("pendingiv_{0}.jsonl" -f [Guid]::NewGuid().ToString('N'))), $Json + "`n", (New-Object System.Text.UTF8Encoding($false)))
    } finally {
        $fs.Close()
    }
}

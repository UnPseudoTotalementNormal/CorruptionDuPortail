# TimeRecorder - Claude Code working-time tracker (STOP / accumulate)
# Hook: Stop. Computes the elapsed time since the matching start marker and adds it
# to the per-day Claude ledger that the Unity TimeRecorder calendar reads.
#
# Day bucket = local date at turn end. The ledger JSON is owned entirely by these
# hooks (machine-local, gitignored); Unity only reads it.

$ErrorActionPreference = 'Stop'

# A single turn longer than this is clamped - protects the ledger against idle
# permission waits or a session left open for hours. Edit to taste.
$MaxTurnSeconds = 14400   # 4h

try {
    $raw = [Console]::In.ReadToEnd()

    $sessionId = 'default'
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
        $payload = $raw | ConvertFrom-Json
        if ($payload.session_id) { $sessionId = [string]$payload.session_id }
    }

    $projectDir = $env:CLAUDE_PROJECT_DIR
    if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }

    $dir    = Join-Path $projectDir '.claude/timerecorder'
    $marker = Join-Path $dir ("start_{0}.txt" -f $sessionId)

    # No start recorded (e.g. hooks were added mid-turn) -> nothing to do
    if (-not (Test-Path $marker)) { exit 0 }

    $startMs = [long]([System.IO.File]::ReadAllText($marker).Trim())
    Remove-Item -Path $marker -Force -ErrorAction SilentlyContinue

    $nowMs   = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
    $seconds = [int][Math]::Floor(($nowMs - $startMs) / 1000.0)

    if ($seconds -le 0) { exit 0 }
    if ($seconds -gt $MaxTurnSeconds) { $seconds = $MaxTurnSeconds }

    $now = Get-Date
    $y = $now.Year; $mo = $now.Month; $d = $now.Day

    $ledgerPath = Join-Path $dir 'claude_time.json'

    # Load existing entries into a hashtable keyed by "y-m-d"
    $byKey = @{}
    if (Test-Path $ledgerPath) {
        $existing = [System.IO.File]::ReadAllText($ledgerPath)
        if (-not [string]::IsNullOrWhiteSpace($existing)) {
            $ledger = $existing | ConvertFrom-Json
            foreach ($e in @($ledger.days)) {
                if ($null -eq $e) { continue }
                $byKey[("{0}-{1}-{2}" -f [int]$e.year, [int]$e.month, [int]$e.day)] = [int]$e.seconds
            }
        }
    }

    $key = "{0}-{1}-{2}" -f $y, $mo, $d
    if ($byKey.ContainsKey($key)) { $byKey[$key] += $seconds } else { $byKey[$key] = $seconds }

    # Build JSON by hand so a single entry still serializes as an array (avoids the
    # PowerShell ConvertTo-Json single-element-collapses-to-object quirk).
    $parts = foreach ($k in $byKey.Keys) {
        $kp = $k.Split('-')
        '{{"year":{0},"month":{1},"day":{2},"seconds":{3}}}' -f [int]$kp[0], [int]$kp[1], [int]$kp[2], [int]$byKey[$k]
    }

    $total = 0
    foreach ($v in $byKey.Values) { $total += [int]$v }

    $json = '{"days":[' + ($parts -join ',') + '],"totalSeconds":' + $total + '}'
    [System.IO.File]::WriteAllText($ledgerPath, $json)
} catch {
    [Console]::Error.WriteLine("[timerecorder stop hook] $_")
}

exit 0

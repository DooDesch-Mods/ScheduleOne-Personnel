# Checks that Personnel NPCs no longer cancel their own path requests every frame and that contact-only NPCs
# (spawn.physical false) run no schedule. Starts the slot with this worktree's Debug build and save 5, waits until the
# save is loaded and the world has settled, then measures over a window:
#   - failed walks of Personnel NPCs per minute, by result, from the "[dev] walk results" lines the Debug build
#     writes to this slot's Latest.log every 30 s. Interrupted is the self-cancel loop and must stay low.
#     Partial means the destination is off the reachable NavMesh: a pack problem, listed per NPC, not a failure here.
#   - vanilla "NPC is not on navmesh" and "NPC walk to location not successful" per minute from Player.log, only
#     when this slot owns Player.log for the whole window (the file is shared; the game started last takes it over)
#   - the contact-only lines and the path request guard in Latest.log
#   - whether physical Personnel NPCs move (two "personnel npcs" snapshots)
#   - [ERROR] lines in Latest.log; with -BaselineLog only lines that are not in the baseline count as new
#
#     pwsh -File tests/walk_loop_check.ps1                        # first free slot, builds and starts it
#     pwsh -File tests/walk_loop_check.ps1 -Slot mptest5 -NoStart # game already in save 5
#     pwsh -File tests/walk_loop_check.ps1 -StackProfile main     # mods like the real install
#     pwsh -File tests/walk_loop_check.ps1 -NoStart -Slot mptest5 -BaselineLog <Latest.log of a run without the fix>
#
# Needs packs with scheduled NPCs in UserData/Personnel/Packs of the slot (for example More NPCs). Prints PASS, FAIL
# or SKIP per check and exits 1 when any check fails. The game keeps running.
param(
    [string]$Slot = '',
    [switch]$NoStart,
    [string]$StackProfile = '',
    [int]$SettleSeconds = 60,
    [int]$WindowSeconds = 120,
    [double]$MaxInterruptedPerMinute = 100,
    [double]$MaxOffMeshPerMinute = 500,
    [string]$BaselineLog = ''
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$playerLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\TVGS\Schedule I\Player.log'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

# The game keeps its logs open for writing, so they are read with shared read/write access.
function Read-Shared([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return '' }
    $stream = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try { return (New-Object System.IO.StreamReader($stream)).ReadToEnd() } finally { $stream.Dispose() }
}

function Count-Text([string]$path, [string]$needle) {
    return ([regex]::Matches((Read-Shared $path), [regex]::Escape($needle))).Count
}

function Latest-Lines { return @((Read-Shared $latest) -split "`r?`n") }

# Physical Personnel NPCs and their positions, from "personnel npcs".
function Positions {
    $out = (& python $s1 --slot $Slot run 'personnel npcs' 2>&1 | Out-String)
    $map = @{}
    foreach ($m in [regex]::Matches($out, '(?m)^\s*.*?\s(\S+) \([^)]*\) physical.*? at (-?[\d.]+), (-?[\d.]+), (-?[\d.]+)')) {
        $map[$m.Groups[1].Value] = @([double]$m.Groups[2].Value, [double]$m.Groups[3].Value, [double]$m.Groups[4].Value)
    }
    return $map
}

function Error-Lines([string[]]$lines) {
    return @($lines | Where-Object { $_ -match '\[ERROR\]' } |
        ForEach-Object { ($_ -replace '^\[[^\]]*\]\s*', '').Trim() } | Sort-Object -Unique)
}

$acqArgs = @('-I', $slotsPy, '--json', 'acquire', '--mod', 'Personnel')
if ($Slot) { $acqArgs += @('--slot', $Slot) }
$lease = (& python @acqArgs | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $lease) { throw "slots.py acquire failed (exit $LASTEXITCODE)" }
$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
if (-not $Slot) { $Slot = @($lease.lease.slots)[0] }
if (-not $Slot) { $Slot = @($lease.slots)[0] }
if (-not $Slot) { throw "no slot in the lease: $($lease | ConvertTo-Json -Depth 5 -Compress)" }
$gameRoot = $table.$Slot.root
Write-Host "slot $Slot -> $gameRoot"
$latest = Join-Path $gameRoot 'MelonLoader\Latest.log'

if (-not $NoStart) {
    $sessionArgs = @('-File', $session, '-Slot', $Slot, '-Build', (Split-Path $repo -Leaf), '-Save', '5', '-NoApp')
    if ($StackProfile) { $sessionArgs += @('-Profile', $StackProfile) }
    & pwsh @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}
& python $s1 --slot $Slot wait-ready --timeout 900
if ($LASTEXITCODE -ne 0) { throw "the save did not finish loading (wait-ready exit $LASTEXITCODE)" }
Write-Host "save loaded, settling for $SettleSeconds s"
Start-Sleep -Seconds $SettleSeconds

function Owner {
    $line = @((Read-Shared $playerLog) -split "`r?`n" | Select-Object -First 80 | Where-Object { $_ -match 'Discovering subsystems at path' })[0]
    return [string]$line
}
$rootFwd = ($gameRoot -replace '\\', '/')

$walk = 'NPC walk to location not successful'
$offMesh = 'NPC is not on navmesh'
$owner0 = Owner
$w0 = Count-Text $playerLog $walk; $o0 = Count-Text $playerLog $offMesh
$start = (Latest-Lines).Count
$t0 = Get-Date
$p0 = Positions
Start-Sleep -Seconds $WindowSeconds
$p1 = Positions
$w1 = Count-Text $playerLog $walk; $o1 = Count-Text $playerLog $offMesh
$owner1 = Owner
$mins = ((Get-Date) - $t0).TotalMinutes
$all = Latest-Lines
$window = @($all | Select-Object -Skip $start)

# Failed walks of Personnel NPCs, from the Debug build's 30 s reports inside the window.
$reports = @($window | Where-Object { $_ -match '\[dev\] walk results \(\d+ s\): (\d+) not successful' })
$byResult = @{}
$reportedTotal = 0
foreach ($r in $reports) {
    $m = [regex]::Match($r, ': (\d+) not successful(?: \(([^)]*)\))?')
    $reportedTotal += [int]$m.Groups[1].Value
    foreach ($part in ($m.Groups[2].Value -split ', ' | Where-Object { $_ })) {
        $kv = $part -split ' '
        $byResult[$kv[0]] = [int]$byResult[$kv[0]] + [int]$kv[1]
    }
}
$reportMins = [math]::Max($reports.Count * 0.5, 0.5)
Write-Host ("  walk reports in the window: {0}, failed walks {1} ({2:N0} per minute)" -f $reports.Count, $reportedTotal, ($reportedTotal / $reportMins))
foreach ($k in $byResult.Keys) { Write-Host ("    {0}: {1} ({2:N0} per minute)" -f $k, $byResult[$k], ($byResult[$k] / $reportMins)) }
Report 'walk-reports-present' ($reports.Count -ge 2) "$($reports.Count) '[dev] walk results' line(s) in the window - is the slot running the Debug build of this worktree?"

# Walks that keep ending short of their destination (Partial, Failed): the target lies off the reachable NavMesh.
# That is the pack's coordinate, so the NPCs are named here for the pack author and do not fail the check. The
# step re-requests right after each short end, and some of those requests end as Interrupted; they belong to the
# same loop and count with it. Each 30 s report is read on its own, because a pair only counts as a loop where it
# ended short in that report.
$loops = @{}
$interruptedInLoops = 0
$block = @()
foreach ($l in ($window + '[dev] walk results (end)')) {
    if ($l -match '\[dev\] walk results \(') {
        $short = @{}
        foreach ($e in $block) { if ($e.Result -ne 'Interrupted') { $short["$($e.Id) $($e.To)"] = $true } }
        foreach ($e in $block) {
            if (-not $short.ContainsKey("$($e.Id) $($e.To)")) { continue }
            if ($e.Result -eq 'Interrupted') { $interruptedInLoops += $e.N; continue }
            $key = "$($e.Id) $($e.Result) to $($e.To), $($e.Gap) m away"
            $loops[$key] = [int]$loops[$key] + $e.N
        }
        $block = @()
        continue
    }
    $m = [regex]::Match($l, "\[dev\]\s+walk x(\d+) '([^']+)' (\w+) to (\([^)]*\)), ([\d.]+) m away")
    if ($m.Success) {
        $block += [pscustomobject]@{ N = [int]$m.Groups[1].Value; Id = $m.Groups[2].Value; Result = $m.Groups[3].Value; To = $m.Groups[4].Value; Gap = $m.Groups[5].Value }
    }
}
$interruptedOther = [int]$byResult['Interrupted'] - $interruptedInLoops
$interruptedRate = $interruptedOther / $reportMins
Write-Host ("  Interrupted inside short-ending loops: {0}, elsewhere: {1}" -f $interruptedInLoops, $interruptedOther)
Report 'interrupted-rate' ($interruptedRate -le $MaxInterruptedPerMinute) ("{0:N0} Interrupted per minute outside short-ending loops, limit {1}" -f $interruptedRate, $MaxInterruptedPerMinute)
foreach ($kv in ($loops.GetEnumerator() | Sort-Object Value -Descending | Select-Object -First 10)) {
    Write-Host ("  pack finding: {0} x{1} ({2:N0} per minute)" -f $kv.Key, $kv.Value, ($kv.Value / $reportMins))
}

$owned = $owner0.Contains($rootFwd) -and $owner1.Contains($rootFwd) -and $w1 -ge $w0
if ($owned) {
    $walkRate = ($w1 - $w0) / $mins
    $offRate = ($o1 - $o0) / $mins
    Write-Host ("  Player.log '{0}': {1:N0} per minute (all NPCs)" -f $walk, $walkRate)
    Write-Host ("  Player.log '{0}': {1:N0} per minute" -f $offMesh, $offRate)
    Report 'off-navmesh-rate' ($offRate -le $MaxOffMeshPerMinute) ("{0:N0} per minute, limit {1}" -f $offRate, $MaxOffMeshPerMinute)
} else {
    Write-Host "SKIP off-navmesh-rate - Player.log was not this slot's for the whole window (start: $owner0, end: $owner1)"
}

$lines = @($all | Where-Object { $_ -match '\[Personnel\]' })
$contactOnly = @($lines | Where-Object { $_ -match 'contact-only \(spawn\.physical false\) - \d+ schedule step\(s\) not run' })
$guardFailed = @($lines | Where-Object { $_ -match 'Path request guard failed|walk result count failed' })
$kept = @($lines | Where-Object { $_ -match "kept pending path request|Path requests: kept" })
Write-Host "  contact-only lines: $($contactOnly.Count), guard lines: $($kept.Count), guard failures: $($guardFailed.Count)"
if ($contactOnly.Count -gt 0) { Write-Host "  $($contactOnly[0].Trim())" }
if ($kept.Count -gt 0) { Write-Host "  $($kept[0].Trim())" }
Report 'contact-only-logged' ($contactOnly.Count -gt 0) 'no contact-only line - the slot has no contact-only NPC with a schedule, or the fix did not run'
Report 'guard-no-failure' ($guardFailed.Count -eq 0) "$($guardFailed.Count) failure line(s): $(@($guardFailed)[0])"

$moved = @($p1.Keys | Where-Object { $p0.ContainsKey($_) } | Where-Object {
    $a = $p0[$_]; $b = $p1[$_]
    [math]::Sqrt([math]::Pow($a[0] - $b[0], 2) + [math]::Pow($a[2] - $b[2], 2)) -gt 1.0
})
Write-Host "  physical NPCs with a position: $($p1.Count), moved more than 1 m in the window: $($moved.Count)"
Report 'physical-npcs-move' ($moved.Count -gt 0) "none of $($p1.Count) physical NPCs moved"

$errors = Error-Lines $all
$personnelErrors = @($errors | Where-Object { $_ -match '\[Personnel\]' })
Report 'no-personnel-errors' ($personnelErrors.Count -eq 0) "$($personnelErrors.Count) line(s): $(@($personnelErrors)[0])"
if ($BaselineLog) {
    $before = Error-Lines (@((Read-Shared $BaselineLog) -split "`r?`n"))
    $new = @($errors | Where-Object { $before -notcontains $_ })
    foreach ($e in $new) { Write-Host "  new: $e" }
    Report 'no-new-errors' ($new.Count -eq 0) "$($new.Count) error line(s) not in the baseline"
} else {
    Write-Host "  $($errors.Count) distinct [ERROR] line(s) in Latest.log (no baseline given)"
}

if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed"; exit 1 }
Write-Host 'all checks passed'

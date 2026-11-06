# Checks the start state of pack customers whose connections do not all exist in the game. Personnel starts a
# customer with relationships.connections locked; vanilla opens a locked customer only through an unlocked
# connection, so a customer whose connection ids are not NPCs of this game would stay locked for good. After the
# save loads, Personnel unlocks such a customer and logs a warning with the missing ids.
#
# The probe pack holds four customers, all with the same routine in Northtown:
#   Gus Ghost   connections: [ghost id]               -> unlocked, warning names the ghost id
#   Kim Kyle    connections: [Connection]             -> stays locked, one live connection (control)
#   Lia Locked  connections: [ghost id], unlocked:false -> stays locked as the pack asks, warning says so
#   Nel None    no connections                        -> unlocked (rule since 2.2.2)
#
#     pwsh -File tests/unresolved_connections_check.ps1                    # first free slot, builds and starts it
#     pwsh -File tests/unresolved_connections_check.ps1 -Profile main      # mods as on the real installation
#     pwsh -File tests/unresolved_connections_check.ps1 -Build Personnel   # another checkout (the old build)
#     pwsh -File tests/unresolved_connections_check.ps1 -NoStart -Slot mptest4   # game already running with the pack
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The game keeps running.
param(
    [string]$Slot = '',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [switch]$NoStart,
    [int]$Save = 5,
    # Checkout folder to build; the default is the one this script lives in.
    [string]$Build = '',
    # A vanilla customer in the same region as the probe routine.
    [string]$Connection = 'kyle_cooley',
    # An id no NPC in the game has.
    [string]$Ghost = 'ghost_nobody_here'
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
if (-not $Build) { $Build = Split-Path $repo -Leaf }
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$script:failed = 0
$pack = 'linkprobe'
$gus = "${pack}_gus_ghost"; $kim = "${pack}_kim_kyle"; $lia = "${pack}_lia_locked"; $nel = "${pack}_nel_none"

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function ReadLogFrom([long]$offset) {
    $fs = [IO.File]::Open($script:log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

# The probe's own lines are read from the game log; the bridge's run output carries unrelated warnings.
function Dump([string]$npc) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run personnel cust $npc dump *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for 'cust $npc dump'" }
    $lines = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match "\[Personnel\] cust " })
    foreach ($l in $lines) { Write-Host "  $($l.Trim())" }
    return ($lines -join "`n")
}
function Field([string]$text, [string]$key) { return [regex]::Match($text, "(?<=$key=)\S*").Value }

$acqArgs = @('-I', $slotsPy, '--json', 'acquire', '--mod', 'Personnel')
if ($Slot) { $acqArgs += @('--slot', $Slot) }
$lease = (& python @acqArgs | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $lease) { throw "slots.py acquire failed (exit $LASTEXITCODE)" }
if (-not $Slot) { $Slot = @($lease.lease.slots)[0] }
if (-not $Slot) { throw "no slot in the lease: $($lease | ConvertTo-Json -Depth 5 -Compress)" }
$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
$gameRoot = $table.$Slot.root
$script:log = Join-Path $gameRoot 'MelonLoader\Latest.log'
Write-Host "slot $Slot -> $gameRoot, build $Build, profile $Profile"

if (-not $NoStart) {
    $dir = Join-Path $gameRoot 'UserData\Personnel\Packs\LinkProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $cust = '"customer": { "spending": { "min": 200, "max": 500 }, "ordersPerWeek": { "min": 1, "max": 5 }, "standards": "Moderate", "allowDirectApproach": true }'
    $route = '"schedule": [ { "type": "walkTo", "time": "06:00", "position": [-64.25, 1.07, 84.49] } ]'
    $json = @"
{ "schemaVersion": 3, "name": "Link Probe", "packId": "$pack", "autoRegister": true,
  "npcs": [
    { "name": "Gus Ghost", "appearance": { "height": 1.0 }, $cust, $route,
      "relationships": { "delta": 1, "unlockType": "Recommendation", "connections": [ "$Ghost" ] } },
    { "name": "Kim Kyle", "appearance": { "height": 1.0 }, $cust, $route,
      "relationships": { "delta": 1, "unlockType": "Recommendation", "connections": [ "$Connection" ] } },
    { "name": "Lia Locked", "appearance": { "height": 1.0 }, $cust, $route,
      "relationships": { "delta": 1, "unlocked": false, "unlockType": "Recommendation", "connections": [ "$Ghost" ] } },
    { "name": "Nel None", "appearance": { "height": 1.0 }, $cust, $route,
      "relationships": { "delta": 1, "unlockType": "Recommendation" } } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
    $sessionArgs = @('-Slot', $Slot, '-Profile', $Profile, '-Build', $Build, '-NoApp', '-Save', "$Save")
    if ($Profile -eq 'stack') { $sessionArgs += @('-Mods', 'Personnel') }
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}
& python -I $s1 --slot $Slot wait-ready --timeout 600 | Out-Host
Report 'save loaded' ($LASTEXITCODE -eq 0) 'wait-ready failed'

# Each pack customer is checked a few seconds after it spawned; an empty list is read four times, 2 s apart.
$deadline = (Get-Date).AddSeconds(60)
$text = ''
while ((Get-Date) -lt $deadline) {
    $text = ReadLogFrom 0
    if ($text -match "\[unlock\] 'Gus Ghost'" -and $text -match "\[unlock\] 'Kim Kyle'" -and $text -match "\[unlock\] 'Lia Locked'") { break }
    Start-Sleep -Seconds 2
}
$gusLine = [regex]::Match($text, "\[unlock\] 'Gus Ghost'[^\r\n]*").Value
Report 'guard ran after the load' ([bool]$gusLine) "no [unlock] line for Gus Ghost in Latest.log"
Report 'warning names Gus Ghost and the missing id' ($gusLine -match [regex]::Escape($Ghost) -and $gusLine -match '- unlocked') "line: '$gusLine'"
$liaLine = [regex]::Match($text, "\[unlock\] 'Lia Locked'[^\r\n]*").Value
Report 'warning says Lia Locked stays locked by the pack' ($liaLine -match 'Left locked') "line: '$liaLine'"
$kimLine = [regex]::Match($text, "\[unlock\] 'Kim Kyle'[^\r\n]*").Value
Report 'Kim Kyle is reported with its connection and stays locked' ($kimLine -match 'connection\(s\) in the game - stays locked') "line: '$kimLine'"
$wrong = @([regex]::Matches($text, "\[unlock\] [^\r\n]*the game dropped [^\r\n]*- unlocked[^\r\n]*") | ForEach-Object Value)
Report 'no customer with a real connection was unlocked' ($wrong.Count -eq 0) "lines: $($wrong -join ' | ')"
foreach ($l in @($gusLine, $kimLine, $liaLine)) { if ($l) { Write-Host "  $l" } }

$d = Dump $gus
Report 'Gus Ghost (ghost connection) is unlocked' ((Field $d 'relUnlocked') -eq 'True') "relUnlocked=$(Field $d 'relUnlocked')"
Report 'Gus Ghost is listed as an unlocked customer' ((Field $d 'inUnlocked') -eq 'True') "inUnlocked=$(Field $d 'inUnlocked')"
$d = Dump $kim
Report 'Kim Kyle (real connection) stays locked' ((Field $d 'relUnlocked') -eq 'False') "relUnlocked=$(Field $d 'relUnlocked')"
Report 'Kim Kyle has its connection' ([int]("0" + (Field $d 'connections')) -ge 1) "connections=$(Field $d 'connections')"
$d = Dump $lia
Report 'Lia Locked (unlocked:false) stays locked' ((Field $d 'relUnlocked') -eq 'False') "relUnlocked=$(Field $d 'relUnlocked')"
$d = Dump $nel
Report 'Nel None (no connections) is unlocked' ((Field $d 'relUnlocked') -eq 'True') "relUnlocked=$(Field $d 'relUnlocked')"

$dll = Join-Path $gameRoot 'Mods\Personnel.dll'
if (Test-Path -LiteralPath $dll) { Write-Host "  Personnel.dll SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)" }
Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

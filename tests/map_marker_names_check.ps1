# Checks that the phone-map markers of Personnel NPCs carry the NPC's name. A marker without a name shows the
# game's placeholder "PoI Main Text" when the player hovers it. The script writes a probe pack with one NPC
# (Mara Marker) that walks to a spot in town, starts the slot, opens the phone map and reads the label text of
# every map marker through the bridge (ui_find), which works with Release and Debug builds alike. With a Debug
# build it also reads the markers through "personnel map" and takes a screenshot with every NPC label shown.
#
#     pwsh -File tests/map_marker_names_check.ps1                        # first free slot, builds this worktree
#     pwsh -File tests/map_marker_names_check.ps1 -Profile main -NoBuild # mods exactly as on the real installation
#     pwsh -File tests/map_marker_names_check.ps1 -NoStart -Slot mptest4 # game already running with the probe pack
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The game keeps running.
param(
    [string]$Slot = '',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [switch]$NoStart,
    # Keep the Personnel.dll the profile brings (for the main profile: the one on the real installation).
    [switch]$NoBuild,
    [int]$Save = 5,
    # Where the screenshot is copied to; empty = only the path the bridge reports.
    [string]$ShotDir = '',
    [string]$ProbeName = 'Mara Marker'
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$call = Join-Path $PSScriptRoot 'bridge_call.py'
$placeholder = 'PoI Main Text'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function ReadLogFrom([long]$offset) {
    $fs = [IO.File]::Open($script:log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

# Raw bridge command; returns the parsed answer, or $null when the bridge refused or did not answer.
function Bridge([string]$command, [string]$argsJson = '{}', [int]$timeoutMs = 12000) {
    $out = (& python -I $call --slot $Slot $command $argsJson --timeout $timeoutMs 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { Write-Host "  bridge $command -> $out"; return $null }
    return ($out | ConvertFrom-Json)
}

# Console command through the bridge; returns the [map-probe] lines it wrote to the game log.
function MapProbe([string]$sub) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run personnel map $sub *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for 'personnel map $sub'" }
    $lines = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match '\[map-probe\] ' })
    foreach ($l in $lines) { Write-Host "  $($l.Trim())" }
    return ($lines -join "`n")
}

$acqArgs = @('-I', $slotsPy, '--json', 'acquire', '--mod', 'Personnel')
if ($Slot) { $acqArgs += @('--slot', $Slot) }
$lease = (& python @acqArgs | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $lease) { throw "slots.py acquire failed (exit $LASTEXITCODE)" }
if (-not $Slot) { $Slot = @($lease.lease.slots)[0] }
if (-not $Slot) { throw "no slot in the lease: $($lease | ConvertTo-Json -Depth 5 -Compress)" }
$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
$gameRoot = $table.$Slot.root
$script:log = Join-Path $gameRoot 'MelonLoader\Latest.log'
Write-Host "slot $Slot -> $gameRoot"

if (-not $NoStart) {
    # One plain NPC with a routine, so it spawns in town and gets a map marker.
    $dir = Join-Path $gameRoot 'UserData\Personnel\Packs\MapProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $json = @"
{ "schemaVersion": 3, "name": "Map Probe", "packId": "mapprobe", "autoRegister": true,
  "npcs": [
    { "name": "$ProbeName", "appearance": { "height": 1.0 },
      "schedule": [ { "type": "walkTo", "time": "06:00", "position": [-64.25, 1.07, 84.49] } ] } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
    $sessionArgs = @('-Slot', $Slot, '-Profile', $Profile, '-NoApp', '-Save', "$Save")
    if (-not $NoBuild) { $sessionArgs += @('-Build', (Split-Path $repo -Leaf)) }
    if ($Profile -eq 'stack') { $sessionArgs += @('-Mods', 'Personnel') }
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}

$dll = Join-Path $gameRoot 'Mods\Personnel.dll'
if (Test-Path -LiteralPath $dll) {
    $ver = (Get-Item -LiteralPath $dll).VersionInfo.FileVersion
    Write-Host "  Personnel.dll $ver SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)"
}

# 1. Stand next to the probe NPC, so its marker is on the part of the map the phone shows.
#    "personnel npcs" is player tooling and exists in Release builds too.
$before = (Get-Item -LiteralPath $script:log).Length
& python -I $s1 --slot $Slot run personnel npcs mapprobe *> $null
$npcLine = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match 'mapprobe_\S+ .* at ' }) | Select-Object -Last 1
$m = [regex]::Match("$npcLine", ' at (-?[\d.]+), (-?[\d.]+), (-?[\d.]+)')
Report 'probe NPC is in town' $m.Success "no live position for the mapprobe NPC: '$npcLine'"
if ($m.Success) {
    $x = [double]$m.Groups[1].Value + 2; $y = [double]$m.Groups[2].Value; $z = [double]$m.Groups[3].Value
    & python -I $s1 --slot $Slot tp $x $y $z | Out-Host
    Start-Sleep -Seconds 2
}

# 2. Phone up, map app open: the map builds the marker UI and keeps it positioned.
Report 'phone raised' ($null -ne (Bridge 'phone' '{"open": true}')) 'phone did not open'
Start-Sleep -Seconds 1
$app = Bridge 'ui_click' '{"query": "Map"}'
Report 'map app opened' ($null -ne $app -and $app.result.clicked) "ui_click Map: $($app | ConvertTo-Json -Compress -Depth 4)"
Start-Sleep -Seconds 3

# 3. The label text of every NPC marker on screen (ui_find also reads the hidden hover labels).
$found = Bridge 'ui_find' '{"query": "NPCPoI"}'
$labels = @()
if ($found) { $labels = @($found.result.elements | ForEach-Object { "$($_.label)" }) }
Write-Host "  $($labels.Count) NPC marker(s) on screen:"
foreach ($l in $labels) { Write-Host "    '$($l -replace "`n", '\n')'" }
Report 'map shows NPC markers' ($labels.Count -gt 0) 'ui_find found no NPCPoI on screen'
$bad = @($labels | Where-Object { $_ -match [regex]::Escape($placeholder) })
Report "no NPC marker reads '$placeholder'" ($bad.Count -eq 0) "$($bad.Count) marker(s) show the placeholder"
Report "a marker reads '$ProbeName'" (@($labels | Where-Object { $_ -eq $ProbeName }).Count -eq 1) 'no marker label equals the probe name'

# 4. Debug build only: every NPC marker by NPC, and a screenshot with all NPC labels shown.
$probe = MapProbe ''
if ($probe -match 'summary markers=') {
    Report 'probe NPC has a marker' ($probe -match 'marker npc=mapprobe_\S+') 'no marker for the mapprobe NPC'
    Report "probe marker reads '$ProbeName'" ($probe -match "npc=mapprobe_\S+ text='$([regex]::Escape($ProbeName))'") ([regex]::Match($probe, "npc=mapprobe_\S+ text='[^']*'").Value)
    Report 'no NPC marker has the placeholder' ($probe -match 'unnamed=0') ([regex]::Match($probe, 'summary .*').Value)
    MapProbe 'show' | Out-Null
} else {
    Write-Host "  SKIP 'personnel map' (Release build, no debug commands): label check above only"
}
Start-Sleep -Seconds 1
$shot = (& python -I $s1 --slot $Slot shot 2>&1 | Out-String).Trim()
Write-Host "  screenshot $shot"
if ($ShotDir -and (Test-Path -LiteralPath $shot)) {
    New-Item -ItemType Directory -Force -Path $ShotDir | Out-Null
    $dest = Join-Path $ShotDir ("map-markers-$Slot-$(Get-Date -Format 'HHmmss').png")
    Copy-Item -LiteralPath $shot -Destination $dest
    Write-Host "  copied to $dest"
}

Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

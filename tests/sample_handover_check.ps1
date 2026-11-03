# Checks the free sample of a locked pack customer shaped like a Personify export, in the early-game state where
# Downtown is still locked. Two probe customers share a Northtown routine and an unlocked connection:
#   Sam Probe  - no spawn.region, as the editor writes it when the start point is not picked on the map. Personnel
#                takes the region from his routine, so the sample choice is valid and opens the handover screen with
#                its item slots.
#   Dina Probe - spawn.region "Downtown", the game's default region that such a customer used to keep. Her sample
#                choice is greyed out with "'Downtown' region must be unlocked".
# A vanilla customer, put back to locked, is the control in the same run. Writes the probe pack into the slot, starts
# it with this worktree's Debug build (session.ps1, save 5) and drives the `personnel cust <id> ...` probe through the
# bridge.
#
#     pwsh -File tests/sample_handover_check.ps1                    # first free slot, builds and starts it
#     pwsh -File tests/sample_handover_check.ps1 -Profile main      # mods as on the real installation
#     pwsh -File tests/sample_handover_check.ps1 -NoStart -Slot mptest4   # game already running with the probe pack
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The probe pack stays in the slot until the next run
# without -NoStart rewrites it; the game keeps running.
param(
    [string]$Slot = '',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [switch]$NoStart,
    # A vanilla customer unlocked in the 'played' template; both probe customers are connected to it.
    [string]$Connection = 'kyle_cooley',
    # Vanilla customers tried in order as the locked control; the first whose sample opens the screen is used.
    [string[]]$Vanilla = @('austin_steiner', 'peter_file', 'kathy_henderson', 'geraldine_poon', 'jessi_waters', 'donna_martin')
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$sam = 'sampleprobe_sam_probe'
$dina = 'sampleprobe_dina_probe'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function ReadLogFrom([long]$offset) {
    $fs = [IO.File]::Open($script:log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

# The bridge's run output is flooded by NavMesh warnings, so the probe's own lines are read from the game log.
function Probe([string]$npc, [string]$action) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run personnel cust $npc $action *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for '$npc $action'" }
    $lines = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match '\[Personnel\] cust' })
    foreach ($l in $lines) { Write-Host "  $($l.Trim())" }
    return ($lines -join "`n")
}

function Shot([string]$label) {
    $out = (& python -I $s1 --slot $Slot shot 2>&1 | Out-String).Trim()
    Write-Host "  screenshot ($label): $out"
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
    # The fields NpcFactory.SetRole writes for a new customer in the Personify editor, the pack header PackProject
    # writes on export (autoRegister true), and a routine in Northtown (the motel's walk spot of the example pack).
    $dir = Join-Path $gameRoot 'UserData\Personnel\Packs\SampleProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $customer = '"customer": { "spending": { "min": 200, "max": 500 }, "ordersPerWeek": { "min": 1, "max": 5 }, "standards": "Moderate", "allowDirectApproach": true }'
    $rel = '"relationships": { "delta": 1, "unlockType": "Recommendation", "connections": [ "' + $Connection + '" ] }'
    $schedule = '"schedule": [ { "type": "walkTo", "time": "06:00", "position": [-64.25, 1.07, 84.49] } ]'
    $json = @"
{ "schemaVersion": 3, "name": "Sample Probe", "packId": "sampleprobe", "autoRegister": true,
  "npcs": [
    { "name": "Sam Probe", "appearance": { "height": 1.0 }, $customer, $rel, $schedule },
    { "name": "Dina Probe", "appearance": { "height": 1.0 }, "spawn": { "region": "Downtown" }, $customer, $rel, $schedule } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
    # Profile main takes only store mods in -Mods; this worktree's build replaces its Personnel through -Build.
    $sessionArgs = @('-Slot', $Slot, '-Profile', $Profile, '-Build', (Split-Path $repo -Leaf), '-NoApp')
    if ($Profile -eq 'stack') { $sessionArgs += @('-Mods', 'Personnel') }
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}

# 1. Both pack customers exist and start locked (they have a connection). Sam's region comes from his routine.
$boot = ReadLogFrom 0
$m = [regex]::Match($boot, "'$sam': no spawn\.region - region (\w+) taken from [^\r\n]*")
Report 'sam region taken from his routine' ($m.Success -and $m.Groups[1].Value -eq 'Northtown') "log: $(if ($m.Success) { $m.Value } else { 'no region line for ' + $sam })"
if ($m.Success) { Write-Host "  $($m.Value)" }
foreach ($npc in @($sam, $dina)) {
    $d = Probe $npc 'dump'
    Report "$npc spawned" ($d -match "cust dump $npc") "no live NPC $npc"
    Report "$npc starts locked" ($d -match 'relUnlocked=False') 'relUnlocked is not False'
}

# 2. Early game: Downtown locked (Dina's region).
$lockOut = Probe $dina 'lockregion'
Report 'downtown locked' ($lockOut -match 'region Downtown unlocked=False') ([regex]::Match($lockOut, 'region \S+ unlocked=\S+').Value)

# 3. Dina keeps the old default region: the choice is greyed out with the region reason.
$d = Probe $dina 'sample'
Report 'dina sample choice greyed by the downtown region' ($d -match "sampleValid=False\('Downtown' region must be unlocked\)") ([regex]::Match($d, 'sampleValid=[^ ]+( [^ ]+)*? requestChance').Value)

# 4. Sam: valid choice, the handover screen opens in sample mode with its slots.
$s = Probe $sam 'sample'
Report 'sam region is northtown' ($s -match 'region=Northtown') ([regex]::Match($s, 'region=\S+').Value)
Report 'sam sample choice valid' ($s -match 'sampleValid=True') ([regex]::Match($s, 'sampleValid=\S+').Value)
Report 'sam sample screen opens' ($s -match 'handoverOpen=True mode=HandoverScreenSampleMode') 'handover screen did not open in sample mode'
$m = [regex]::Match($s, 'customerSlots=(\d+)/(\d+)')
Report 'sam sample slots shown' ($m.Success -and [int]$m.Groups[1].Value -gt 0) "slots $($m.Value)"

# 5. The player's path for Sam: walk up (bring), press E (talk), pick the choice (choose), read the screen.
Probe $sam 'bring' | Out-Null
$talk = Probe $sam 'talk'
Report 'sam dialogue offers the sample' ($talk -match "choices:.*free sample") ([regex]::Match($talk, 'choices:.*').Value)
Probe $sam 'choose' | Out-Null
Start-Sleep -Seconds 2
$screen = Probe $sam 'screen'
Report 'sam sample screen opens from the dialogue' ($screen -match 'handoverOpen=True mode=HandoverScreenSampleMode') ([regex]::Match($screen, 'handoverOpen=\S+ mode=\S+').Value)
$m = [regex]::Match($screen, 'customerSlots=(\d+)/(\d+)')
Report 'sam sample slots shown from the dialogue' ($m.Success -and [int]$m.Groups[1].Value -gt 0) "slots $($m.Value)"
Shot 'sam sample screen'
Probe $sam 'close' | Out-Null

# 6. Control: a vanilla customer put back to locked, in a region that is open.
$control = $null
foreach ($npc in $Vanilla) {
    Probe $npc 'lock' | Out-Null
    $v = Probe $npc 'sample'
    if ($v -match 'handoverOpen=True mode=HandoverScreenSampleMode') { $control = $npc; $vm = [regex]::Match($v, 'customerSlots=(\d+)/(\d+)'); break }
}
Report 'vanilla locked customer sample screen opens' ($null -ne $control) "none of $($Vanilla -join ', ') opened the sample screen"
if ($control) { Write-Host "  control: $control, slots $($vm.Value)" }

Probe $dina 'unlockregion' | Out-Null

$dll = Join-Path $gameRoot 'Mods\Personnel.dll'
$built = Get-ChildItem -LiteralPath (Join-Path $repo 'bin') -Recurse -Filter 'Personnel.dll' -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($Profile -eq 'stack' -and (Test-Path -LiteralPath $dll) -and $built) {
    $h1 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
    $h2 = (Get-FileHash -LiteralPath $built.FullName -Algorithm SHA256).Hash
    Report 'deployed-dll-is-this-build' ($h1 -eq $h2) "slot $h1, build $h2"
    Write-Host "  Personnel.dll SHA256 $h1"
} elseif (Test-Path -LiteralPath $dll) {
    Write-Host "  Personnel.dll SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)"
}

Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

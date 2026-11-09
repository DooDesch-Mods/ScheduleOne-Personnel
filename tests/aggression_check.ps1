# Checks that behavior.aggression reaches the game NPC. A pack NPC with aggression 1 (Agro Probe) and one with
# aggression 0 (Calm Probe) both catch the local player at a failed pickpocket (vanilla
# NPCResponses.PlayerFailedPickpocket). The aggressive one must read aggression 1 and start a fight, the calm one must
# read 0 and not fight. Then the game saves and loads again and the values must hold.
#
#     pwsh -File tests/aggression_check.ps1                    # first free slot, builds and starts it
#     pwsh -File tests/aggression_check.ps1 -Profile main      # mods as on the real installation
#     pwsh -File tests/aggression_check.ps1 -NoStart -Slot mptest4   # game already running with the probe pack
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The game keeps running.
param(
    [string]$Slot = '',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [switch]$NoStart,
    [switch]$NoReload,
    [int]$Save = 5,
    [string[]]$Mods = @()
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$call = Join-Path $PSScriptRoot 'bridge_call.py'
$agro = 'aggrprobe_agro_probe'
$calm = 'aggrprobe_calm_probe'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function ReadLogFrom([long]$offset) {
    $fs = [IO.File]::Open($script:log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

function Probe([string]$npc, [string]$action) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run personnel aggr $npc $action *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for 'aggr $npc $action'" }
    $lines = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match "\[Personnel\] aggr " })
    foreach ($l in $lines) { Write-Host "  $($l.Trim())" }
    return ($lines -join "`n")
}

function Bridge([string]$command, [string]$argsJson = '{}', [int]$timeoutMs = 12000) {
    $out = (& python -I $call --slot $Slot $command $argsJson --timeout $timeoutMs 2>&1 | Out-String).Trim()
    Write-Host "  bridge $command -> $out"
    return ($LASTEXITCODE -eq 0)
}

function WaitReady([int]$timeout = 900) {
    & python -I $s1 --slot $Slot wait-ready --timeout $timeout | Out-Host
    return ($LASTEXITCODE -eq 0)
}

function CheckValues([string]$when) {
    $a = Probe $agro 'dump'
    Report "aggressive NPC reads aggression 1 $when" ($a -match 'aggression=1 ') ([regex]::Match($a, 'aggression=\S+').Value)
    $c = Probe $calm 'dump'
    Report "calm NPC reads aggression 0 $when" ($c -match 'aggression=0 ') ([regex]::Match($c, 'aggression=\S+').Value)
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
    $dir = Join-Path $gameRoot 'UserData\Personnel\Packs\AggrProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $json = @"
{ "schemaVersion": 3, "name": "Aggr Probe", "packId": "aggrprobe", "autoRegister": true,
  "npcs": [
    { "name": "Agro Probe", "appearance": { "height": 1.0 }, "behavior": { "aggression": 1 },
      "schedule": [ { "type": "walkTo", "time": "06:00", "position": [-64.25, 1.07, 84.49] } ] },
    { "name": "Calm Probe", "appearance": { "height": 1.0 }, "behavior": { "aggression": 0 },
      "schedule": [ { "type": "walkTo", "time": "06:00", "position": [-62.25, 1.07, 84.49] } ] } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
    $sessionArgs = @('-Slot', $Slot, '-Profile', $Profile, '-Build', (Split-Path $repo -Leaf), '-NoApp', '-Save', "$Save")
    if ($Profile -eq 'stack') { $sessionArgs += @('-Mods', ((@('Personnel') + $Mods) -join ',')) }
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}

# 1. The values arrive.
CheckValues 'after spawn'

# 2. The vanilla reaction to a failed pickpocket: aggression 1 always fights back, aggression 0 never does.
$a = Probe $agro 'pickpocket'
Report 'aggressive NPC fights back' ($a -match 'combat.enabled=True') ([regex]::Match($a, 'combat.enabled=\S+').Value)
Probe $agro 'stop' | Out-Null
$c = Probe $calm 'pickpocket'
Report 'calm NPC does not fight back' ($c -match 'combat.enabled=False') ([regex]::Match($c, 'combat.enabled=\S+').Value)

# 3. Save, back to the menu, load the same save: the values hold.
if (-not $NoReload) {
    Report 'save' (Bridge 'save_now') 'save_now failed'
    Start-Sleep -Seconds 5
    Report 'return to menu' (Bridge 'return_to_menu') 'return_to_menu failed'
    Start-Sleep -Seconds 10
    $loaded = $false
    for ($i = 0; $i -lt 12 -and -not $loaded; $i++) {
        $loaded = Bridge 'load_save' "{`"slot`": $Save}" 90000
        if (-not $loaded) { Start-Sleep -Seconds 5 }
    }
    Report 'load the save again' $loaded 'load_save failed'
    Report 'game ready after the load' (WaitReady) 'wait-ready failed'
    Start-Sleep -Seconds 5
    CheckValues 'after the load'
}

$dll = Join-Path $gameRoot 'Mods\Personnel.dll'
if (Test-Path -LiteralPath $dll) { Write-Host "  Personnel.dll SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)" }
Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

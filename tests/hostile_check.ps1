# Checks behavior.hostile and behavior.weapon. A hostile pack NPC with a knife (Hostile Probe) and a calm one with
# a revolver (Armed Probe) stand in the world. The player is put in front of each in turn. The hostile one must
# start a fight on its own and hold the knife, the calm one must not fight and must have the revolver as its combat
# weapon. After the fight is stopped the hostile NPC must attack again on the next sighting.
#
#     pwsh -File tests/hostile_check.ps1                      # first free slot, builds and starts it
#     pwsh -File tests/hostile_check.ps1 -Profile main        # mods as on the real installation
#     pwsh -File tests/hostile_check.ps1 -NoStart -Slot mptest4   # game already running with the probe pack
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The game keeps running.
param(
    [string]$Slot = '',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [switch]$NoStart,
    [int]$Save = 5,
    [string[]]$Mods = @()
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$hostile = 'hostprobe_hostile_probe'
$armed = 'hostprobe_armed_probe'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function LogLength { return (Get-Item -LiteralPath $script:log).Length }

function ReadLogFrom([long]$offset) {
    $fs = [IO.File]::Open($script:log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

function Probe([string]$npc, [string]$action) {
    $before = LogLength
    & python -I $s1 --slot $Slot run personnel aggr $npc $action *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for 'aggr $npc $action'" }
    $lines = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match "\[Personnel\] aggr " })
    foreach ($l in $lines) { Write-Host "  $($l.Trim())" }
    return ($lines -join "`n")
}

# Waits up to $seconds for a log line matching $pattern written after $offset.
function WaitLog([long]$offset, [string]$pattern, [int]$seconds) {
    $deadline = (Get-Date).AddSeconds($seconds)
    while ((Get-Date) -lt $deadline) {
        $hit = @((ReadLogFrom $offset) -split "`r?`n" | Where-Object { $_ -match $pattern })
        if ($hit.Count -gt 0) { Write-Host "  $($hit[0].Trim())"; return $true }
        Start-Sleep -Milliseconds 500
    }
    return $false
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
    $dir = Join-Path $gameRoot 'UserData\Personnel\Packs\HostProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $json = @"
{ "schemaVersion": 3, "name": "Host Probe", "packId": "hostprobe", "autoRegister": true,
  "npcs": [
    { "name": "Hostile Probe", "appearance": { "height": 1.0 }, "behavior": { "hostile": true, "weapon": "knife" },
      "spawn": { "x": -64.25, "y": 0.2, "z": 84.49 },
      "schedule": [ { "type": "walkTo", "time": "06:00", "position": [-64.25, 0.2, 84.49] } ] },
    { "name": "Armed Probe", "appearance": { "height": 1.0 }, "behavior": { "weapon": "revolver" },
      "spawn": { "x": -24.25, "y": 0.2, "z": 84.49 },
      "schedule": [ { "type": "walkTo", "time": "06:00", "position": [-24.25, 0.2, 84.49] } ] } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
    $sessionArgs = @('-Slot', $Slot, '-Profile', $Profile, '-Build', (Split-Path $repo -Leaf), '-NoApp', '-Save', "$Save")
    if ($Profile -eq 'stack') { $sessionArgs += @('-Mods', ((@('Personnel') + $Mods) -join ',')) }
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}

# 1. The pack values arrive.
$a = Probe $armed 'dump'
Report 'calm NPC is not fighting' ($a -match 'combat.enabled=False') ([regex]::Match($a, 'combat.enabled=\S+').Value)
Report 'calm NPC has the revolver as combat weapon' ($a -match 'defaultWeapon=Avatar/Equippables/Revolver') ([regex]::Match($a, 'defaultWeapon=\S+').Value)

# 2. The calm NPC sees the player and does not attack.
$mark = LogLength
Probe $armed 'face' | Out-Null
Report 'calm NPC does not attack when it sees the player' (-not (WaitLog $mark "'$armed': hostile - attacking" 6)) 'it attacked'

# 3. The hostile NPC sees the player and attacks with the knife.
$mark = LogLength
Probe $hostile 'face' | Out-Null
Report 'hostile NPC attacks when it sees the player' (WaitLog $mark "'$hostile': hostile - attacking" 10) 'no attack line within 10 s'
# A knife hit knocks the player out within seconds and that ends the fight, so the state is read right away.
Start-Sleep -Milliseconds 300
$h = Probe $hostile 'dump'
Report 'hostile NPC is fighting the player' ($h -match 'combat.enabled=True' -and $h -notmatch 'target=none') (([regex]::Match($h, 'combat.enabled=\S+').Value) + ' ' + ([regex]::Match($h, 'target=\S+').Value))
Report 'hostile NPC holds the knife' ($h -match 'weapon=Knife') ([regex]::Match($h, ' weapon=\S+').Value)


# 4. The fight ends while the player is still in sight; the NPC starts a new one on its own.
# A knocked-out player is no target, so the next sighting is tried once the player is up again (up to 60 s).
$mark = LogLength
Probe $hostile 'stop' | Out-Null
$again = $false
$deadline = (Get-Date).AddSeconds(60)
while (-not $again -and (Get-Date) -lt $deadline) {
    $d = Probe $hostile 'dump'
    if ($d -match 'player.unconscious=False' -and $d -match 'player.alive=True') {
        Probe $hostile 'face' | Out-Null
        $again = WaitLog $mark "'$hostile': hostile - attacking " 5
    } else { Start-Sleep -Seconds 2 }
}
Report 'hostile NPC attacks again after the fight ended' $again 'no new attack within 60 s'
Probe $hostile 'stop' | Out-Null

# 5. A fight that vanilla ends after a hit restarts at most every 3 s, not on every check.
$times = @([regex]::Matches((ReadLogFrom 0), "\[(\d\d:\d\d:\d\d\.\d+)\] \[Personnel\] '$hostile': hostile - attacking ") | ForEach-Object { [TimeSpan]::Parse($_.Groups[1].Value) })
$gaps = @(for ($i = 1; $i -lt $times.Count; $i++) { ($times[$i] - $times[$i - 1]).TotalSeconds })
$minGap = if ($gaps.Count) { ($gaps | Measure-Object -Minimum).Minimum } else { 99 }
Report 'hostile NPC waits at least 3 s between fight starts' ($times.Count -ge 2 -and $minGap -ge 2.9) "$($times.Count) starts, shortest gap $([math]::Round($minGap, 2)) s"

$dll = Join-Path $gameRoot 'Mods\Personnel.dll'
if (Test-Path -LiteralPath $dll) { Write-Host "  Personnel.dll SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)" }
Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

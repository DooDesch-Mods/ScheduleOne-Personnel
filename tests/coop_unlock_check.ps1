# Co-op check for the free-sample unlock: on a joined pair, the CLIENT hands a free sample to a locked Personnel pack
# customer (Una Probe, connected to an unlocked vanilla customer) and to a locked vanilla customer as the control. The
# host rolls the sample (made certain with `personnel cust <id> guarantee`) and must end up with both unlocked; then
# the host saves and its NPCs.json must store both as unlocked. Needs a Debug build (the `personnel cust` probe).
#
#     pwsh -File tests/coop_unlock_check.ps1                 # writes the probe pack, brings the pair up (coop_up.ps1)
#     pwsh -File tests/coop_unlock_check.ps1 -NoUp           # pair already running with the probe pack in both slots
#     pwsh -File tests/coop_unlock_check.ps1 -Giver host     # the host hands the samples instead
#     pwsh -File tests/coop_unlock_check.ps1 -Reload         # then the host loads the save again and checks once more
#     pwsh -File tests/coop_unlock_check.ps1 -S1Api <dir>    # S1API DLLs from <dir>\Mods and <dir>\Plugins in both slots
#
# Prints PASS or FAIL per check and exits 1 when any check fails. Stop the pair afterwards with tests/coop_down.ps1.
param(
    [switch]$NoUp,
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [ValidateSet('client', 'host')]
    [string]$Giver = 'client',
    [string]$Connection = 'kyle_cooley',
    # Vanilla controls, tried in order; the first one the giver can open the sample screen for is used.
    [string[]]$Vanilla = @('austin_steiner', 'peter_file', 'kathy_henderson', 'geraldine_poon', 'jessi_waters', 'donna_martin'),
    [int]$SettleSeconds = 20,
    # Passed to coop_up.ps1: a folder with S1API DLLs that replace the stack's S1API in both slots.
    [string]$S1Api = '',
    # After the save, stop the client, take the host to the menu and load the save again, then check the host.
    [switch]$Reload
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$call = Join-Path $PSScriptRoot 'bridge_call.py'
$una = 'unlockprobe_una_probe'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function ReadLogFrom([string]$log, [long]$offset) {
    $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

# Runs a console command in a slot and returns the Personnel probe lines it wrote to that slot's log.
function Probe([string]$slot, [string]$command) {
    $log = Join-Path $lease.table.$slot.root 'MelonLoader\Latest.log'
    $before = (Get-Item -LiteralPath $log).Length
    $parts = $command -split ' '
    & python -I $s1 --slot $slot run @parts *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for '$command' in $slot" }
    $lines = @((ReadLogFrom $log $before) -split "`r?`n" | Where-Object { $_ -match '\[Personnel\] (cust|rel) ' })
    foreach ($l in $lines) { Write-Host "  [$slot] $($l.Trim().Substring(0, [Math]::Min(260, $l.Trim().Length)))" }
    return ($lines -join "`n")
}

function WritePack([string]$slotRoot) {
    $dir = Join-Path $slotRoot 'UserData\Personnel\Packs\UnlockProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $json = @"
{ "schemaVersion": 3, "name": "Unlock Probe", "packId": "unlockprobe", "autoRegister": true,
  "npcs": [
    { "name": "Una Probe", "appearance": { "height": 1.0 },
      "customer": { "spending": { "min": 200, "max": 500 }, "ordersPerWeek": { "min": 1, "max": 5 }, "standards": "Moderate", "allowDirectApproach": true },
      "relationships": { "delta": 1, "unlockType": "Recommendation", "connections": [ "$Connection" ] },
      "schedule": [ { "type": "walkTo", "time": "06:00", "position": [-64.25, 1.07, 84.49] } ] } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
}

if (-not $NoUp) {
    $pre = (& python -I $slotsPy --json acquire --coop --via duo.ps1 --mod Personnel | Out-String | ConvertFrom-Json).lease
    if (-not $pre -or @($pre.slots).Count -lt 2) { Write-Host 'FAIL lease - no co-op pair'; exit 1 }
    foreach ($s in @($pre.slots)) { WritePack $pre.table.$s.root }
    & pwsh -File (Join-Path $PSScriptRoot 'coop_up.ps1') -Profile $Profile -S1Api $S1Api
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL coop_up - pair did not come up'; exit 1 }
}

$lease = (& python -I $slotsPy --json mine | Out-String | ConvertFrom-Json).lease
if (-not $lease -or @($lease.slots).Count -lt 2) { Write-Host 'FAIL lease - this session holds no co-op pair'; exit 1 }
$hostSlot = @($lease.slots)[0]
$clientSlot = @($lease.slots)[-1]
$giverSlot = if ($Giver -eq 'host') { $hostSlot } else { $clientSlot }
Write-Host "host $hostSlot, client $clientSlot, giver $Giver ($giverSlot)"

# 1. Start state: the probe exists on both sides and is locked; the vanilla control is put back to locked on both.
foreach ($slot in @($hostSlot, $clientSlot)) {
    $d = Probe $slot "personnel cust $una dump"
    Report "$slot $una spawned" ($d -match "cust dump $una") "no live NPC $una"
    Report "$slot $una starts locked" ($d -match 'relUnlocked=False') 'relUnlocked is not False'
}

# 2. The giver opens the sample flow. A vanilla control whose sample screen opens is picked first.
$control = $null
foreach ($npc in $Vanilla) {
    foreach ($slot in @($hostSlot, $clientSlot)) { Probe $slot "personnel cust $npc lock" | Out-Null }
    $v = Probe $giverSlot "personnel cust $npc sample"
    if ($v -match 'handoverOpen=True mode=HandoverScreenSampleMode') { $control = $npc; break }
}
Report 'vanilla control found' ($null -ne $control) "none of $($Vanilla -join ', ') opened the sample screen"

foreach ($npc in @($una, $control) | Where-Object { $_ }) {
    foreach ($slot in @($hostSlot, $clientSlot)) { Probe $slot "personnel cust $npc unlockregion" | Out-Null }
    $g = Probe $hostSlot "personnel cust $npc guarantee"
    Report "host guarantees the sample for $npc" ($g -match 'guaranteeFirstSample=True') 'guarantee not set'
    Probe $giverSlot "personnel cust $npc stock" | Out-Null
    Probe $giverSlot "personnel cust $npc bring" | Out-Null
    $talk = Probe $giverSlot "personnel cust $npc talk"
    Report "$Giver dialogue offers the sample to $npc" ($talk -match 'choices:.*free sample') ([regex]::Match($talk, 'choices:.*').Value)
    Probe $giverSlot "personnel cust $npc choose" | Out-Null
    Start-Sleep -Seconds 2
    $done = Probe $giverSlot "personnel cust $npc done"
    Report "$Giver hands the sample to $npc" ($done -match 'handed .*handoverOpen=False') ([regex]::Match($done, 'cust done.*').Value)
}
Write-Host "waiting $SettleSeconds s for the samples to be consumed"
Start-Sleep -Seconds $SettleSeconds

# 3. Both sides see both customers unlocked.
foreach ($slot in @($hostSlot, $clientSlot)) {
    $side = if ($slot -eq $hostSlot) { 'host' } else { 'client' }
    foreach ($npc in @($una, $control) | Where-Object { $_ }) {
        $d = Probe $slot "personnel cust $npc dump"
        Report "$side sees $npc unlocked" ($d -match 'relUnlocked=True') ([regex]::Match($d, 'relUnlocked=\S+').Value)
    }
}

# 4. The host saves; its NPCs.json stores both as unlocked.
$out = (& python -I $call --slot $hostSlot save_now 2>&1 | Out-String).Trim()
Write-Host "  save_now -> $out"
Report 'host saves' ($LASTEXITCODE -eq 0) $out
Start-Sleep -Seconds 5
$steam = $lease.table.$hostSlot.steamid
$savesRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\TVGS\Schedule I\Saves'
$npcsJson = Get-ChildItem -LiteralPath $savesRoot -Recurse -Filter 'NPCs.json' -ErrorAction SilentlyContinue |
    Where-Object { -not $steam -or $_.FullName -like "*\$steam\*" } | Sort-Object LastWriteTime -Descending | Select-Object -First 1
Report 'host save file found' ($null -ne $npcsJson) "no NPCs.json under $savesRoot"
if ($npcsJson) {
    Write-Host "  $($npcsJson.FullName) written $($npcsJson.LastWriteTime)"
    $data = Get-Content -LiteralPath $npcsJson.FullName -Raw | ConvertFrom-Json -Depth 50
    foreach ($npc in @($una, $control) | Where-Object { $_ }) {
        $entry = $null
        foreach ($e in @($data.NPCs)) {
            $base = $e.BaseData | ConvertFrom-Json
            if ($base.ID -eq $npc) { $entry = $e; break }
        }
        $rel = if ($entry) { @($entry.AdditionalDatas | Where-Object { $_.Name -eq 'Relationship' })[0] } else { $null }
        $unlocked = if ($rel) { ($rel.Contents | ConvertFrom-Json).Unlocked } else { $null }
        Report "host save stores $npc unlocked" ($unlocked -eq $true) "Relationship.Unlocked=$unlocked (entry found: $($null -ne $entry))"
    }
}

# 5. The host leaves to the menu and loads the same save, the "save and load again" of the report. The client is
#    stopped first, since it would only lose the session. Both customers must still be unlocked on the host.
if ($Reload) {
    $clientRoot = $lease.table.$clientSlot.root
    Get-CimInstance Win32_Process -Filter "Name='Schedule I.exe'" |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($clientRoot, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "  stopped client pid $($_.ProcessId)" }
    $out = (& python -I $call --slot $hostSlot return_to_menu 2>&1 | Out-String).Trim()
    Write-Host "  return_to_menu -> $out"
    Report 'host returns to the menu' ($LASTEXITCODE -eq 0) $out
    Start-Sleep -Seconds 10
    $loaded = $false
    for ($i = 0; $i -lt 12 -and -not $loaded; $i++) {
        $out = (& python -I $call --slot $hostSlot load_save '{"slot": 5}' --timeout 90000 2>&1 | Out-String).Trim()
        Write-Host "  load_save -> $out"
        $loaded = ($LASTEXITCODE -eq 0)
        if (-not $loaded) { Start-Sleep -Seconds 5 }
    }
    Report 'host loads the save again' $loaded 'load_save failed'
    & python -I $s1 --slot $hostSlot wait-ready --timeout 900 | Out-Host
    Report 'host ready after the load' ($LASTEXITCODE -eq 0) 'wait-ready failed'
    foreach ($npc in @($una, $control) | Where-Object { $_ }) {
        $d = Probe $hostSlot "personnel cust $npc dump"
        Report "host still sees $npc unlocked after the load" ($d -match 'relUnlocked=True') ([regex]::Match($d, 'relUnlocked=\S+').Value)
        Report "host still lists $npc as unlocked after the load" ($d -match 'inUnlocked=True') ([regex]::Match($d, 'inUnlocked=\S+').Value)
    }
}

if ($script:failed -gt 0) { Write-Host "RESULT FAIL ($script:failed failed)"; exit 1 }
Write-Host 'RESULT PASS'
exit 0

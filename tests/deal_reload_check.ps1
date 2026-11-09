# Checks that an open deal request by text can still be answered after save and load. A pack customer (Dora Probe)
# and an unlocked vanilla customer as the control each send a deal request (Customer.ForceDealOffer, the same call
# the customer makes on its own). The game saves, goes back to the menu and loads the same save again; then both
# must still hold the offer and show the accept, counter and decline buttons with their click handlers.
#
#     pwsh -File tests/deal_reload_check.ps1                    # first free slot, builds and starts it
#     pwsh -File tests/deal_reload_check.ps1 -Profile main      # mods as on the real installation
#     pwsh -File tests/deal_reload_check.ps1 -NoStart -Slot mptest4   # game already running with the probe pack
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The game keeps running.
param(
    [string]$Slot = '',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [switch]$NoStart,
    [int]$Save = 5,
    # A vanilla customer unlocked in the 'played' template, the control.
    [string]$Vanilla = 'kyle_cooley',
    [string]$ProbeId = 'dealprobe_dora_probe',
    [string[]]$Mods = @()
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$call = Join-Path $PSScriptRoot 'bridge_call.py'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function ReadLogFrom([long]$offset) {
    $fs = [IO.File]::Open($script:log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

function Probe([string]$what, [string]$npc, [string]$action) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run personnel $what $npc $action *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for '$what $npc $action'" }
    $lines = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match "\[Personnel\] (cust|rel) " })
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

# The three buttons of a deal request, each with its click handler.
function ButtonsOk([string]$convo) {
    return ($convo -match 'offered=True') -and ($convo -match '\[ACCEPT_CONTRACT cb=True\]') -and
        ($convo -match '\[COUNTEROFFER cb=True\]') -and ($convo -match '\[REJECT_CONTRACT cb=True\]')
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
    $dir = Join-Path $gameRoot 'UserData\Personnel\Packs\DealProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $json = @"
{ "schemaVersion": 3, "name": "Deal Probe", "packId": "dealprobe", "autoRegister": true,
  "npcs": [
    { "name": "Dora Probe", "appearance": { "height": 1.0 },
      "customer": { "spending": { "min": 200, "max": 500 }, "ordersPerWeek": { "min": 1, "max": 5 }, "standards": "Low", "allowDirectApproach": true },
      "relationships": { "delta": 3, "unlockType": "DirectApproach", "connections": [ ] },
      "schedule": [ { "type": "walkTo", "time": "06:00", "position": [-64.25, 1.07, 84.49] } ] } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
    $sessionArgs = @('-Slot', $Slot, '-Profile', $Profile, '-Build', (Split-Path $repo -Leaf), '-NoApp', '-Save', "$Save")
    if ($Profile -eq 'stack') { $sessionArgs += @('-Mods', ((@('Personnel') + $Mods) -join ',')) }
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}

# 1. Both customers are unlocked and send a deal request.
$d = Probe 'cust' $ProbeId 'dump'
Report 'probe customer spawned' ($d -match "cust dump $ProbeId") "no live NPC $ProbeId"
Probe 'rel' $ProbeId 'direct' | Out-Null
$checked = @($ProbeId)
$r = Probe 'cust' $ProbeId 'request'
Report "$ProbeId deal request has its buttons before the save" (ButtonsOk $r) ([regex]::Match($r, 'cust convo.*').Value)
# The vanilla customer is a control only. One with an assigned dealer or no product to want sends no request.
$r = Probe 'cust' $Vanilla 'request'
if ($r -match 'offered=False') { Write-Host "SKIP $Vanilla control - the game made no deal request (dealer assigned or nothing to order)" }
else {
    Report "$Vanilla deal request has its buttons before the save" (ButtonsOk $r) ([regex]::Match($r, 'cust convo.*').Value)
    $checked += $Vanilla
}

# 2. Save, back to the menu, load the same save.
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

# 3. Both requests can still be answered.
foreach ($npc in $checked) {
    $c = Probe 'cust' $npc 'convo'
    Report "$npc deal request has its buttons after the load" (ButtonsOk $c) ([regex]::Match($c, 'cust convo.*').Value)
}

$dll = Join-Path $gameRoot 'Mods\Personnel.dll'
if (Test-Path -LiteralPath $dll) { Write-Host "  Personnel.dll SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)" }
Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

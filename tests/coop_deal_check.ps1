# Co-op check for scheduled deals with Personnel pack customers: on a client the "Customer attend deal" behaviour must
# have its owner and its Customer, so the host's Enable/Activate RPCs reach it and the customer offers the handover. A
# vanilla customer runs the same steps as the control. Needs a Debug build (the `personnel cust` probe).
#
#     pwsh -File tests/coop_deal_check.ps1                 # brings the pair up with coop_up.ps1 first
#     pwsh -File tests/coop_deal_check.ps1 -NoUp           # pair already running
#     pwsh -File tests/coop_deal_check.ps1 -Pack cleo -Vanilla kyle,austin
#
# Prints PASS or FAIL per check and exits 1 when any check fails. Stop the pair afterwards with tests/coop_down.ps1.
param(
    [string]$Pack = 'cleo',
    # Vanilla controls, tried in order; the first one the host can book a player deal for is used. Played saves give
    # most early customers to dealers, so the list is long.
    [string[]]$Vanilla = @('kyle', 'austin', 'jessi', 'mick', 'beth', 'peter', 'donna', 'chloe', 'sam', 'geraldine',
        'meg', 'jerry', 'ludwig', 'eugene', 'kathy', 'doris', 'jennifer', 'lisa', 'elizabeth', 'george', 'herbert',
        'lucy', 'dean', 'randy', 'jeff', 'charles', 'fiona', 'alison', 'carl', 'harold', 'jackie', 'joyce', 'keith'),
    [switch]$NoUp,
    # 'main': both slots hold the real installation's mods plus this build (coop_up.ps1 -Profile main).
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [int]$SettleSeconds = 6
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function Run([string]$slot, [string]$command) {
    $out = & python -I $s1 --slot $slot run $command 2>&1 | Out-String
    Write-Host "[$slot] $command`n$($out.TrimEnd())"
    return $out
}

if (-not $NoUp) {
    & pwsh -File (Join-Path $PSScriptRoot 'coop_up.ps1') -Profile $Profile
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL coop_up - pair did not come up'; exit 1 }
}

$lease = (& python -I $slotsPy --json mine | ConvertFrom-Json).lease
if (-not $lease -or @($lease.slots).Count -lt 2) { Write-Host 'FAIL lease - this session holds no co-op pair'; exit 1 }
$hostSlot = @($lease.slots)[0]
$clientSlot = @($lease.slots)[-1]
$clientLog = Join-Path $lease.table.$clientSlot.root 'MelonLoader\Latest.log'

# 1. The repair ran on the client when the pack NPC was created.
$repairLine = Select-String -LiteralPath $clientLog -Pattern "'[^']*$Pack[^']*': \d+ behaviour owner\(s\) checked" | Select-Object -Last 1
Report "client repair ran for $Pack" ($null -ne $repairLine) "no 'behaviour owner(s) checked' line in $clientLog"
if ($repairLine) { Write-Host "  $($repairLine.Line.Trim())" }

# 2. No behaviour on the client's pack NPC points at a foreign or missing owner, and the attend behaviour knows its
#    Customer (without it Activate throws before the customer waits at the drop).
$ident = Run $clientSlot "personnel cust $Pack ident"
Report "client $Pack has no foreign owner" ($ident -match 'stack entries with a foreign owner: 0') 'foreign owner count is not 0'
Report "client $Pack attend owner set" ($ident -match 'attend\.beh ptr=\d') 'attend.beh is null'
Report "client $Pack attend customer set" ($ident -match 'attend\.customer=set') 'attend._customer is null'

# 3. Host books a scheduled deal for the pack customer and the first vanilla control a dealer did not take.
$deal = Run $hostSlot "personnel cust $Pack deal"
Report "host books deal for $Pack" ($deal -match 'contract=True|already has a contract') 'no contract created'
$control = $null
foreach ($npc in $Vanilla) {
    $deal = Run $hostSlot "personnel cust $npc deal"
    # An existing contract counts only as a player deal; a dealer's contract shows "Waiting for <dealer>".
    if ($deal -match 'Waiting for') { Write-Host "  skipping $npc - contract belongs to a dealer"; continue }
    if ($deal -match 'contract=True|already has a contract') { $control = $npc; break }
}
Report 'host books deal for a vanilla control' ($null -ne $control) "none of $($Vanilla -join ', ') got a player deal"
Start-Sleep -Seconds $SettleSeconds

# 4. Both sides see the customer waiting for the handover.
foreach ($slot in @($hostSlot, $clientSlot)) {
    foreach ($npc in @($Pack, $control) | Where-Object { $_ }) {
        $hand = Run $slot "personnel cust $npc hand"
        $side = if ($slot -eq $hostSlot) { 'host' } else { 'client' }
        Report "$side $npc attend behaviour enabled" ($hand -match 'attend\.enabled=True') 'attend.enabled is not True'
        Report "$side $npc ready for handover" ($hand -match 'readyForHandover=True') 'readyForHandover is not True'
    }
}

if ($script:failed -gt 0) { Write-Host "RESULT FAIL ($script:failed failed)"; exit 1 }
Write-Host 'RESULT PASS'
exit 0

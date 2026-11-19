# Checks that Personnel leaves NPCs from other mods as those mods made them (#104). Two mods serve as samples:
#   nightlife  Nightlife (Nexus 3164): twelve physical club NPCs that wait at a holding point beside the town hall.
#              Check: they stay at that holding point.
#   empire     Empire 2.0 (Nexus 1437): contact-only dealers and suppliers (S1API IsPhysical false).
#              Check: none of them has a body drawn in the world.
# Starts the slot with that mod, with or without this worktree's Personnel build, loads save 5 and reports where each
# NPC stands and whether its body is drawn.
#
#     pwsh -File tests/foreign_npc_check.ps1                              # Nightlife, with Personnel, first free slot
#     pwsh -File tests/foreign_npc_check.ps1 -Mod empire -WithoutPersonnel
#     pwsh -File tests/foreign_npc_check.ps1 -Mod empire -Slot mptest -NoStart
#
# Needs the mod in the slot catalog (Workspace/tools/slots/catalog.json). Prints PASS or FAIL per check, writes a
# screenshot and exits 1 when any check fails. The game keeps running.
param(
    [ValidateSet('nightlife', 'empire')]
    [string]$Mod = 'nightlife',
    [string]$Slot = '',
    [switch]$WithoutPersonnel,
    [switch]$NoStart,
    [string]$Workspace = ''
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
if (-not $Workspace) { $Workspace = Join-Path $root 'Workspace' }
$session = Join-Path $Workspace 'tools\session\session.ps1'
$probe = Join-Path $PSScriptRoot 'foreign_npc_probe.py'
$catalogName = @{ nightlife = 'Nightlife'; empire = 'Empire' }[$Mod]
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name - $detail" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

if (-not $NoStart) {
    $sessionArgs = @('-NoProfile', '-File', $session, '-Mods', $catalogName, '-Save', '5', '-NoApp', '-Purpose', "104 foreign NPCs ($Mod)")
    if ($Slot) { $sessionArgs += @('-Slot', $Slot) }
    if (-not $WithoutPersonnel) { $sessionArgs += @('-Build', (Split-Path $repo -Leaf)) }
    & pwsh @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}
if (-not $Slot) {
    # slots.py marks the slot this Claude session leases with the state "mine".
    $register = & python -I (Join-Path $root 'Workspace\tools\slots\slots.py')
    $Slot = $register | Select-String -Pattern '^(\S+)\s+\d+\s+mine\s' | ForEach-Object { $_.Matches[0].Groups[1].Value } | Select-Object -Last 1
    if (-not $Slot) { throw 'no slot given and none held by this session (python Workspace/tools/slots/slots.py)' }
    Write-Host "slot: $Slot"
}

$out = & python -I $probe --slot $Slot --mod $Mod
$code = $LASTEXITCODE
$out | ForEach-Object { Write-Host $_ }
$result = $out | Where-Object { $_ -like 'RESULT *' } | Select-Object -Last 1
if (-not $result) { throw "probe printed no RESULT line (exit $code)" }
$data = ($result.Substring(7) | ConvertFrom-Json)

$label = if ($WithoutPersonnel) { "$catalogName without Personnel" } else { "$catalogName with Personnel" }
Report "NPCs exist ($label)" ($data.found -eq $data.expected) "$($data.found) of $($data.expected) NPCs below @NPCs"

if ($Mod -eq 'nightlife') {
    # Nightlife parks its club NPCs at a holding point beside the town hall (x 52-66, z 24-30) until the club opens.
    # Personnel must leave them there: an NPC elsewhere means something moved an NPC Personnel does not own.
    $moved = @($data.npcs | Where-Object { -not $_.pos -or $_.pos[0] -lt 52 -or $_.pos[0] -gt 66 -or $_.pos[2] -lt 24 -or $_.pos[2] -gt 30 })
    $movedNames = ($moved | ForEach-Object { $_.name }) -join ', '
    Report "club NPCs stay where Nightlife put them ($label)" ($moved.Count -eq 0) $(if ($moved.Count) { "away from the holding point: $movedNames" } else { 'all at the holding point' })
    # Whether they are drawn there is Nightlife's own behaviour (it hides them only after the club opened once), so
    # it is reported, not checked.
    Write-Host "INFO drawn at the holding point: $($data.drawnOutside), hidden: $($data.hidden)"
}
else {
    # Contact-only NPCs live in the phone. A drawn body in the world means something made them physical.
    $shown = @($data.npcs | Where-Object { $_.drawn })
    $shownNames = ($shown | ForEach-Object { "$($_.name) at ($([math]::Round($_.pos[0],1)), $([math]::Round($_.pos[2],1)))" }) -join ', '
    Report "contact-only NPCs have no body in the world ($label)" ($shown.Count -eq 0) $(if ($shown.Count) { "drawn: $shownNames" } else { "all $($data.hidden) hidden" })
}
Write-Host "screenshot: $($data.shot)"

if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed"; exit 1 }
Write-Host 'all checks passed'
exit 0

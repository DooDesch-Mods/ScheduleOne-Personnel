# Checks that NPCs another mod adds stay where that mod puts them when Personnel is installed (#104). Uses Nightlife
# (Nexus 3164), whose twelve club NPCs are physical S1API NPCs that wait at a holding point and are shown only
# while the club is open. Starts the slot with Nightlife, with or without this worktree's Personnel build, loads
# save 5 and reports where each club NPC stands and whether its body is drawn.
#
#     pwsh -File tests/foreign_npc_check.ps1                    # with Personnel, first free slot
#     pwsh -File tests/foreign_npc_check.ps1 -WithoutPersonnel  # the same stack without Personnel, for comparison
#     pwsh -File tests/foreign_npc_check.ps1 -Slot mptest -NoStart
#
# Needs Nightlife in the slot catalog (Workspace/tools/slots/catalog.json). Prints PASS or FAIL per check, writes a
# screenshot of the holding point and exits 1 when any check fails. The game keeps running.
param(
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
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$probe = Join-Path $PSScriptRoot 'foreign_npc_probe.py'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name - $detail" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

if (-not $NoStart) {
    $sessionArgs = @('-NoProfile', '-File', $session, '-Mods', 'Nightlife', '-Save', '5', '-NoApp', '-Purpose', '104 foreign NPCs')
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

$out = & python -I $probe --slot $Slot
$code = $LASTEXITCODE
$out | ForEach-Object { Write-Host $_ }
$result = $out | Where-Object { $_ -like 'RESULT *' } | Select-Object -Last 1
if (-not $result) { throw "probe printed no RESULT line (exit $code)" }
$data = ($result.Substring(7) | ConvertFrom-Json)

$label = if ($WithoutPersonnel) { 'without Personnel' } else { 'with Personnel' }
Report "club NPCs exist ($label)" ($data.found -eq 12) "$($data.found) of 12 club NPCs in the scene"
# Nightlife parks its club NPCs at a holding point beside the town hall (x 52-66, z 24-30) until the club opens.
# Personnel must leave them there: an NPC elsewhere means something moved an NPC Personnel does not own.
$moved = @($data.npcs | Where-Object { -not $_.pos -or $_.pos[0] -lt 52 -or $_.pos[0] -gt 66 -or $_.pos[2] -lt 24 -or $_.pos[2] -gt 30 })
$movedNames = ($moved | ForEach-Object { $_.name }) -join ', '
Report "club NPCs stay where Nightlife put them ($label)" ($moved.Count -eq 0) $(if ($moved.Count) { "away from the holding point: $movedNames" } else { 'all at the holding point' })
# Whether they are drawn there is Nightlife's own behaviour (it hides them only after the club opened once), so it is
# reported, not checked.
Write-Host "INFO drawn at the holding point: $($data.drawnOutside), hidden: $($data.hidden)"
Write-Host "screenshot: $($data.shot)"

if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed"; exit 1 }
Write-Host 'all checks passed'
exit 0

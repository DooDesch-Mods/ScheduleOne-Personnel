# Runs foreign_npc_spots.py twice on the same fresh save (#104): run A with the mod under test and without
# Personnel, run B with the mod and this worktree's Personnel build. Each run starts the slot, waits for the save,
# walks the times of day at the given spots and writes a contact sheet. Prints one PASS/FAIL line per run: PASS when
# no NPC of the mod under test has a drawn body anywhere at any of the times.
#
#     pwsh -File tests/foreign_npc_spots.ps1 -Out F:\path\evidence
#     pwsh -File tests/foreign_npc_spots.ps1 -Out F:\path\evidence -Runs B
#
# Defaults are the two spots of the Empire report: behind the gas mart next to the car wash (the map origin, where
# S1API parks contact-only NPCs) and the street under the freeway between the barbershop and the clothing store.
param(
    [Parameter(Mandatory)][string]$Out,
    [string]$Slot = 'mptest',
    [ValidateSet('empire')][string]$Mod = 'empire',
    [string[]]$Runs = @('A', 'B'),
    [string[]]$Spots = @('gasmart-carwash:2,1,2:-8,4,14', 'bridge-barber:-17,1,22'),
    [string[]]$Times = @('0800', '1500', '2000', '0100')
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$bridge = Join-Path $root 'Workspace\tools\bridge\s1.py'
$catalogName = @{ empire = 'Empire' }[$Mod]
$failed = 0
New-Item -ItemType Directory -Force $Out | Out-Null

foreach ($run in $Runs) {
    $label = if ($run -eq 'A') { "$catalogName without Personnel" } else { "$catalogName with Personnel" }
    Write-Host "=== run $run ($label)"
    $running = Get-Process -Name 'Schedule I' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "*\_$($Slot -replace '^mptest', 'MPTest')\Schedule I\*" }
    foreach ($p in $running) { Write-Host "stopping the $Slot game (pid $($p.Id))"; Stop-Process -Id $p.Id -Force; $p.WaitForExit(20000) | Out-Null }

    $sessionArgs = @('-NoProfile', '-File', $session, '-Slot', $Slot, '-Mods', $catalogName, '-Save', '5', '-NoApp', '-Purpose', "104 $label spots")
    if ($run -ne 'A') { $sessionArgs += @('-Build', (Split-Path $repo -Leaf)) }
    & pwsh @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed for run $run (exit $LASTEXITCODE)" }
    & python -I $bridge --slot $Slot wait-ready --timeout 300
    if ($LASTEXITCODE -ne 0) { throw "run ${run}: the save did not load" }

    $probeArgs = @('-I', (Join-Path $PSScriptRoot 'foreign_npc_spots.py'), '--slot', $Slot, '--mod', $Mod, '--tag', $run, '--out', $Out, '--times') + $Times
    foreach ($s in $Spots) { $probeArgs += @('--spot', $s) }
    $lines = & python @probeArgs
    $lines | Where-Object { $_ -notlike 'RESULT *' } | ForEach-Object { Write-Host $_ }
    $result = $lines | Where-Object { $_ -like 'RESULT *' } | Select-Object -Last 1
    if (-not $result) { throw "run ${run}: probe printed no RESULT line" }
    $data = $result.Substring(7) | ConvertFrom-Json
    $result | Set-Content (Join-Path $Out "result-$run.json")
    & python -I (Join-Path $PSScriptRoot 'contact_sheet.py') $Out $run (Join-Path $Out "sheet-$run.png")

    $seen = @($data.rows | ForEach-Object { $_.foreignAnywhere } | Where-Object { $_ })
    if ($seen.Count -eq 0) { Write-Host "PASS no $catalogName NPC drawn at any time ($label)" }
    else { Write-Host "FAIL $catalogName NPCs drawn ($label): $(($seen | ForEach-Object { "$($_.name) at ($($_.pos -join ', '))" }) -join '; ')"; $failed++ }
}
if ($failed) { exit 1 }
exit 0

# Stops the co-op pair from tests/coop_up.ps1 and removes the packs it copied between the slots.
#
#     pwsh -File tests/coop_down.ps1
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$lease = (& python -I $slotsPy --json mine | ConvertFrom-Json).lease
if (-not $lease) { Write-Host 'this session holds no slots'; exit 0 }
$roots = @($lease.slots | ForEach-Object { $lease.table.$_.root })

& pwsh -NoProfile -File (Join-Path $root 'Workspace\tools\session\duo.ps1') -Stop

foreach ($slotRoot in $roots) {
    $packs = Join-Path $slotRoot 'UserData\Personnel\Packs'
    $added = Join-Path $packs '.coop_added.txt'
    if (-not (Test-Path -LiteralPath $added)) { Write-Host "no copied packs in $packs"; continue }
    foreach ($name in Get-Content -LiteralPath $added) {
        if (-not $name) { continue }
        Remove-Item -LiteralPath (Join-Path $packs $name) -Recurse -Force
        Write-Host "removed copied pack $name from $packs"
    }
    Remove-Item -LiteralPath $added
}

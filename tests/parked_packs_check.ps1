# Checks that packs parked as Packs/<name>.parked-perf stay on disk but do not load. Puts a parked dummy pack and a
# live control pack into the slot's Packs folder, parks the Examples pack, starts the slot with this worktree's build
# (session.ps1, main menu is enough: packs load in OnInitializeMelon) and reads the boot log.
#
#     pwsh -File tests/parked_packs_check.ps1                  # first free slot, builds and starts it
#     pwsh -File tests/parked_packs_check.ps1 -Slot mptest3
#     pwsh -File tests/parked_packs_check.ps1 -NoStart         # game already booted with the probe packs in place
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The probe packs are removed and the Examples pack
# is restored afterwards; the game keeps running (stop it with session.ps1's slot or quit it in game).
param(
    [string]$Slot = '',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [switch]$NoStart
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function Write-Pack([string]$dir, [string]$packId, [string]$npc) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $json = @"
{ "schemaVersion": 3, "name": "$packId", "packId": "$packId",
  "npcs": [ { "name": "$npc", "appearance": { "height": 1.0 } } ] }
"@
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $dir"
}

# The lease decides the slot; session.ps1 reuses the lease this Claude session already holds.
$acqArgs = @('-I', $slotsPy, '--json', 'acquire', '--mod', 'Personnel')
if ($Slot) { $acqArgs += @('--slot', $Slot) }
$lease = (& python @acqArgs | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $lease) { throw "slots.py acquire failed (exit $LASTEXITCODE)" }
$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
if (-not $Slot) { $Slot = @($lease.lease.slots)[0] }
if (-not $Slot) { $Slot = @($lease.slots)[0] }
if (-not $Slot) { throw "no slot in the lease: $($lease | ConvertTo-Json -Depth 5 -Compress)" }
$gameRoot = $table.$Slot.root
Write-Host "slot $Slot -> $gameRoot"

$packs = Join-Path $gameRoot 'UserData\Personnel\Packs'
$parkedDir = Join-Path $packs 'ParkedProbe.Parked-Perf'   # mixed case: the suffix match is case-insensitive
$liveDir = Join-Path $packs 'ParkedProbeLive'
$examples = Join-Path $packs 'Examples'
$examplesParked = Join-Path $packs 'Examples.parked-perf'
$examplesWasThere = Test-Path -LiteralPath (Join-Path $examples 'manifest.json')

# The example pack is only written with LoadExamplePack on. session.ps1 kills the slot's game (no preference save on
# exit), so this edit survives until the next boot reads it; the original file comes back in finally.
$cfg = Join-Path $gameRoot 'UserData\MelonPreferences.cfg'
$cfgOriginal = if (Test-Path -LiteralPath $cfg) { [IO.File]::ReadAllText($cfg) } else { $null }
if (-not $NoStart) {
    $text = if ($cfgOriginal) { $cfgOriginal } else { '' }
    if ($text -match '(?m)^\[Personnel_01_Main\]') {
        if ($text -match '(?ms)^\[Personnel_01_Main\][^\[]*?^LoadExamplePack\s*=') {
            $text = [regex]::Replace($text, '(?ms)(^\[Personnel_01_Main\][^\[]*?^LoadExamplePack\s*=\s*)\w+', '${1}true')
        } else {
            $text = $text -replace '(?m)^\[Personnel_01_Main\]\s*$', "[Personnel_01_Main]`r`nLoadExamplePack = true"
        }
    } else {
        $text = $text.TrimEnd() + "`r`n`r`n[Personnel_01_Main]`r`nLoadExamplePack = true`r`n"
    }
    [IO.File]::WriteAllText($cfg, $text)
    Write-Host "set Personnel_01_Main.LoadExamplePack = true in $cfg"
}

try {
    Write-Pack $parkedDir 'parkedprobe' 'Parked Probe'
    Write-Pack $liveDir 'parkedprobelive' 'Live Probe'
    if ($examplesWasThere) {
        if (Test-Path -LiteralPath $examplesParked) { throw "$examplesParked already exists; move it away first" }
        Rename-Item -LiteralPath $examples -NewName 'Examples.parked-perf'
        Write-Host "parked $examples"
    } else {
        Write-Host "no Examples pack in the slot - the Examples check runs without it"
    }

    if (-not $NoStart) {
        & pwsh -File $session -Slot $Slot -Profile $Profile -Build (Split-Path $repo -Leaf) -Mods Personnel -Save 0 -NoApp
        if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
    }

    $log = Join-Path $gameRoot 'MelonLoader\Latest.log'
    $lines = @(Get-Content -LiteralPath $log | Where-Object { $_ -match '\[Personnel\]' })
    Write-Host "$($lines.Count) Personnel log lines in $log"
    $skip = $lines | Where-Object { $_ -match "Pack 'ParkedProbe\.Parked-Perf': parked \(\.parked-perf\) - skipped\." }
    $loadedParked = $lines | Where-Object { $_ -match "Pack 'ParkedProbe\.Parked-Perf' \(" -or $_ -match 'parkedprobe_parked_probe' }
    $loadedLive = $lines | Where-Object { $_ -match "Pack 'ParkedProbeLive' \(parkedprobelive\): 1 NPC" }
    $exSkip = $lines | Where-Object { $_ -match "Pack 'Examples\.parked-perf': parked \(\.parked-perf\) - skipped\." }
    $exKept = $lines | Where-Object { $_ -match 'Example pack is parked \(\.parked-perf\) - not writing a new one\.' }
    $exWritten = $lines | Where-Object { $_ -match 'Wrote example NPC pack|Replaced an outdated bundled example pack' }
    $exLoaded = $lines | Where-Object { $_ -match "Pack 'Examples(\.parked-perf)?' \(" }

    Report 'parked-pack-skip-logged' ([bool]$skip) 'no skip line for ParkedProbe.Parked-Perf'
    if ($skip) { Write-Host "  $(@($skip)[0].Trim())" }
    Report 'parked-pack-not-loaded' (-not $loadedParked) "parked pack loaded: $(@($loadedParked)[0])"
    Report 'live-pack-loaded' ([bool]$loadedLive) 'control pack ParkedProbeLive did not load - the loader did not run on this build'
    if ($examplesWasThere) {
        Report 'parked-examples-skip-logged' ([bool]$exSkip) 'no skip line for Examples.parked-perf'
        Report 'parked-examples-guard-logged' ([bool]$exKept) 'no "Example pack is parked" line (LoadExamplePack on?)'
        Report 'parked-examples-not-rewritten' (-not $exWritten -and -not (Test-Path -LiteralPath $examples)) `
            "the example pack was written again next to its parked copy: $(@($exWritten)[0])"
        Report 'parked-examples-not-loaded' (-not $exLoaded) "example pack loaded: $(@($exLoaded)[0])"
    }

    $dll = Join-Path $gameRoot 'Mods\Personnel.dll'
    $built = Get-ChildItem -LiteralPath (Join-Path $repo 'bin') -Recurse -Filter 'Personnel.dll' -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if ((Test-Path -LiteralPath $dll) -and $built) {
        $h1 = (Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash
        $h2 = (Get-FileHash -LiteralPath $built.FullName -Algorithm SHA256).Hash
        Report 'deployed-dll-is-this-build' ($h1 -eq $h2) "slot $h1, build $h2 ($($built.FullName))"
        Write-Host "  Personnel.dll SHA256 $h1"
    } else {
        Report 'deployed-dll-is-this-build' $false "missing: slot dll $dll or build output under $repo\bin"
    }
}
finally {
    if (-not $NoStart -and $null -ne $cfgOriginal) {
        [IO.File]::WriteAllText($cfg, $cfgOriginal)
        Write-Host "restored $cfg"
    }
    foreach ($d in @($parkedDir, $liveDir)) {
        if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force; Write-Host "removed probe pack $d" }
    }
    if ($examplesWasThere -and (Test-Path -LiteralPath $examplesParked)) {
        if (Test-Path -LiteralPath $examples) {
            Write-Warning "both $examples and $examplesParked exist; left both for a look"
        } else {
            Rename-Item -LiteralPath $examplesParked -NewName 'Examples'
            Write-Host "restored $examples"
        }
    }
}

Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

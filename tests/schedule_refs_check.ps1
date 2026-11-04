# Checks the warning for schedule steps whose seat set or building is not in the scene, and that a step with a valid
# seat set really seats the NPC. Writes two probe packs into the slot, starts the slot with this worktree's build and
# save 5 (the check runs when an NPC is created in Main), then reads the log and watches the seated probe NPC.
#
#     pwsh -File tests/schedule_refs_check.ps1                  # first free slot, builds and starts it
#     pwsh -File tests/schedule_refs_check.ps1 -Slot mptest2
#     pwsh -File tests/schedule_refs_check.ps1 -NoStart         # game already in save 5 with the probe packs loaded
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The probe packs are removed afterwards; the game
# keeps running.
param(
    [string]$Slot = '',
    [switch]$NoStart,
    [int]$SitTimeoutSeconds = 150
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $repo '..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

# Diner outdoor seat set; the chair below it carries only an AvatarSeat.
$seatSet = 'Map/Hyland Point/Region_Downtown/Diner/Round Outdoor Set'
$seatX = -1.38; $seatZ = 44.92

function Npc([string]$name, [string]$seat, [string]$building) {
    return [ordered]@{
        name = $name
        appearance = @{ height = 1.0 }
        spawn = @{ x = 4.0; y = 1.95; z = 40.0; physical = $true }
        schedule = @(
            [ordered]@{ type = 'sit'; time = '06:00'; duration = 900; seatSetPath = $seat },
            [ordered]@{ type = 'stayInBuilding'; time = '21:30'; duration = 60; building = $building }
        )
    }
}

function Write-Pack([string]$dir, [string]$packId, $npc) {
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $pack = [ordered]@{ schemaVersion = 3; name = $packId; packId = $packId; autoRegister = $true; npcs = @($npc) }
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value ($pack | ConvertTo-Json -Depth 8) -Encoding utf8
    Write-Host "wrote probe pack $dir"
}

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
$badDir = Join-Path $packs 'SchedRefProbeBad'
$okDir = Join-Path $packs 'SchedRefProbeOk'
$badId = 'schedrefbad_bad_probe'
$okId = 'schedrefok_ok_probe'

try {
    if (-not $NoStart) {
        Write-Pack $badDir 'schedrefbad' (Npc 'Bad Probe' "$seatSet/Outdoor chair" 'Hyland Medical')
        Write-Pack $okDir 'schedrefok' (Npc 'Ok Probe' $seatSet "Hyland Medical Doctor's Office")
        & pwsh -File $session -Slot $Slot -Build (Split-Path $repo -Leaf) -Save 5 -NoApp
        if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
    }

    $log = Join-Path $gameRoot 'MelonLoader\Latest.log'
    $lines = @(Get-Content -LiteralPath $log | Where-Object { $_ -match '\[Personnel\]' })
    Write-Host "$($lines.Count) Personnel log lines in $log"
    $badSeat = $lines | Where-Object { $_ -match [regex]::Escape("'$badId' (Bad Probe) schedule action 'sit' at 0600: seatSetPath '$seatSet/Outdoor chair' matches no seat set") }
    $badBld = $lines | Where-Object { $_ -match [regex]::Escape("'$badId' (Bad Probe) schedule action 'stayInBuilding' at 2130: building 'Hyland Medical' is not in the scene") }
    $badSum = $lines | Where-Object { $_ -match [regex]::Escape("'$badId': 2 of 2 sit/stay step(s) point at") }
    $okSum = $lines | Where-Object { $_ -match [regex]::Escape("'$okId': 2 sit/stay step(s) checked, every seat and building found.") }
    $okWarn = $lines | Where-Object { $_ -match [regex]::Escape("'$okId' (Ok Probe) schedule action") }

    Report 'broken-seat-warned' ([bool]$badSeat) 'no warning naming the NPC and the seat path'
    if ($badSeat) { Write-Host "  $(@($badSeat)[0].Trim())" }
    Report 'broken-building-warned' ([bool]$badBld) 'no warning naming the NPC and the building'
    Report 'broken-summary-warned' ([bool]$badSum) 'no summary line for the broken NPC'
    Report 'valid-refs-quiet' ([bool]$okSum -and -not $okWarn) "summary: $([bool]$okSum), warning: $(@($okWarn)[0])"

    # The valid probe spawns a few metres from the seat set at 12:00 and should be seated within the timeout.
    & python $s1 --slot $Slot time 1200 | Out-Null
    $best = $null; $deadline = (Get-Date).AddSeconds($SitTimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $out = (& python $s1 --slot $Slot run "personnel npcs $okId" 2>&1 | Out-String)
        $m = [regex]::Match($out, [regex]::Escape($okId) + '.* at (-?[\d.]+), (-?[\d.]+), (-?[\d.]+)')
        if ($m.Success) {
            $d = [math]::Sqrt([math]::Pow([double]$m.Groups[1].Value - $seatX, 2) + [math]::Pow([double]$m.Groups[3].Value - $seatZ, 2))
            if ($null -eq $best -or $d -lt $best) { $best = $d }
            if ($d -lt 2.0) { break }
        } else { Write-Host "  no position line: $($out.Trim())" }
        Start-Sleep -Seconds 5
    }
    Report 'valid-seat-reached' ($null -ne $best -and $best -lt 2.0) "closest approach $best m to the seat set"
    if ($null -ne $best) { Write-Host ("  closest approach {0:N2} m" -f $best) }
}
finally {
    if (-not $NoStart) {
        foreach ($d in @($badDir, $okDir)) {
            if (Test-Path -LiteralPath $d) { Remove-Item -LiteralPath $d -Recurse -Force; Write-Host "removed probe pack $d" }
        }
    }
}
if ($script:failed -gt 0) { Write-Host "$script:failed check(s) failed"; exit 1 }
Write-Host 'all checks passed'

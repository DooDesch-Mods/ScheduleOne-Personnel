# Checks where pack NPCs with a body and no spawn.position start (#104). Without the fix they stand at the world
# origin, the alley behind the Central gas mart. Writes a probe pack with four such NPCs, starts the slot with this
# worktree's build and save 5, then reads the log and the live positions:
#   Walk Probe  first step walkTo                -> starts at the walkTo position (also wears clothes and a hat)
#   Stay Probe  first step stayInBuilding        -> starts at the building's door
#   Seat Probe  first step sit                   -> starts at the seat set
#   Lone Probe  spawn.physical, no schedule      -> region stand point, with a warning naming pack and NPC
# Every other physical Personnel NPC in the slot (the probe packs of other checks) must not stand at the origin either.
#
#     pwsh -File tests/spawn_place_check.ps1                       # first free slot, builds and starts it
#     pwsh -File tests/spawn_place_check.ps1 -Slot mptest2 -ShotDir F:\path\evidence
#     pwsh -File tests/spawn_place_check.ps1 -NoStart -Slot mptest # game already in save 5 with the probe pack
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The game keeps running.
param(
    [string]$Slot = '',
    [switch]$NoStart,
    [int]$Save = 5,
    [string]$ShotDir = '',
    # A physical NPC within this many metres (x/z) of the world origin counts as standing at the origin.
    [double]$OriginRadius = 3.0
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

# Console command through the bridge; returns the log lines it wrote.
function Run([string]$cmd) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run $cmd *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for '$cmd'" }
    return @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ })
}

function Dist2([double[]]$a, [double[]]$b) { [math]::Sqrt([math]::Pow($a[0] - $b[0], 2) + [math]::Pow($a[2] - $b[2], 2)) }

$walkTo = @(-64.25, 1.07, 84.49)
$seatSet = 'Map/Hyland Point/Region_Downtown/Diner/Round Outdoor Set'
$seatAt = @(-1.38, 0, 44.92)
$building = "Hyland Medical Doctor's Office"
$packId = 'spawnplace'

$acqArgs = @('-I', $slotsPy, '--json', 'acquire', '--mod', 'Personnel')
if ($Slot) { $acqArgs += @('--slot', $Slot) }
$lease = (& python @acqArgs | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $lease) { throw "slots.py acquire failed (exit $LASTEXITCODE)" }
if (-not $Slot) { $Slot = @($lease.lease.slots)[0] }
if (-not $Slot) { $Slot = @($lease.slots)[0] }
if (-not $Slot) { throw "no slot in the lease: $($lease | ConvertTo-Json -Depth 5 -Compress)" }
$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
$gameRoot = $table.$Slot.root
$script:log = Join-Path $gameRoot 'MelonLoader\Latest.log'
Write-Host "slot $Slot -> $gameRoot"

if (-not $NoStart) {
    $dir = Join-Path $gameRoot 'UserData\Personnel\Packs\SpawnPlaceProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $clothes = [ordered]@{
        height = 1.0; skinColor = '#8D5524'; hairPath = 'Avatar/Hair/Mohawk/Mohawk'; hairColor = '#C0392B'
        bodyLayers = @([ordered]@{ path = 'Avatar/Layers/Top/T-Shirt'; tint = '#2E86C1' }, [ordered]@{ path = 'Avatar/Layers/Bottom/Jeans'; tint = '#1B2631' })
        accessories = @([ordered]@{ path = 'Avatar/Accessories/Head/BucketHat/BucketHat'; color = '#F1C40F' })
    }
    $npcs = @(
        [ordered]@{ name = 'Walk Probe'; appearance = $clothes
            schedule = @([ordered]@{ type = 'walkTo'; time = '06:00'; position = $walkTo }) },
        [ordered]@{ name = 'Stay Probe'; appearance = @{ height = 1.0 }
            schedule = @([ordered]@{ type = 'stayInBuilding'; time = '06:00'; duration = 60; building = $building }) },
        [ordered]@{ name = 'Seat Probe'; appearance = @{ height = 1.0 }
            schedule = @([ordered]@{ type = 'sit'; time = '06:00'; duration = 900; seatSetPath = $seatSet }) },
        [ordered]@{ name = 'Lone Probe'; appearance = @{ height = 1.0 }; spawn = @{ physical = $true } }
    )
    $pack = [ordered]@{ schemaVersion = 3; name = 'Spawn Place Probe'; packId = $packId; autoRegister = $true; npcs = $npcs }
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value ($pack | ConvertTo-Json -Depth 8) -Encoding utf8
    Write-Host "wrote probe pack $dir"
    & pwsh -NoProfile -File $session -Slot $Slot -Build (Split-Path $repo -Leaf) -Mods Personnel -Save $Save -NoApp -Purpose '104 spawn place'
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
    & python -I $s1 --slot $Slot wait-ready --timeout 300
    if ($LASTEXITCODE -ne 0) { throw 'the save did not load' }
    Start-Sleep -Seconds 5
}

$dll = Join-Path $gameRoot 'Mods\Personnel.dll'
Write-Host "  Personnel.dll SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)"

# 1. One placement line per probe NPC.
$spawnLines = @(Get-Content -LiteralPath $script:log | Where-Object { $_ -match '\[spawn\] ' })
foreach ($l in $spawnLines) { Write-Host "  $($l.Trim())" }
function HasLine([string]$pattern) { return [bool](@($spawnLines | Where-Object { $_ -match $pattern }).Count) }
Report 'walk probe starts at its walkTo step' (HasLine "'${packId}_walk_probe': no spawn.position - placed at the schedule step 'walkTo' at 0600") 'no placement line'
Report 'stay probe starts at the building door' (HasLine "'${packId}_stay_probe': no spawn.position - placed at the schedule step 'stayInBuilding' at 0600 \(door of '$([regex]::Escape($building))'\)") 'no placement line'
Report 'seat probe starts at the seat set' (HasLine "'${packId}_seat_probe': no spawn.position - placed at the schedule step 'sit' at 0600 \(seat set ") 'no placement line'
Report 'lone probe warned with pack and NPC id' (HasLine "pack '[^']+' NPC '${packId}_lone_probe': no spawn.position and no schedule step with a place - placed at the stand point") 'no warning line'
Report 'lone probe placed (fallback)' (HasLine "'${packId}_lone_probe': no spawn.position - placed at the stand point") 'no placement line'

# 2. Live positions: no physical Personnel NPC at the origin; walkTo NPCs at their walk point.
$npcLines = @(Run 'personnel npcs' | Where-Object { $_ -match ' physical.* at -?[\d.]+, -?[\d.]+, -?[\d.]+' })
$pos = @{}
foreach ($l in $npcLines) {
    $m = [regex]::Match($l, '\s(\S+) \(.*\) physical.* at (-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\s*$')
    if ($m.Success) { $pos[$m.Groups[1].Value] = @([double]$m.Groups[2].Value, [double]$m.Groups[3].Value, [double]$m.Groups[4].Value) }
}
Write-Host "  $($pos.Count) physical Personnel NPC(s) with a live position:"
foreach ($k in ($pos.Keys | Sort-Object)) { Write-Host ("    {0} at {1}" -f $k, ($pos[$k] -join ', ')) }
Report 'physical NPCs found' ($pos.Count -ge 4) "only $($pos.Count)"
$atOrigin = @($pos.Keys | Where-Object { (Dist2 $pos[$_] @(0, 0, 0)) -le $OriginRadius })
Report 'no physical Personnel NPC at the origin' ($atOrigin.Count -eq 0) ($atOrigin -join ', ')
foreach ($id in @("${packId}_walk_probe", 'mapprobe_mara_marker', 'sampleprobe_sam_probe', 'sampleprobe_dina_probe', 'unlockprobe_una_probe')) {
    if (-not $pos.ContainsKey($id)) { if ($id -like "${packId}_*") { Report "$id has a position" $false 'not listed' } else { Write-Host "  SKIP $id (pack not in this slot)" }; continue }
    $d = Dist2 $pos[$id] $walkTo
    Report ("$id at its walkTo point ({0:0.0} m)" -f $d) ($d -le 3) ($pos[$id] -join ', ')
}
if ($pos.ContainsKey("${packId}_seat_probe")) {
    $d = Dist2 $pos["${packId}_seat_probe"] $seatAt
    Report ("seat probe near its seat set ({0:0.0} m)" -f $d) ($d -le 6) ($pos["${packId}_seat_probe"] -join ', ')
}

# 3. Clothes: the walk probe's jeans and hat are active objects on its avatar (Debug build only), and a screenshot.
# A far avatar is culled to an impostor and reports every object as off, so the player stands next to it first.
if ($pos.ContainsKey("${packId}_walk_probe")) {
    $p = $pos["${packId}_walk_probe"]
    & python -I $s1 --slot $Slot tp ($p[0] + 3) ($p[1] + 0.2) ($p[2] + 3) --yaw 225 *> $null
    Start-Sleep -Seconds 3
}
$avatar = @(Run "personnel avatar ${packId}_walk_probe" | Where-Object { $_ -match 'avatar ' })
foreach ($l in $avatar) { Write-Host "  $($l.Trim())" }
$live = @($avatar | Where-Object { $_ -match 'live objects' })
if ($live.Count) {
    Report 'walk probe avatar not culled up close' (-not ($avatar -match 'IsCulled=True')) 'still culled'
    Report 'walk probe wears its jeans' ($live[0] -match 'jeans\[[^\]]*,on\]') 'jeans not active'
    Report 'walk probe wears its hat' ($live[0] -match 'buckethat\[[^\]]*,on\]') 'BucketHat not active'
} else { Write-Host "  SKIP avatar probe (Release build)" }
# Camera shot of one NPC from its current position (NPCs walk, so the position is read again right before).
function Shot([string]$id, [string]$file) {
    $l = @(Run "personnel npcs $id" | Where-Object { $_ -match "\s$([regex]::Escape($id)) \(.* at -?[\d.]+, -?[\d.]+, -?[\d.]+" })
    if (-not $l.Count) { Write-Host "  SKIP shot of $id (not listed)"; return }
    $m = [regex]::Match($l[0], ' at (-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\s*$')
    $q = @([double]$m.Groups[1].Value, [double]$m.Groups[2].Value, [double]$m.Groups[3].Value)
    & python -I $s1 --slot $Slot tp ($q[0] + 3) ($q[1] + 0.2) ($q[2] + 3) --yaw 225 *> $null
    Start-Sleep -Seconds 2
    $eye = @(($q[0] + 2.5), ($q[1] + 1.8), ($q[2] + 2.5)); $target = @($q[0], ($q[1] + 1.0), $q[2])
    $argsJson = (@{ eye = $eye; target = $target; sight = 'off'; settleFrames = 10 } | ConvertTo-Json -Compress)
    $out = (& python -I $call --slot $Slot camera_shot $argsJson --timeout 20000 2>&1 | Out-String).Trim()
    & python -I $call --slot $Slot camera_release '{}' --timeout 8000 *> $null
    $shot = $null
    try { $r = $out | ConvertFrom-Json; $shot = if ($r.result.path) { $r.result.path } else { $r.result.shot } } catch { Write-Host "  camera_shot answer not JSON: $out" }
    if ($shot -and $ShotDir) {
        New-Item -ItemType Directory -Force -Path $ShotDir | Out-Null
        $dest = Join-Path $ShotDir $file
        Copy-Item -LiteralPath $shot -Destination $dest -Force
        Write-Host "  screenshot $dest"
    } elseif ($shot) { Write-Host "  screenshot $shot" } else { Write-Host "  no screenshot for $id" }
}
# The debug probes of other packs share the walkTo point and their naked bodies cover the walk probe's clothes, so
# it is brought 8 m away from them first (after the position checks above).
& python -I $s1 --slot $Slot tp ($walkTo[0] + 6) ($walkTo[1] + 0.2) $walkTo[2] --yaw 90 *> $null
Start-Sleep -Seconds 1
$bring = @(Run "personnel cust ${packId}_walk_probe bring" | Where-Object { $_ -match 'cust bring' })
Write-Host "  $(if ($bring.Count) { $bring[0].Trim() } else { 'cust bring wrote no line' })"
Shot "${packId}_walk_probe" 'walk-probe-clothes.png'
# Comparison: a pack NPC of another pack with the same jeans layer.
Shot 'more_npcs_jack_cimicato' 'compare-jack-cimicato.png'

Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

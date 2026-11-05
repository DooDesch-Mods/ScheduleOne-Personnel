# Before/after pictures of a vanilla replacement: a six-view rig of the target dealer as the game ships him, then the
# same save with the probe pack (mohawk, bucket hat, blazer). Both runs set the clock to the same time first.
#
#     pwsh -File tests/replace_shots.ps1 -Slot mptest2 -Out F:/Projects/Mods/Schedule1/_drafts/replace-shots
#
# Prints PASS or FAIL per step and the paths of both rigs. The probe pack is removed at the end.
param(
    [string]$Slot,
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [int]$Save = 5,
    [string]$Target = 'benji_coleman',
    [string]$Object = 'Benji',
    [string]$Clock = '1200',
    [double]$Radius = 2.6,
    [string]$Out = (Join-Path $PSScriptRoot '..\..\_drafts\replace-shots')
)
$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..')
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$session = Join-Path $root 'Workspace\tools\session\session.ps1'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$script:failed = 0
$inv = [Globalization.CultureInfo]::InvariantCulture
New-Item -ItemType Directory -Force -Path $Out | Out-Null

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function Start-Slot([string[]]$extra) {
    $sessionArgs = @('-Slot', $Slot, '-Profile', $Profile, '-NoApp', '-Save', "$Save") + $extra
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
    & python -I $s1 --slot $Slot wait-ready --timeout 300 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "wait-ready failed (exit $LASTEXITCODE)" }
}

function S1([string[]]$argv) {
    $text = (& python -I $s1 --slot $Slot @argv 2>$null | Out-String)
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py $($argv -join ' ') exited $LASTEXITCODE" }
    return $text
}

# A spot a few metres from the NPC whose highest ground hit is at his feet: not on a car, a roof or inside a wall.
function FreeSpot($p) {
    foreach ($o in @(@(4, 0), @(-4, 0), @(0, 4), @(0, -4), @(3, 3), @(-3, 3), @(3, -3), @(-3, -3), @(6, 0), @(-6, 0))) {
        $x = $p[0] + $o[0]; $z = $p[2] + $o[1]
        $g = (S1 @('ground', $x.ToString($inv), $z.ToString($inv))) | ConvertFrom-Json
        if ($g -and [math]::Abs([double]$g.ground - [double]$p[1]) -lt 0.3) { return @($x, [double]$g.ground, $z) }
    }
    Write-Host "  no free ground next to $($p[0].ToString($inv)) $($p[2].ToString($inv)); standing 4 m east"
    return @(($p[0] + 4), [double]$p[1], $p[2])
}

function Part([string]$part) {
    $f = (S1 @('find', $part, '--under', $Object, '-n', '20', '--sort', 'none')) | ConvertFrom-Json
    if (-not $f -or $f.matched -lt 1) { return $null }
    return @($f.objects | Where-Object { $_.path -like 'NPCs/*' })[0]
}

# The dealer walks his route. Far from the player he is drawn as a flat impostor (a replacement borrows its voice
# donor's), and his body renderers keep stale bounds. So the player follows the impostor until the body is active,
# then time is frozen and the rig aims at the body's bounds.
function Rig([string]$name) {
    $null = S1 @('run', 'settimescale', '1')
    $null = S1 @('time', $Clock)
    $null = S1 @('fog', 'off')
    $body = $null
    for ($i = 0; $i -lt 8; $i++) {
        $body = Part 'Body_LOD0'
        if ($body -and $body.active) { break }
        $far = Part 'Impostor'
        if (-not $far) { Write-Host "  no body and no impostor under $Object"; break }
        $p = $far.position
        Write-Host "  $Object far view at $($p[0].ToString($inv)) $($p[2].ToString($inv)), moving the player there"
        $spot = FreeSpot $p
        $null = S1 @('tp', $spot[0].ToString($inv), ($spot[1] + 1).ToString($inv), $spot[2].ToString($inv))
        Start-Sleep -Seconds 2
    }
    if (-not $body -or -not $body.active) { Report "$name rig" $false "the body of $Object never became active"; return $null }
    $null = S1 @('run', 'settimescale', '0')
    $body = Part 'Body_LOD0'
    $c = $body.bounds.center
    Write-Host "  $Object body at $($c[0].ToString($inv)) $($c[1].ToString($inv)) $($c[2].ToString($inv))"
    $rig = (S1 @('cam', 'rig', '--target', $c[0].ToString($inv), ($c[1] + 0.2).ToString($inv), $c[2].ToString($inv),
                 '--radius', $Radius.ToString($inv), '--height', '0.3', '--hud', 'hide')) | ConvertFrom-Json
    $null = S1 @('run', 'settimescale', '1')
    if (-not $rig -or -not $rig.path) { Report "$name rig" $false 'no png path in the cam rig answer'; return $null }
    $dest = Join-Path $Out "$name.png"
    Copy-Item -LiteralPath $rig.path -Destination $dest -Force
    Report "$name rig" $true $dest
    return $dest
}

$acqArgs = @('-I', $slotsPy, '--json', 'acquire', '--mod', 'Personnel')
if ($Slot) { $acqArgs += @('--slot', $Slot) }
$lease = (& python @acqArgs | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $lease) { throw "slots.py acquire failed (exit $LASTEXITCODE)" }
if (-not $Slot) { $Slot = @($lease.lease.slots)[0] }
$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
$gameRoot = $table.$Slot.root
$packDir = Join-Path $gameRoot 'UserData\Personnel\Packs\ReplaceProbe'
Write-Host "slot $Slot -> $gameRoot"

try {
    if (Test-Path -LiteralPath $packDir) { Remove-Item -LiteralPath $packDir -Recurse -Force; Write-Host "removed an old probe pack $packDir" }
    $extra = @('-Build', (Split-Path $repo -Leaf))
    if ($Profile -eq 'stack') { $extra += @('-Mods', 'Personnel') }
    Start-Slot $extra
    $null = Rig 'before'

    New-Item -ItemType Directory -Force -Path $packDir | Out-Null
    $json = @"
{ "schemaVersion": 4, "name": "Replace Probe", "packId": "replaceprobe",
  "npcs": [
    { "name": "Replace Probe", "replaces": "$Target",
      "voice": { "from": "kyle_cooley", "pitch": 1.2 },
      "appearance": {
        "gender": 0.0, "height": 1.05, "weight": 0.6, "skinColor": "#6B3F2A",
        "hairPath": "Avatar/Hair/Mohawk/Mohawk", "hairColor": "#C0392B",
        "faceLayers": [ { "path": "Avatar/Layers/Face/Face_SmugPout", "tint": "#000000" } ],
        "bodyLayers": [ { "path": "Avatar/Layers/Top/T", "tint": "#2E86C1" },
                        { "path": "Avatar/Layers/Bottom/Jeans", "tint": "#1B2631" } ],
        "accessories": [ { "path": "Avatar/Accessories/Head/BucketHat/BucketHat", "color": "#F1C40F" },
                         { "path": "Avatar/Accessories/Chest/Blazer/Blazer", "color": "#7D3C98" },
                         { "path": "Avatar/Accessories/Feet/CombatBoots/CombatBoots", "color": "#222222" } ] },
      "dealer": { "cut": 0.5, "signingFee": 1234 } } ] }
"@
    Set-Content -LiteralPath (Join-Path $packDir 'manifest.json') -Value $json -Encoding utf8
    Write-Host "wrote probe pack $packDir"
    Start-Slot @('-KeepSlot', '-Template', 'none')
    $null = Rig 'after'
}
finally {
    if (Test-Path -LiteralPath $packDir) {
        try { Remove-Item -LiteralPath $packDir -Recurse -Force; Write-Host "removed probe pack $packDir" }
        catch { Write-Host "removing $packDir failed: $_" }
    }
}

Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

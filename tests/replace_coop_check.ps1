# Checks a vanilla replacement in co-op: host and client carry the same probe pack, whose entry replaces the target
# dealer with a distinct look (mohawk, bucket hat, blazer), a borrowed voice and a 50 % cut. tests/coop_up.ps1 brings
# up a joined pair with this worktree's build; then "personnel replace probe" runs on both sides:
#
#   1. the client joins (identical packs, no hang on "Loading world...")
#   2. host and client both show the replaced look and voice on the vanilla NPC, which keeps its vanilla name
#   3. the host's dealer cut is the replaced one (host authority)
#   4. no replacement errors in either log
#
#     pwsh -File tests/replace_coop_check.ps1
#     pwsh -File tests/replace_coop_check.ps1 -Profile main
#
# Prints PASS or FAIL per check and exits 1 when any check fails. tests/coop_down.ps1 stops the pair at the end and the
# probe pack is removed from both slots.
param(
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [string]$Target = 'benji_coleman',
    [string]$VoiceFrom = 'kyle_cooley',
    [switch]$KeepRunning
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function ReadLogFrom([string]$log, [long]$offset) {
    $fs = [IO.File]::Open($log, 'Open', 'Read', 'ReadWrite')
    try { $fs.Seek($offset, 'Begin') | Out-Null; return (New-Object IO.StreamReader($fs)).ReadToEnd() } finally { $fs.Dispose() }
}

function Probe([string]$slot, [string]$slotRoot) {
    $log = Join-Path $slotRoot 'MelonLoader\Latest.log'
    $before = (Get-Item -LiteralPath $log).Length
    & python -I $s1 --slot $slot run personnel replace probe $Target *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE on $slot" }
    $line = [string](@((ReadLogFrom $log $before) -split "`r?`n" | Where-Object { $_ -match '\[replace-probe\].* found=' }) | Select-Object -Last 1)
    Write-Host "  $slot $($line.Trim())"
    return $line
}

function Field([string]$text, [string]$key) { return [regex]::Match($text, "(?<= $key=)\S*").Value }

$json = @"
{ "schemaVersion": 4, "name": "Replace Probe", "packId": "replaceprobe",
  "npcs": [
    { "name": "Replace Probe", "replaces": "$Target",
      "voice": { "from": "$VoiceFrom", "pitch": 1.2 },
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

# A session holds one lease; the co-op pair needs a fresh one.
$mine = (& python -I $slotsPy --json mine | ConvertFrom-Json).lease
if ($mine -and @($mine.slots).Count -lt 2) {
    & python -I $slotsPy release | Out-Host
    Write-Host "released the single-slot lease $(@($mine.slots) -join ',')"
}
$lease = (& python -I $slotsPy --json acquire --coop --via duo.ps1 --mod Personnel | ConvertFrom-Json).lease
if (-not $lease -or @($lease.slots).Count -lt 2) { Write-Host 'FAIL no co-op pair leased'; exit 1 }
$hostName = @($lease.slots)[0]; $clientName = @($lease.slots)[-1]
$hostRoot = $lease.table.$hostName.root; $clientRoot = $lease.table.$clientName.root
Write-Host "host $hostName ($hostRoot), client $clientName ($clientRoot)"

# Another pack that replaces the target would compete with the probe; coop_up copies packs between the slots.
foreach ($slotRoot in @($hostRoot, $clientRoot)) {
    $packs = Join-Path $slotRoot 'UserData\Personnel\Packs'
    $rivals = @(Get-ChildItem -LiteralPath $packs -Recurse -Filter manifest.json -ErrorAction SilentlyContinue |
                Where-Object { $_.Directory.Name -ne 'ReplaceProbe' -and (Get-Content -Raw -LiteralPath $_.FullName) -match "`"replaces`"\s*:\s*`"$Target`"" })
    if ($rivals.Count -gt 0) { Write-Host "FAIL another pack replaces $Target in ${slotRoot}: $(($rivals | ForEach-Object { $_.Directory.Name }) -join ', ')"; exit 1 }
}

$probeDirs = @()
foreach ($slotRoot in @($hostRoot, $clientRoot)) {
    $dir = Join-Path $slotRoot 'UserData\Personnel\Packs\ReplaceProbe'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    Set-Content -LiteralPath (Join-Path $dir 'manifest.json') -Value $json -Encoding utf8
    $probeDirs += $dir
    Write-Host "wrote probe pack $dir"
}

try {
    $logStart = @{}
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'coop_up.ps1') -Profile $Profile | Out-Host
    Report 'client joins with identical packs' ($LASTEXITCODE -eq 0) "coop_up.ps1 exited $LASTEXITCODE"
    if ($LASTEXITCODE -ne 0) { throw 'no joined pair' }

    $h = Probe $hostName $hostRoot
    $c = Probe $clientName $clientRoot
    foreach ($side in @(@('host', $h), @('client', $c))) {
        $n, $p = $side
        Report "$n`: replacement applied" ((Field $p 'applied') -eq 'True') "applied=$(Field $p 'applied')"
        Report "$n`: vanilla name kept" ((Field $p 'name') -match '^Benji') "name=$(Field $p 'name')"
        Report "$n`: replaced hair" ((Field $p 'hair') -match 'mohawk') "hair=$(Field $p 'hair')"
        Report "$n`: replaced clothing" ((Field $p 'wornIds') -match 'buckethat' -and (Field $p 'wornIds') -match 'blazer') "wornIds=$(Field $p 'wornIds')"
        Report "$n`: replaced voice pitch" ((Field $p 'pitch') -eq '1.2') "pitch=$(Field $p 'pitch')"
    }
    Report 'host: replaced dealer cut' ((Field $h 'cut') -eq '0.5') "cut=$(Field $h 'cut')"
    Report 'client: same voice as the host' ((Field $c 'voice') -eq (Field $h 'voice')) "host $(Field $h 'voice'), client $(Field $c 'voice')"

    foreach ($side in @(@('host', $hostRoot), @('client', $clientRoot))) {
        $n, $r = $side
        $errs = @((ReadLogFrom (Join-Path $r 'MelonLoader\Latest.log') 0) -split "`r?`n" |
                  Where-Object { $_ -match '\[Personnel\]' -and $_ -match '(?i)replace' -and $_ -match '(\[E\]|ERROR|Exception)' })
        Report "$n`: no replacement errors in the log" ($errs.Count -eq 0) (($errs | Select-Object -First 3) -join ' | ')
    }
} finally {
    if (-not $KeepRunning) {
        try { & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'coop_down.ps1') | Out-Host } catch { Write-Host "coop_down failed: $_" }
        foreach ($dir in $probeDirs) {
            try { Remove-Item -LiteralPath $dir -Recurse -Force; Write-Host "removed probe pack $dir" }
            catch { Write-Host "removing $dir failed: $_" }
        }
    }
}

if ($script:failed -eq 0) { Write-Host 'SUMMARY all PASS'; exit 0 }
Write-Host "SUMMARY $script:failed FAIL"
exit 1

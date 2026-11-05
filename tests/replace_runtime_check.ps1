# Checks that a pack entry with "replaces" takes over a vanilla dealer and lets go of it again. Writes a probe pack
# into the slot whose entry replaces the target dealer with a distinct look (mohawk, bucket hat, blazer), a borrowed
# voice and a 50 % cut, starts the slot with this worktree's build and save 5, and reads "personnel replace" probe
# lines from the game log:
#
#   1. the replacement is applied (look, cut, voice) and templates.json lists the target with its vanilla values
#   2. "replace restore" gives back the vanilla look and cut, "replace apply" takes over again
#   3. after save, menu and load the replacement is applied again
#   4. after the pack is removed and the game restarted on the same save, the dealer is vanilla and the save loads
#
#     pwsh -File tests/replace_runtime_check.ps1                       # first free slot, builds and starts it
#     pwsh -File tests/replace_runtime_check.ps1 -Slot mptest2
#     pwsh -File tests/replace_runtime_check.ps1 -Profile main         # mods as on the real installation
#
# Prints PASS or FAIL per check and exits 1 when any check fails. The probe pack is removed at the end; the game
# keeps running.
param(
    [string]$Slot = '',
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [int]$Save = 5,
    [string]$Target = 'benji_coleman',
    # Vanilla NPC whose voice the probe entry borrows.
    [string]$VoiceFrom = 'kyle_cooley'
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

# Runs "personnel replace <args>" and returns its [replace-probe] lines from the game log.
function Replace([string[]]$cmdArgs) {
    $before = (Get-Item -LiteralPath $script:log).Length
    & python -I $s1 --slot $Slot run personnel replace @cmdArgs *> $null
    if ($LASTEXITCODE -ne 0) { Write-Host "  s1.py run exited $LASTEXITCODE for 'replace $($cmdArgs -join ' ')'" }
    $lines = @((ReadLogFrom $before) -split "`r?`n" | Where-Object { $_ -match '\[replace-probe\]' })
    foreach ($l in $lines) { Write-Host "  $($l.Trim())" }
    return ($lines -join "`n")
}

function Field([string]$text, [string]$key) { return [regex]::Match($text, "(?<= $key=)\S*").Value }

function Bridge([string]$command, [string]$argsJson = '{}', [int]$timeoutMs = 12000) {
    $out = (& python -I $call --slot $Slot $command $argsJson --timeout $timeoutMs 2>&1 | Out-String).Trim()
    Write-Host "  bridge $command -> $out"
    return ($LASTEXITCODE -eq 0)
}

function WaitReady([int]$timeout = 900) {
    & python -I $s1 --slot $Slot wait-ready --timeout $timeout | Out-Host
    return ($LASTEXITCODE -eq 0)
}

function Start-Slot([string[]]$extra) {
    $sessionArgs = @('-Slot', $Slot, '-Profile', $Profile, '-NoApp', '-Save', "$Save") + $extra
    & pwsh -NoProfile -File $session @sessionArgs
    if ($LASTEXITCODE -ne 0) { throw "session.ps1 failed (exit $LASTEXITCODE)" }
}

$acqArgs = @('-I', $slotsPy, '--json', 'acquire', '--mod', 'Personnel')
if ($Slot) { $acqArgs += @('--slot', $Slot) }
$lease = (& python @acqArgs | Out-String | ConvertFrom-Json)
if ($LASTEXITCODE -ne 0 -or -not $lease) { throw "slots.py acquire failed (exit $LASTEXITCODE)" }
if (-not $Slot) { $Slot = @($lease.lease.slots)[0] }
if (-not $Slot) { throw "no slot in the lease: $($lease | ConvertTo-Json -Depth 5 -Compress)" }
$table = (& python -I $slotsPy --json table | Out-String | ConvertFrom-Json)
$gameRoot = $table.$Slot.root
$script:log = Join-Path $gameRoot 'MelonLoader\Latest.log'
Write-Host "slot $Slot -> $gameRoot"

$packDir = Join-Path $gameRoot 'UserData\Personnel\Packs\ReplaceProbe'
New-Item -ItemType Directory -Force -Path $packDir | Out-Null
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
Set-Content -LiteralPath (Join-Path $packDir 'manifest.json') -Value $json -Encoding utf8
Write-Host "wrote probe pack $packDir"

try {
    $extra = @('-Build', (Split-Path $repo -Leaf))
    if ($Profile -eq 'stack') { $extra += @('-Mods', 'Personnel') }
    Start-Slot $extra

    # 1. Applied after the load.
    $l = Replace @('list')
    Report 'replacement registered' ($l -match "target=$Target def=\S+ applied=True") "no applied line for $Target"
    $p = Replace @('probe', $Target)
    Report 'target found' ($p -match 'found=true') 'NPC not found'
    Report 'replaced look: hair' ((Field $p 'hair') -match 'mohawk') "hair=$(Field $p 'hair')"
    Report 'replaced look: clothing' ((Field $p 'wornIds') -match 'buckethat' -and (Field $p 'wornIds') -match 'blazer') "wornIds=$(Field $p 'wornIds')"
    Report 'replaced dealer cut' ((Field $p 'cut') -eq '0.5') "cut=$(Field $p 'cut')"
    Report 'replaced voice pitch' ((Field $p 'pitch') -eq '1.2') "pitch=$(Field $p 'pitch')"
    $replacedVoice = Field $p 'voice'

    $c = Replace @('catalog')
    Report 'templates.json written' ($c -match 'catalog=True') 'catalog command failed'
    $tplFile = Join-Path $gameRoot 'UserData\Personify\World\templates.json'
    $tpl = if (Test-Path -LiteralPath $tplFile) { Get-Content -LiteralPath $tplFile -Raw | ConvertFrom-Json } else { $null }
    $entry = $tpl.npcs | Where-Object { $_.id -eq $Target }
    Report 'template lists the target' ([bool]$entry) "no $Target in $tplFile"
    Report 'template marks the replacement' ($entry.replacedBy -match 'replaceprobe') "replacedBy=$($entry.replacedBy)"
    Report 'template keeps the vanilla cut' ($entry -and [double]$entry.npc.dealer.cut -ne 0.5) "cut=$($entry.npc.dealer.cut)"
    Report 'template keeps the vanilla hair' ($entry -and $entry.npc.appearance.hairPath -notmatch 'mohawk') "hairPath=$($entry.npc.appearance.hairPath)"
    $blockedStory = Replace @('blocked', 'uncle_nelson')
    Report 'story NPC blocked' ($blockedStory -match 'blocked=(?!no)\S') 'uncle_nelson not blocked'
    $blockedDealer = Replace @('blocked', $Target)
    Report 'target not blocked' ($blockedDealer -match 'blocked=no') 'target reported as blocked'

    # 2. Restore and apply again.
    Replace @('restore', $Target) | Out-Null
    $r = Replace @('probe', $Target)
    Report 'restore: not applied' ((Field $r 'applied') -eq 'False') "applied=$(Field $r 'applied')"
    Report 'restore: vanilla hair' ((Field $r 'hair') -notmatch 'mohawk') "hair=$(Field $r 'hair')"
    Report 'restore: vanilla cut' ((Field $r 'cut') -ne '0.5') "cut=$(Field $r 'cut')"
    Report 'restore: vanilla voice' ((Field $r 'voice') -ne $replacedVoice -or (Field $r 'pitch') -ne '1.2') "voice=$(Field $r 'voice') pitch=$(Field $r 'pitch')"
    $vanillaCut = Field $r 'cut'
    Replace @('apply') | Out-Null
    $a = Replace @('probe', $Target)
    Report 'apply again' ((Field $a 'applied') -eq 'True' -and (Field $a 'cut') -eq '0.5') "applied=$(Field $a 'applied') cut=$(Field $a 'cut')"

    # 3. Save, menu, load.
    Report 'save' (Bridge 'save_now') 'save_now failed'
    Start-Sleep -Seconds 5
    Report 'return to menu' (Bridge 'return_to_menu') 'return_to_menu failed'
    Start-Sleep -Seconds 10
    $loaded = $false
    for ($i = 0; $i -lt 12 -and -not $loaded; $i++) {
        $loaded = Bridge 'load_save' "{`"slot`": $Save}" 90000
        if (-not $loaded) { Start-Sleep -Seconds 5 }
    }
    Report 'load the save again' $loaded 'load_save failed'
    Report 'game ready after the load' (WaitReady) 'wait-ready failed'
    Start-Sleep -Seconds 5
    $s = Replace @('probe', $Target)
    Report 'applied after save and load' ((Field $s 'applied') -eq 'True' -and (Field $s 'hair') -match 'mohawk' -and (Field $s 'cut') -eq '0.5') "applied=$(Field $s 'applied') hair=$(Field $s 'hair') cut=$(Field $s 'cut')"
    Report 'save before the pack removal' (Bridge 'save_now') 'save_now failed'
    Start-Sleep -Seconds 5

    # 4. Pack removed, same save (no fresh template copy), no new build.
    Remove-Item -LiteralPath $packDir -Recurse -Force
    Write-Host "removed probe pack $packDir"
    Start-Slot @('-KeepSlot', '-Template', 'none')
    $n = Replace @('probe', $Target)
    Report 'pack removed: target found' ($n -match 'found=true') 'NPC not found after the restart'
    Report 'pack removed: not applied' ((Field $n 'applied') -eq 'False') "applied=$(Field $n 'applied')"
    Report 'pack removed: vanilla hair' ((Field $n 'hair') -notmatch 'mohawk') "hair=$(Field $n 'hair')"
    Report 'pack removed: vanilla cut' ((Field $n 'cut') -eq $vanillaCut) "cut=$(Field $n 'cut'), vanilla $vanillaCut"
    $errs = @(Get-Content -LiteralPath $script:log | Where-Object { $_ -match '\[Personnel\].*(replace|replacement)' -and $_ -match '(?i)error|exception' })
    Report 'no replacement errors in the log' ($errs.Count -eq 0) "$($errs.Count) line(s), first: $(@($errs)[0])"
}
finally {
    if (Test-Path -LiteralPath $packDir) { Remove-Item -LiteralPath $packDir -Recurse -Force; Write-Host "removed probe pack $packDir" }
}

$dll = Join-Path $gameRoot 'Mods\Personnel.dll'
if (Test-Path -LiteralPath $dll) { Write-Host "  Personnel.dll SHA256 $((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash)" }
Write-Host "SUMMARY $(if ($script:failed) { "$script:failed FAIL" } else { 'all PASS' })"
exit ([int]($script:failed -gt 0))

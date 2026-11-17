# Co-op check for the join pack comparison (Content/PackSync.cs). The host writes its NPC pack list to the lobby; a
# joining client compares it with its own packs.
#   1. match: with identical packs the host logs the published list, the client logs the match, shows no notice and
#      its log holds no EquippedItemHandler error.
#   2. mismatch: the client restarts without -Remove packs and joins again. Its error line and its notice name every
#      removed pack as missing; a screenshot of the notice is taken the moment the client logs it.
# The removed packs are put back afterwards, also when the check fails.
#
#     pwsh -File tests/pack_mismatch_check.ps1                 # brings the pair up with coop_up.ps1 first
#     pwsh -File tests/pack_mismatch_check.ps1 -NoUp           # pair already running with identical packs
#     pwsh -File tests/pack_mismatch_check.ps1 -Remove SampleProbe,MapProbe -ShotDir _drafts
#
# Prints PASS or FAIL per check and exits 1 when any check fails. Stop the pair afterwards with tests/coop_down.ps1.
param(
    [string[]]$Remove = @('SampleProbe', 'MapProbe'),
    [switch]$NoUp,
    # Skips the identical-packs checks, for a rerun of only the mismatch part against a running host.
    [switch]$SkipMatch,
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [int]$JoinAttempts = 3,
    # Where the notice screenshot is copied to; empty keeps only the path s1.py prints.
    [string]$ShotDir = ''
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function Lines([string]$log, [string]$pattern) {
    if (-not (Test-Path -LiteralPath $log)) { return @() }
    return @(Select-String -LiteralPath $log -Pattern $pattern | ForEach-Object { $_.Line })
}

if (-not $NoUp) {
    & pwsh -File (Join-Path $PSScriptRoot 'coop_up.ps1') -Profile $Profile
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL coop_up - pair did not come up'; exit 1 }
}

$lease = (& python -I $slotsPy --json mine | ConvertFrom-Json).lease
if (-not $lease -or @($lease.slots).Count -lt 2) { Write-Host 'FAIL lease - this session holds no co-op pair'; exit 1 }
$hostSlot = @($lease.slots)[0]
$clientSlot = @($lease.slots)[-1]
$hostLog = Join-Path $lease.table.$hostSlot.root 'MelonLoader\Latest.log'
$client = $lease.table.$clientSlot
$clientLog = Join-Path $client.root 'MelonLoader\Latest.log'
$clientPacks = Join-Path $client.root 'UserData\Personnel\Packs'
$aside = Join-Path $client.root 'UserData\Personnel\_pack_check_aside'

# 1. Identical packs.
if (-not $SkipMatch) {
$published = Lines $hostLog '\[packs\] published \d+ NPC pack'
Report 'match: host publishes its pack list' ($published.Count -gt 0) "no '[packs] published' line in $hostLog"
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline -and (Lines $clientLog '\[packs\] (NPC packs match|NPC packs differ|NPC packs load|the host published no)').Count -eq 0) {
    Start-Sleep -Seconds 2
}
$verdict = Lines $clientLog '\[packs\] (NPC packs match|NPC packs differ|NPC packs load|the host published no)'
Write-Host "client verdict: $($verdict -join ' | ')"
Report 'match: client logs that its packs match the host' (@($verdict | Where-Object { $_ -match 'NPC packs match the host' }).Count -gt 0) "verdict: $($verdict -join ' | ')"
Report 'match: client shows no pack notice' ((Lines $clientLog '\[packs\] notice shown').Count -eq 0) 'a pack notice was shown'
$handlerErrors = (Lines $clientLog 'EquippedItemHandler').Count
Report 'match: no EquippedItemHandler error on the client' ($handlerErrors -eq 0) "$handlerErrors line(s)"
}

# Restarts the client with --join and waits until it is in the world. Screenshots the pack notice as soon as the client
# logs it. Returns @{ Joined; Shot }.
function Join-Client {
    Get-CimInstance Win32_Process -Filter "Name='Schedule I.exe'" |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($client.root, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "stopped client pid $($_.ProcessId)" }
    Start-Sleep -Seconds 4
    $process = Start-Process -FilePath (Join-Path $client.root 'Schedule I.exe') -ArgumentList '--join' -WorkingDirectory $client.root -PassThru
    $result = @{ Joined = $false; Shot = $null }
    $deadline = (Get-Date).AddSeconds(300)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 1
        if ($process.HasExited) { Write-Host "client exited with $($process.ExitCode)"; return $result }
        if (-not $result.Shot -and (Lines $clientLog '\[packs\] notice shown').Count -gt 0) {
            Start-Sleep -Seconds 3
            $out = & python -I $s1 --slot $clientSlot shot 2>&1 | Out-String
            Write-Host "shot: $($out.Trim())"
            $m = [regex]::Match($out, '[A-Za-z]:\\[^\r\n"]+\.png')
            $result.Shot = if ($m.Success) { $m.Value } else { $out.Trim() }
        }
        try {
            $h = Invoke-RestMethod -Uri "http://127.0.0.1:$($client.port)/health" -TimeoutSec 3
            if ($h.scene -eq 'Main' -and $h.inGame -and -not $h.loading) {
                $result.Joined = $true
                # The notice waits until the player sees the world. A client new to the host's save first gets the
                # intro cutscene and the character creator; `personnel intro done` takes it through both.
                $until = (Get-Date).AddSeconds(120)
                $nextSkip = Get-Date
                while (-not $result.Shot -and (Get-Date) -lt $until) {
                    Start-Sleep -Seconds 1
                    if ((Lines $clientLog '\[packs\] notice shown').Count -eq 0 -and (Get-Date) -ge $nextSkip) {
                        $skip = & python -I $s1 --slot $clientSlot run 'personnel intro done' 2>&1 | Out-String
                        Write-Host "intro: $(([regex]::Matches($skip, 'intro: [^\r\n]+') | ForEach-Object Value) -join ' | ')"
                        $nextSkip = (Get-Date).AddSeconds(4)
                    }
                    if ((Lines $clientLog '\[packs\] notice shown').Count -gt 0) {
                        Start-Sleep -Seconds 3
                        $hud = & python -I $s1 --slot $clientSlot run 'personnel hud' 2>&1 | Out-String
                        ([regex]::Matches($hud, 'hud: [^\r\n]+') | ForEach-Object Value) | ForEach-Object { Write-Host $_ }
                        $out = & python -I $s1 --slot $clientSlot shot 2>&1 | Out-String
                        Write-Host "shot: $($out.Trim())"
                        $m = [regex]::Match($out, '[A-Za-z]:\\[^\r\n"]+\.png')
                        $result.Shot = if ($m.Success) { $m.Value } else { $out.Trim() }
                    }
                }
                return $result
            }
            # A station's config RPC that throws inside LoadManager.onLoadComplete ends the vanilla load routine, and the
            # client stays on the loading screen for good. That is a separate join bug; another attempt usually passes.
            if ($h.loading -and (Select-String -LiteralPath $clientLog -Pattern 'ConfigurationReplicator.*ReceiveStringField' -Quiet)) {
                Write-Host 'WARN client load routine died in ConfigurationReplicator.ReceiveStringField (separate join bug), joining again'
                return $result
            }
        } catch { }
    }
    Write-Host 'client not in the world after 300 s'
    return $result
}

# 2. The client lacks packs the host has.
$moved = @()
try {
    Get-CimInstance Win32_Process -Filter "Name='Schedule I.exe'" |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($client.root, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "stopped client pid $($_.ProcessId)" }
    Start-Sleep -Seconds 4
    New-Item -ItemType Directory -Force $aside | Out-Null
    foreach ($name in $Remove) {
        $p = Join-Path $clientPacks $name
        if (Test-Path -LiteralPath $p) { Move-Item -LiteralPath $p -Destination (Join-Path $aside $name); $moved += $name; Write-Host "set aside $name" }
        else { Write-Host "FAIL mismatch: client has no pack $name to remove"; $script:failed++ }
    }
    Write-Host "client packs now: $((Get-ChildItem -LiteralPath $clientPacks -Directory).Name -join ', ')"

    $join = @{ Joined = $false; Shot = $null }
    for ($attempt = 1; $attempt -le $JoinAttempts -and -not $join.Joined; $attempt++) {
        Write-Host "join attempt $attempt of $JoinAttempts"
        $join = Join-Client
    }
    Report 'mismatch: client joins without the packs' $join.Joined "client not in the world after $JoinAttempts attempt(s)"

    $errorLine = @(Lines $clientLog '\[packs\] NPC packs differ from the host') | Select-Object -Last 1
    $noticeLine = @(Lines $clientLog '\[packs\] notice shown') | Select-Object -Last 1
    Write-Host "error line: $errorLine"
    Write-Host "notice line: $noticeLine"
    Report 'mismatch: client writes an error line' ([bool]$errorLine -and $errorLine -match '\[ERROR\] \[Personnel\]') "line: '$errorLine'"
    $missingPart = if ($errorLine -match 'missing ([^;]+);') { $Matches[1] } else { '' }
    foreach ($name in $moved) {
        Report "mismatch: error line names $name as missing" ($missingPart -match [regex]::Escape($name)) "missing part: '$missingPart'"
        Report "mismatch: notice names $name" ([bool]$noticeLine -and $noticeLine -match "Missing: [^`"]*$([regex]::Escape($name))") "notice: $noticeLine"
    }
    Report 'mismatch: notice screenshot taken' ([bool]$join.Shot -and (Test-Path -LiteralPath $join.Shot)) "shot: $($join.Shot)"
    if ($join.Shot -and (Test-Path -LiteralPath $join.Shot) -and $ShotDir) {
        New-Item -ItemType Directory -Force $ShotDir | Out-Null
        $copy = Join-Path $ShotDir 'personnel-pack-notice.png'
        Copy-Item -LiteralPath $join.Shot -Destination $copy -Force
        Write-Host "notice screenshot: $copy"
    }
}
finally {
    Get-CimInstance Win32_Process -Filter "Name='Schedule I.exe'" |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($client.root, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "stopped client pid $($_.ProcessId)" }
    Start-Sleep -Seconds 3
    foreach ($name in $moved) {
        Move-Item -LiteralPath (Join-Path $aside $name) -Destination (Join-Path $clientPacks $name)
        Write-Host "restored $name"
    }
}

if ($script:failed -gt 0) { Write-Host "RESULT FAIL ($script:failed failed)"; exit 1 }
Write-Host 'RESULT PASS'
exit 0

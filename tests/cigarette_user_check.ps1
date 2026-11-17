# Co-op check for networked items NPCs hold (the smoke-break cigarette): on the client every EquippedItemHandler must
# resolve its user, the NPC holding it. A handler without a user throws a NullReferenceException in its Update every
# frame on that peer. Covers both ways a client learns about a held item: the host equips while the client is in the
# world, and the client joins while the item is already held. Then the client stays in the world for -WatchMinutes and
# its log must hold no EquippedItemHandler error. Needs a Debug build (the `personnel equip` probe).
#
#     pwsh -File tests/cigarette_user_check.ps1                 # brings the pair up with coop_up.ps1 first
#     pwsh -File tests/cigarette_user_check.ps1 -NoUp           # pair already running
#     pwsh -File tests/cigarette_user_check.ps1 -Pack cleo -Vanilla kyle
#
# Prints PASS or FAIL per check and exits 1 when any check fails. Stop the pair afterwards with tests/coop_down.ps1.
param(
    [string]$Pack = 'cleo',
    [string]$Vanilla = 'kyle',
    [switch]$NoUp,
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    [int]$SettleSeconds = 5,
    [int]$JoinAttempts = 3,
    # How long the client stays in the world afterwards while its log is checked; 0 skips the soak.
    [int]$WatchMinutes = 10
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'
$s1 = Join-Path $root 'Workspace\tools\bridge\s1.py'
$script:failed = 0

function Report([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) { Write-Host "PASS $name" } else { Write-Host "FAIL $name - $detail"; $script:failed++ }
}

function Run([string]$slot, [string]$command) {
    $out = & python -I $s1 --slot $slot run $command 2>&1 | Out-String
    Write-Host "[$slot] $command`n$($out.TrimEnd())"
    return $out
}

# Handler lines of one `personnel equip` listing: total, without a user, and the NPC ids that hold one.
function Read-Equip([string]$out) {
    $summary = [regex]::Match($out, 'equip: (\d+) handler\(s\), (\d+) without a user')
    $npcs = @([regex]::Matches($out, ' npc (\S+) userObject') | ForEach-Object { $_.Groups[1].Value } | Where-Object { $_ -ne '-' })
    [pscustomobject]@{
        Found   = $summary.Success
        Total   = if ($summary.Success) { [int]$summary.Groups[1].Value } else { -1 }
        Missing = if ($summary.Success) { [int]$summary.Groups[2].Value } else { -1 }
        Npcs    = $npcs
    }
}

function Test-Client([string]$label, [string[]]$expected) {
    $e = Read-Equip (Run $clientSlot 'personnel equip')
    Report "$label client listed its handlers" $e.Found 'no summary line from personnel equip'
    Report "$label client sees held items" ($e.Total -ge $expected.Count) "only $($e.Total) handler(s)"
    Report "$label every client handler has its user" ($e.Missing -eq 0) "$($e.Missing) of $($e.Total) without a user"
    foreach ($id in $expected) {
        Report "$label client resolves the holder $id" (@($e.Npcs | Where-Object { $_ -like "*$id*" }).Count -gt 0) "no handler with user $id"
    }
}

if (-not $NoUp) {
    & pwsh -File (Join-Path $PSScriptRoot 'coop_up.ps1') -Profile $Profile
    if ($LASTEXITCODE -ne 0) { Write-Host 'FAIL coop_up - pair did not come up'; exit 1 }
}

$lease = (& python -I $slotsPy --json mine | ConvertFrom-Json).lease
if (-not $lease -or @($lease.slots).Count -lt 2) { Write-Host 'FAIL lease - this session holds no co-op pair'; exit 1 }
$hostSlot = @($lease.slots)[0]
$clientSlot = @($lease.slots)[-1]
$client = $lease.table.$clientSlot

# 1. The host puts a cigarette into a vanilla NPC's and a pack NPC's hand while the client is in the world.
$holders = @()
foreach ($npc in @($Vanilla, $Pack)) {
    $out = Run $hostSlot "personnel equip smoke $npc"
    $ok = $out -match "equip smoke \S+: handler object \d+"
    Report "host equips a cigarette on $npc" $ok 'no handler created'
    if ($ok) { $holders += $npc }
}
$hostEquip = Read-Equip (Run $hostSlot 'personnel equip')
Report 'host handlers have their user' ($hostEquip.Found -and $hostEquip.Missing -eq 0) "$($hostEquip.Missing) of $($hostEquip.Total) without a user"
Start-Sleep -Seconds $SettleSeconds
Test-Client 'live:' $holders

# Restarts the client with --join and waits until it is in the world. Returns $true when it got there.
function Join-Client {
    Get-CimInstance Win32_Process -Filter "Name='Schedule I.exe'" |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($client.root, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "stopped client pid $($_.ProcessId)" }
    Start-Sleep -Seconds 4
    $process = Start-Process -FilePath (Join-Path $client.root 'Schedule I.exe') -ArgumentList '--join' -WorkingDirectory $client.root -PassThru
    $deadline = (Get-Date).AddSeconds(300)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        if ($process.HasExited) { Write-Host "client exited with $($process.ExitCode)"; return $false }
        try {
            $h = Invoke-RestMethod -Uri "http://127.0.0.1:$($client.port)/health" -TimeoutSec 3
            if ($h.scene -eq 'Main' -and $h.inGame -and -not $h.loading) { return $true }
            # A station's config RPC that throws inside LoadManager.onLoadComplete ends the vanilla load routine, and the
            # client stays on the loading screen for good. That is a separate join bug; another attempt usually passes.
            if ($h.loading -and (Select-String -LiteralPath $clientLog -Pattern 'ConfigurationReplicator.*ReceiveStringField' -Quiet)) {
                Write-Host 'WARN client load routine died in ConfigurationReplicator.ReceiveStringField (separate join bug), joining again'
                return $false
            }
        } catch { Write-Host ("{0:HH:mm:ss} client bridge down: {1}" -f (Get-Date), $_.Exception.Message) }
    }
    Write-Host 'client not in the world after 300 s'
    return $false
}

# Log lines from vanilla EquippedItemHandler: its NullReferenceException trace in Update or OnStartClient, and its
# "can only be used with users that implement INetworkedEquippableUser" error.
function Count-HandlerErrors([int]$fromLine) {
    $lines = @(Get-Content -LiteralPath $clientLog)
    if ($lines.Count -le $fromLine) { return 0 }
    return @($lines[$fromLine..($lines.Count - 1)] | Where-Object { $_ -match 'EquippedItemHandler' }).Count
}

# 2. The client joins again while the cigarettes are held, so it learns about them from the spawn payload.
$clientLog = Join-Path $client.root 'MelonLoader\Latest.log'
$joined = $false
for ($attempt = 1; $attempt -le $JoinAttempts -and -not $joined; $attempt++) {
    Write-Host "join attempt $attempt of $JoinAttempts"
    $joined = Join-Client
}
Report 'client joins again' $joined "client not in the world after $JoinAttempts attempt(s)"
if ($joined) {
    Start-Sleep -Seconds $SettleSeconds
    Test-Client 'join:' $holders

    # 3. The card's own steps: the client stays in the world while NPCs take their smoke breaks, and its log gets no
    #    EquippedItemHandler error.
    if ($WatchMinutes -gt 0) {
        $start = @(Get-Content -LiteralPath $clientLog).Count
        Write-Host ("{0:HH:mm:ss} watching the client log for {1} min from line {2}" -f (Get-Date), $WatchMinutes, $start)
        $until = (Get-Date).AddMinutes($WatchMinutes)
        while ((Get-Date) -lt $until) { Start-Sleep -Seconds 10 }
        $errors = Count-HandlerErrors $start
        Report "soak: no EquippedItemHandler error in $WatchMinutes min" ($errors -eq 0) "$errors EquippedItemHandler line(s) in $clientLog"
        Test-Client 'soak:' @()
    }
}

if ($script:failed -gt 0) { Write-Host "RESULT FAIL ($script:failed failed)"; exit 1 }
Write-Host 'RESULT PASS'
exit 0

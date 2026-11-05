# Brings up a joined co-op pair for the Personnel co-op checks: duo.ps1 builds this worktree into both slots, starts the
# host on a fresh save 5, and this script relaunches the client with LocalLobby's --join so it joins this session's lobby.
#
#     pwsh -File tests/coop_up.ps1
#     pwsh -File tests/coop_up.ps1 -Profile main     # both slots hold the real installation's mods plus this build
#
# Both slots need the same packs under UserData/Personnel/Packs; a client with a different set hangs on join. The script
# copies the host's packs that the client lacks and lists them in UserData/Personnel/Packs/.coop_added.txt, which
# tests/coop_down.ps1 reads to remove them again.
param(
    [ValidateSet('stack', 'main')]
    [string]$Profile = 'stack',
    # Folder with Mods\S1API.Il2Cpp.MelonLoader.dll and Plugins\S1APILoader.MelonLoader.dll, copied over the stack's
    # S1API in both slots before the host restarts. For a check against an S1API release the catalog does not pin yet.
    [string]$S1Api = ''
)
$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$worktree = Split-Path (Resolve-Path (Join-Path $PSScriptRoot '..')) -Leaf
$duo = Join-Path $root 'Workspace\tools\session\duo.ps1'
$slotsPy = Join-Path $root 'Workspace\tools\slots\slots.py'

& pwsh -NoProfile -File $duo -Stop
$lease = (& python -I $slotsPy --json acquire --coop --via duo.ps1 --mod Personnel | ConvertFrom-Json).lease
if (-not $lease -or @($lease.slots).Count -lt 2) { Write-Host 'FAIL no co-op pair leased'; exit 1 }
$hostRoot = $lease.table.(@($lease.slots)[0]).root
$clientName = @($lease.slots)[-1]
$client = $lease.table.$clientName
$hostPacks = Join-Path $hostRoot 'UserData\Personnel\Packs'
$clientPacks = Join-Path $client.root 'UserData\Personnel\Packs'

# Align packs in both directions so host and client register the same NPCs.
foreach ($pair in @(@($hostPacks, $clientPacks), @($clientPacks, $hostPacks))) {
    $from, $to = $pair
    $added = Join-Path $to '.coop_added.txt'
    Get-ChildItem -LiteralPath $from -Directory | ForEach-Object {
        $target = Join-Path $to $_.Name
        if (-not (Test-Path -LiteralPath $target)) {
            Copy-Item -LiteralPath $_.FullName -Destination $target -Recurse
            Add-Content -LiteralPath $added -Value $_.Name
            Write-Host "copied pack $($_.Name) to $to"
        }
    }
}

& pwsh -File $duo -Build $worktree -BootSeconds 240
if ($LASTEXITCODE -ne 0) { Write-Host "duo failed with $LASTEXITCODE"; exit 1 }

function Stop-Slot([string]$slotRoot) {
    Get-CimInstance Win32_Process -Filter "Name='Schedule I.exe'" |
        Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($slotRoot, [StringComparison]::OrdinalIgnoreCase) } |
        ForEach-Object { Stop-Process -Id $_.ProcessId -Force; Write-Host "stopped pid $($_.ProcessId) in $slotRoot" }
}

function Wait-Health([int]$port, [scriptblock]$done, $process) {
    $deadline = (Get-Date).AddSeconds(300)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Seconds 5
        if ($process -and $process.HasExited) { Write-Host "game exited with $($process.ExitCode)"; return $false }
        try {
            $h = Invoke-RestMethod -Uri "http://127.0.0.1:$port/health" -TimeoutSec 3
            Write-Host ("{0:HH:mm:ss} port {1} scene={2} inGame={3} loading={4}" -f (Get-Date), $port, $h.scene, $h.inGame, $h.loading)
            if (& $done $h) { return $true }
        } catch { Write-Host ("{0:HH:mm:ss} port {1} bridge down: {2}" -f (Get-Date), $port, $_.Exception.Message) }
    }
    Write-Host "port $port not ready after 300 s"
    return $false
}

Write-Host "client slot: $clientName port $($client.port) root $($client.root)"
Stop-Slot $client.root

# Profile main: duo.ps1 laid out the save and the lease; now both slots get the real installation's mods with this build
# on top (and LocalLobby for --host/--join), and the host restarts into the same save.
if ($Profile -eq 'main' -or $S1Api) {
    . (Join-Path $root 'Workspace\tools\slots\stack.lib.ps1')
    $hostName = @($lease.slots)[0]
    Stop-Slot $hostRoot
    Start-Sleep -Seconds 4
    $table = Get-StackSlotTable
    if ($Profile -eq 'main') {
        $src = Resolve-StackSource $worktree
        $stack = Compose-MainProfile $table['main'].root @{ $src.Mod = $src.Dir } 'Debug' @('LocalLobby')
        foreach ($name in @($hostName, $clientName)) { Apply-Stack $name $table[$name] $stack; Write-Host "applied profile main to $name" }
    }
    if ($S1Api) {
        foreach ($slotRoot in @($hostRoot, $client.root)) {
            foreach ($rel in @('Mods\S1API.Il2Cpp.MelonLoader.dll', 'Plugins\S1APILoader.MelonLoader.dll')) {
                $from = Join-Path $S1Api $rel
                if (-not (Test-Path -LiteralPath $from)) { Write-Host "FAIL S1API override: $from is missing"; exit 1 }
                Copy-Item -LiteralPath $from -Destination (Join-Path $slotRoot $rel) -Force
                Write-Host "S1API override: $rel -> $slotRoot ($((Get-FileHash -LiteralPath $from).Hash.Substring(0, 12)))"
            }
        }
    }
    $hostProcess = Start-Process -FilePath (Join-Path $hostRoot 'Schedule I.exe') -ArgumentList @('--host', '--autoload', '--saveslot', '4') -WorkingDirectory $hostRoot -PassThru
    if (-not (Wait-Health $lease.table.$hostName.port { param($h) $h.scene -eq 'Main' -and $h.inGame -and -not $h.loading } $hostProcess)) {
        Write-Host 'host did not reach the world'; exit 1
    }
    Write-Host 'host is in the world'
    # The restarted host created a new lobby; the client joins the id in its own lobby.txt.
    $lobbyId = (Get-Content -LiteralPath (Join-Path $hostRoot 'UserData\LocalLobby\lobby.txt') -Raw).Trim()
    $clientLobbyDir = Join-Path $client.root 'UserData\LocalLobby'
    New-Item -ItemType Directory -Force -Path $clientLobbyDir | Out-Null
    [System.IO.File]::WriteAllText((Join-Path $clientLobbyDir 'lobby.txt'), $lobbyId)
    Write-Host "client lobby.txt set to $lobbyId"
}
Start-Sleep -Seconds 4
$exe = Join-Path $client.root 'Schedule I.exe'

$process = Start-Process -FilePath $exe -ArgumentList '--join' -WorkingDirectory $client.root -PassThru
$deadline = (Get-Date).AddSeconds(300)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 5
    if ($process.HasExited) { Write-Host "client exited with $($process.ExitCode)"; exit 1 }
    try {
        $h = Invoke-RestMethod -Uri "http://127.0.0.1:$($client.port)/health" -TimeoutSec 3
        Write-Host ("{0:HH:mm:ss} scene={1} inGame={2} loading={3}" -f (Get-Date), $h.scene, $h.inGame, $h.loading)
        if ($h.scene -eq 'Main' -and $h.inGame -and -not $h.loading) { Write-Host 'client is in the world'; exit 0 }
    } catch { Write-Host ("{0:HH:mm:ss} bridge down: {1}" -f (Get-Date), $_.Exception.Message) }
}
Write-Host 'client not in the world after 300 s'
exit 1

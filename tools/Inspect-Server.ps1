[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ServerRoot
)

$ErrorActionPreference = 'Stop'
$resolvedRoot = (Resolve-Path -LiteralPath $ServerRoot).Path
$rootItem = Get-Item -LiteralPath $resolvedRoot
if (-not $rootItem.PSIsContainer) {
    throw "ServerRoot is not a directory: $resolvedRoot"
}

$csgoCandidates = @(
    (Join-Path $resolvedRoot 'game\csgo'),
    (Join-Path $resolvedRoot 'csgo'),
    $resolvedRoot
) | Select-Object -Unique

$csgoDirectory = $null
foreach ($candidate in $csgoCandidates) {
    $gameInfo = Join-Path $candidate 'gameinfo.gi'
    if (Test-Path -LiteralPath $gameInfo -PathType Leaf) {
        $csgoDirectory = (Resolve-Path -LiteralPath $candidate).Path
        break
    }
}

$processes = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -in @('cs2.exe', 'srcds.exe', 'steamcmd.exe') -or
    ($_.ExecutablePath -and $_.ExecutablePath.StartsWith($resolvedRoot, [StringComparison]::OrdinalIgnoreCase))
} | Select-Object ProcessId, Name, ExecutablePath, CommandLine

$launchPatterns = @('*.ps1', '*.bat', '*.cmd', '*.vdf')
$launchFiles = foreach ($pattern in $launchPatterns) {
    Get-ChildItem -LiteralPath $resolvedRoot -Filter $pattern -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '\\game\\bin\\' } |
        Select-Object FullName, Length, LastWriteTime
}

$result = [ordered]@{
    ServerRoot = $resolvedRoot
    CsgoDirectory = $csgoDirectory
    RootEntries = @(Get-ChildItem -LiteralPath $resolvedRoot -Force | Select-Object Name, FullName, PSIsContainer, Length, LastWriteTime)
    RunningProcesses = @($processes)
    LaunchFiles = @($launchFiles | Sort-Object FullName)
}

if ($csgoDirectory) {
    $addons = Join-Path $csgoDirectory 'addons'
    $cssRoot = Join-Path $addons 'counterstrikesharp'
    $pluginRoot = Join-Path $cssRoot 'plugins'
    $awperRoot = Join-Path $pluginRoot 'AwperTrainer'
    $result.AddonsEntries = @(
        if (Test-Path -LiteralPath $addons) {
            Get-ChildItem -LiteralPath $addons -Force | Select-Object Name, FullName, PSIsContainer, Length, LastWriteTime
        }
    )
    $result.CounterStrikeSharpEntries = @(
        if (Test-Path -LiteralPath $cssRoot) {
            Get-ChildItem -LiteralPath $cssRoot -Force | Select-Object Name, FullName, PSIsContainer, Length, LastWriteTime
        }
    )
    $result.PluginEntries = @(
        if (Test-Path -LiteralPath $pluginRoot) {
            Get-ChildItem -LiteralPath $pluginRoot -Force | Select-Object Name, FullName, PSIsContainer, Length, LastWriteTime
        }
    )
    $result.AwperFiles = @(
        if (Test-Path -LiteralPath $awperRoot) {
            Get-ChildItem -LiteralPath $awperRoot -File -Recurse | Select-Object FullName, Length, LastWriteTime
        }
    )

    $dependencyPaths = [ordered]@{
        MetamodVdf = Join-Path $addons 'metamod.vdf'
        CounterStrikeSharpVdf = Join-Path $addons 'counterstrikesharp.vdf'
        CounterStrikeSharpApi = Join-Path $cssRoot 'api\CounterStrikeSharp.API.dll'
        BotControllerNative = Join-Path $addons 'BotController\bin\win64\BotController.dll'
        BotControllerApi = Join-Path $cssRoot 'shared\CS2-Bot-Controller\BotControllerApi.dll'
        RayTraceNative = Join-Path $addons 'RayTrace\bin\win64\RayTrace.dll'
        RayTraceImpl = Join-Path $cssRoot 'shared\RayTrace\RayTraceImpl.dll'
        RayTraceApi = Join-Path $cssRoot 'shared\RayTrace\RayTraceApi.dll'
    }
    $result.DependencyFiles = @(
        foreach ($entry in $dependencyPaths.GetEnumerator()) {
            $exists = Test-Path -LiteralPath $entry.Value -PathType Leaf
            $item = if ($exists) { Get-Item -LiteralPath $entry.Value } else { $null }
            [pscustomobject]@{
                Name = $entry.Key
                Path = $entry.Value
                Exists = $exists
                Length = if ($item) { $item.Length } else { $null }
                Version = if ($item -and $item.VersionInfo) { $item.VersionInfo.FileVersion } else { $null }
                LastWriteTime = if ($item) { $item.LastWriteTime } else { $null }
            }
        }
    )

    $logRoots = @(
        (Join-Path $csgoDirectory 'logs'),
        (Join-Path $cssRoot 'logs'),
        (Join-Path $resolvedRoot 'logs')
    ) | Select-Object -Unique
    $result.RecentLogs = @(
        foreach ($logRoot in $logRoots) {
            if (Test-Path -LiteralPath $logRoot) {
                Get-ChildItem -LiteralPath $logRoot -File -Recurse -ErrorAction SilentlyContinue |
                    Sort-Object LastWriteTime -Descending |
                    Select-Object -First 10 FullName, Length, LastWriteTime
            }
        }
    )
}

$result | ConvertTo-Json -Depth 6

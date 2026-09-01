[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ServerRoot,

    [Parameter(Mandatory)]
    [string]$NativeStage,

    [Parameter(Mandatory)]
    [string]$ManagedStage
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ServerRoot).Path
$csgo = (Resolve-Path -LiteralPath (Join-Path $root 'game\csgo')).Path
$native = (Resolve-Path -LiteralPath $NativeStage).Path
$managed = (Resolve-Path -LiteralPath $ManagedStage).Path
if (Get-Process -Name 'cs2' -ErrorAction SilentlyContinue) {
    throw 'cs2.exe is running; stop it before replacing BotController files.'
}

$sourceFiles = [ordered]@{
    NativeDll = Join-Path $native 'addons\BotController\bin\win64\BotController.dll'
    NativeGameData = Join-Path $native 'addons\BotController\gamedata.json'
    NativeVdf = Join-Path $native 'addons\metamod\BotController.vdf'
    ManagedApi = Join-Path $managed 'addons\counterstrikesharp\shared\BotControllerApi\BotControllerApi.dll'
    ManagedImpl = Join-Path $managed 'addons\counterstrikesharp\plugins\BotControllerImpl\BotControllerImpl.dll'
}
foreach ($entry in $sourceFiles.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
        throw "Missing staged $($entry.Key): $($entry.Value)"
    }
}

$expectedHashes = @{
    $sourceFiles.NativeDll = 'D80830479C607C5CAB5A64E328F51475EFED2F09D9A65A4AE1EE462489050667'
    $sourceFiles.ManagedApi = 'E2C5BF295323D86C0AF646F312147F8B4B529A21B18BC4A19A0BA88345CD2395'
    $sourceFiles.ManagedImpl = '881B5C51E0E18A2611028933E653AA3DC2A23204738EADEB6B7F99DB11C50F64'
}
foreach ($entry in $expectedHashes.GetEnumerator()) {
    $actual = (Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash
    if ($actual -ne $entry.Value) {
        throw "Staged file checksum mismatch for $($entry.Key): $actual"
    }
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupParent = [System.IO.Path]::GetFullPath((Join-Path $root 'backups'))
$backupRoot = [System.IO.Path]::GetFullPath((Join-Path $backupParent "botcontroller-abi18-$timestamp"))
if (-not $backupRoot.StartsWith($backupParent + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe backup target: $backupRoot"
}
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

$relativeTargets = @(
    'addons\BotController',
    'addons\metamod\BotController.vdf',
    'addons\counterstrikesharp\shared\BotControllerApi',
    'addons\counterstrikesharp\plugins\BotControllerImpl'
)
foreach ($relative in $relativeTargets) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $csgo $relative))
    if (-not $target.StartsWith($csgo + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe BotController target: $target"
    }
    if (Test-Path -LiteralPath $target) {
        $backup = Join-Path $backupRoot $relative
        $backupDirectory = [System.IO.Path]::GetDirectoryName($backup)
        New-Item -ItemType Directory -Path $backupDirectory -Force | Out-Null
        Copy-Item -LiteralPath $target -Destination $backup -Recurse -Force
    }
}

foreach ($relative in $relativeTargets) {
    $target = [System.IO.Path]::GetFullPath((Join-Path $csgo $relative))
    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Recurse -Force
    }
}

$addons = Join-Path $csgo 'addons'
$css = Join-Path $addons 'counterstrikesharp'
Copy-Item -LiteralPath (Join-Path $native 'addons\BotController') -Destination $addons -Recurse -Force
Copy-Item -LiteralPath $sourceFiles.NativeVdf -Destination (Join-Path $addons 'metamod') -Force
Copy-Item -LiteralPath (Join-Path $managed 'addons\counterstrikesharp\shared\BotControllerApi') -Destination (Join-Path $css 'shared') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $managed 'addons\counterstrikesharp\plugins\BotControllerImpl') -Destination (Join-Path $css 'plugins') -Recurse -Force

$deployed = @(
    (Join-Path $addons 'BotController\bin\win64\BotController.dll'),
    (Join-Path $addons 'BotController\gamedata.json'),
    (Join-Path $addons 'metamod\BotController.vdf'),
    (Join-Path $css 'shared\BotControllerApi\BotControllerApi.dll'),
    (Join-Path $css 'plugins\BotControllerImpl\BotControllerImpl.dll')
)

[ordered]@{
    BackupDirectory = $backupRoot
    RemovedAndReplacedTargets = $relativeTargets
    DeployedFiles = @(
        foreach ($path in $deployed) {
            $item = Get-Item -LiteralPath $path
            [pscustomobject]@{
                Path = $item.FullName
                Length = $item.Length
                Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
            }
        }
    )
} | ConvertTo-Json -Depth 5

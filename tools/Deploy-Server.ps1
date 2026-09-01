[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ServerRoot,

    [Parameter(Mandatory)]
    [string]$AwperPackage,

    [Parameter(Mandatory)]
    [string]$DependencyStage
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ServerRoot).Path
$csgo = (Resolve-Path -LiteralPath (Join-Path $root 'game\csgo')).Path
$stage = (Resolve-Path -LiteralPath $DependencyStage).Path
$awperZip = (Resolve-Path -LiteralPath $AwperPackage).Path

if (-not (Test-Path -LiteralPath (Join-Path $csgo 'gameinfo.gi') -PathType Leaf)) {
    throw "Resolved directory is not game/csgo: $csgo"
}
$serverExecutable = [System.IO.Path]::GetFullPath((Join-Path $root 'game\bin\win64\cs2.exe'))
$runningServer = Get-CimInstance Win32_Process | Where-Object {
    $_.Name -eq 'cs2.exe' -and $_.ExecutablePath -and
    $_.ExecutablePath.Equals($serverExecutable, [StringComparison]::OrdinalIgnoreCase)
}
if ($runningServer) {
    throw "The CS2 dedicated server is running (PID $($runningServer.ProcessId)); stop it before replacing plugin or native files."
}

$expectedAwperHash = '834326E39438E60117B8ADD1ADC07C252A7A173751CE989E04D137C2709E9258'
$actualAwperHash = (Get-FileHash -LiteralPath $awperZip -Algorithm SHA256).Hash
if ($actualAwperHash -ne $expectedAwperHash) {
    throw "AwperTrainer package checksum mismatch: $actualAwperHash"
}

$rayCssArchive = Join-Path $stage 'RayTrace-CSS-API-v1.0.16.tar.gz'
$rayNativeArchive = Join-Path $stage 'RayTrace-MM-v1.0.16-windows.tar.gz'
$telemetryConfig = Join-Path $stage 'AwperTrainer.json'
$expectedFiles = @($rayCssArchive, $rayNativeArchive, $telemetryConfig)
foreach ($path in $expectedFiles) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Staged dependency file is missing: $path"
    }
}

$expectedRayHashes = @{
    $rayCssArchive = 'E865CA551DA35AF31DC70F271840D1DCE932A84E1C1BD27AA5AD3191EFE6E1D4'
    $rayNativeArchive = '020B7B49CB249793A6840AF3EDFAF8D07C3C8A8069C8246972D113D047429C59'
}
foreach ($entry in $expectedRayHashes.GetEnumerator()) {
    $actual = (Get-FileHash -LiteralPath $entry.Key -Algorithm SHA256).Hash
    if ($actual -ne $entry.Value) {
        throw "RayTrace package checksum mismatch for $($entry.Key): $actual"
    }
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupRoot = [System.IO.Path]::GetFullPath((Join-Path $root "backups\awpertrainer-$timestamp"))
$allowedBackupParent = [System.IO.Path]::GetFullPath((Join-Path $root 'backups'))
if (-not $backupRoot.StartsWith($allowedBackupParent + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe backup target: $backupRoot"
}
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

$relativeTargets = @(
    'addons\RayTrace',
    'addons\metamod\RayTrace.vdf',
    'addons\counterstrikesharp\shared\RayTraceApi',
    'addons\counterstrikesharp\plugins\RayTraceImpl',
    'addons\counterstrikesharp\plugins\AwperTrainer',
    'addons\counterstrikesharp\configs\plugins\AwperTrainer',
    'scripts\awper\awper_camera.vjs_c',
    'scripts\awper\awper_hud.vjs_c',
    'panorama\layout\custom_game\awper_hud.vxml_c',
    'panorama\styles\custom_game\awper_hud.vcss_c'
)
$backedUp = [System.Collections.Generic.List[string]]::new()
foreach ($relative in $relativeTargets) {
    $source = [System.IO.Path]::GetFullPath((Join-Path $csgo $relative))
    if (-not $source.StartsWith($csgo + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe source target: $source"
    }
    if (Test-Path -LiteralPath $source) {
        $destination = Join-Path $backupRoot $relative
        $destinationParent = [System.IO.Path]::GetDirectoryName($destination)
        New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
        $backedUp.Add($relative)
    }
}

$tempParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$tempExtract = Join-Path $tempParent ('awper-deploy-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempExtract | Out-Null
try {
    $rayCssExtract = Join-Path $tempExtract 'ray-css'
    $rayNativeExtract = Join-Path $tempExtract 'ray-native'
    $awperExtract = Join-Path $tempExtract 'awper'
    New-Item -ItemType Directory -Path $rayCssExtract, $rayNativeExtract, $awperExtract | Out-Null

    & tar.exe @('-xf', $rayCssArchive, '-C', $rayCssExtract)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to extract RayTrace CSS package.' }
    & tar.exe @('-xf', $rayNativeArchive, '-C', $rayNativeExtract)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to extract RayTrace native package.' }
    & tar.exe @('-xf', $awperZip, '-C', $awperExtract)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to extract AwperTrainer package.' }

    $rayCssRoot = Get-ChildItem -LiteralPath $rayCssExtract -Directory | Select-Object -First 1
    if (-not $rayCssRoot) { throw 'RayTrace CSS archive has no root directory.' }
    $rayCssPayload = Join-Path $rayCssRoot.FullName 'counterstrikesharp'
    $cssTarget = Join-Path $csgo 'addons\counterstrikesharp'
    Copy-Item -LiteralPath (Join-Path $rayCssPayload 'shared\RayTraceApi') -Destination (Join-Path $cssTarget 'shared') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $rayCssPayload 'plugins\RayTraceImpl') -Destination (Join-Path $cssTarget 'plugins') -Recurse -Force

    $addonsTarget = Join-Path $csgo 'addons'
    Copy-Item -LiteralPath (Join-Path $rayNativeExtract 'RayTrace') -Destination $addonsTarget -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $rayNativeExtract 'metamod\RayTrace.vdf') -Destination (Join-Path $addonsTarget 'metamod') -Force

    $awperTarget = Join-Path $cssTarget 'plugins\AwperTrainer'
    New-Item -ItemType Directory -Path $awperTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $awperExtract '*') -Destination $awperTarget -Recurse -Force

    $cameraAsset = Join-Path $awperExtract 'resources\awper_camera.vjs_c'
    if (-not (Test-Path -LiteralPath $cameraAsset -PathType Leaf)) {
        throw "AwperTrainer camera bridge asset is missing: $cameraAsset"
    }
    $cameraTargetDirectory = Join-Path $csgo 'scripts\awper'
    New-Item -ItemType Directory -Path $cameraTargetDirectory -Force | Out-Null
    Copy-Item -LiteralPath $cameraAsset -Destination (Join-Path $cameraTargetDirectory 'awper_camera.vjs_c') -Force

    $hudScriptAsset = Join-Path $awperExtract 'resources\awper_hud.vjs_c'
    $hudLayoutAsset = Join-Path $awperExtract 'resources\awper_hud.vxml_c'
    $hudStyleAsset = Join-Path $awperExtract 'resources\awper_hud.vcss_c'
    foreach ($hudAsset in @($hudScriptAsset, $hudLayoutAsset, $hudStyleAsset)) {
        if (-not (Test-Path -LiteralPath $hudAsset -PathType Leaf)) {
            throw "AwperTrainer HUD asset is missing: $hudAsset"
        }
    }
    Copy-Item -LiteralPath $hudScriptAsset -Destination (Join-Path $cameraTargetDirectory 'awper_hud.vjs_c') -Force
    $hudLayoutTargetDirectory = Join-Path $csgo 'panorama\layout\custom_game'
    $hudStyleTargetDirectory = Join-Path $csgo 'panorama\styles\custom_game'
    New-Item -ItemType Directory -Path $hudLayoutTargetDirectory, $hudStyleTargetDirectory -Force | Out-Null
    Copy-Item -LiteralPath $hudLayoutAsset -Destination (Join-Path $hudLayoutTargetDirectory 'awper_hud.vxml_c') -Force
    Copy-Item -LiteralPath $hudStyleAsset -Destination (Join-Path $hudStyleTargetDirectory 'awper_hud.vcss_c') -Force

    $configTarget = Join-Path $cssTarget 'configs\plugins\AwperTrainer'
    New-Item -ItemType Directory -Path $configTarget -Force | Out-Null
    Copy-Item -LiteralPath $telemetryConfig -Destination (Join-Path $configTarget 'AwperTrainer.json') -Force
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath($tempExtract)
    if ($resolvedTemp.StartsWith($tempParent, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTemp).StartsWith('awper-deploy-', [StringComparison]::Ordinal)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$deployedFiles = @(
    Get-ChildItem -LiteralPath (Join-Path $csgo 'addons\RayTrace') -File -Recurse
    Get-ChildItem -LiteralPath (Join-Path $csgo 'addons\counterstrikesharp\shared\RayTraceApi') -File -Recurse
    Get-ChildItem -LiteralPath (Join-Path $csgo 'addons\counterstrikesharp\plugins\RayTraceImpl') -File -Recurse
    Get-ChildItem -LiteralPath (Join-Path $csgo 'addons\counterstrikesharp\plugins\AwperTrainer') -File -Recurse
    Get-Item -LiteralPath (Join-Path $csgo 'scripts\awper\awper_camera.vjs_c')
    Get-Item -LiteralPath (Join-Path $csgo 'scripts\awper\awper_hud.vjs_c')
    Get-Item -LiteralPath (Join-Path $csgo 'panorama\layout\custom_game\awper_hud.vxml_c')
    Get-Item -LiteralPath (Join-Path $csgo 'panorama\styles\custom_game\awper_hud.vcss_c')
    Get-Item -LiteralPath (Join-Path $csgo 'addons\metamod\RayTrace.vdf')
    Get-Item -LiteralPath (Join-Path $csgo 'addons\counterstrikesharp\configs\plugins\AwperTrainer\AwperTrainer.json')
)

[ordered]@{
    ServerRoot = $root
    CsgoDirectory = $csgo
    BackupDirectory = $backupRoot
    BackedUpTargets = @($backedUp)
    DeployedFileCount = $deployedFiles.Count
    CriticalFiles = @(
        $deployedFiles |
            Where-Object { $_.Name -in @('AwperTrainer.dll', 'AwperTrainer.Core.dll', 'RayTrace.dll', 'RayTraceApi.dll', 'RayTraceImpl.dll', 'RayTrace.vdf', 'AwperTrainer.json') } |
            Select-Object FullName, Length, @{ Name = 'Sha256'; Expression = { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
    )
} | ConvertTo-Json -Depth 6

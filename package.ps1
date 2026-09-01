[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not $NoBuild) {
    $buildArgs = @('build', (Join-Path $PSScriptRoot 'AwperTrainer.slnx'), '--configuration', $Configuration, '--nologo')
    & dotnet @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }
}

$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'artifacts'))
$packageRoot = [System.IO.Path]::GetFullPath((Join-Path $artifactRoot 'AwperTrainer'))
$expectedPrefix = $artifactRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $packageRoot.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to replace package outside the artifact directory: $packageRoot"
}
if (Test-Path -LiteralPath $packageRoot) { Remove-Item -LiteralPath $packageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $packageRoot | Out-Null

$output = Join-Path $PSScriptRoot "src\AwperTrainer.Plugin\bin\$Configuration"
$runtimeFiles = @('AwperTrainer.dll', 'AwperTrainer.Core.dll', 'AwperTrainer.deps.json', 'AwperTrainer.runtimeconfig.json')
foreach ($name in $runtimeFiles) {
    $source = Join-Path $output $name
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Missing build output: $source" }
    Copy-Item -LiteralPath $source -Destination $packageRoot
}
foreach ($name in @('README.md', 'command.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $packageRoot
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\Verify-ServerInstall.ps1') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'tools\Deploy-ClientHud.ps1') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config\AwperTrainer.example.json') -Destination $packageRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'config\awper_bindings.cfg') -Destination $packageRoot
$resourcesTarget = Join-Path $packageRoot 'resources'
New-Item -ItemType Directory -Path $resourcesTarget | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\awper_camera.vjs_c') -Destination $resourcesTarget
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\awper_hud.vjs_c') -Destination $resourcesTarget
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\panorama\layout\custom_game\awper_hud.vxml_c') -Destination $resourcesTarget
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'assets\panorama\styles\custom_game\awper_hud.vcss_c') -Destination $resourcesTarget

$forbidden = @('BotControllerApi.dll', 'RayTraceApi.dll', 'CounterStrikeSharp.API.dll')
foreach ($name in $forbidden) {
    if (Test-Path -LiteralPath (Join-Path $packageRoot $name)) { throw "Runtime dependency contract was accidentally packaged: $name" }
}

$zip = Join-Path $artifactRoot 'AwperTrainer.zip'
Compress-Archive -Path (Join-Path $packageRoot '*') -DestinationPath $zip -Force
Write-Output "Package verified: $zip"

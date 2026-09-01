[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $CsgoDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [System.IO.Path]::GetFullPath($CsgoDirectory)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "CS2 csgo directory not found: $root" }

$required = @(
    'addons\counterstrikesharp\plugins\AwperTrainer\AwperTrainer.dll',
    'addons\counterstrikesharp\plugins\AwperTrainer\AwperTrainer.Core.dll',
    'addons\counterstrikesharp\shared\BotControllerApi\BotControllerApi.dll',
    'addons\counterstrikesharp\plugins\BotControllerImpl\BotControllerImpl.dll',
    'addons\counterstrikesharp\shared\RayTraceApi\RayTraceApi.dll',
    'addons\counterstrikesharp\plugins\RayTraceImpl\RayTraceImpl.dll',
    'scripts\awper\awper_camera.vjs_c',
    'scripts\awper\awper_hud.vjs_c',
    'panorama\layout\custom_game\awper_hud.vxml_c',
    'panorama\styles\custom_game\awper_hud.vcss_c'
)
$missing = [System.Collections.Generic.List[string]]::new()
foreach ($relative in $required) {
    $path = Join-Path $root $relative
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { $missing.Add($relative) }
}

$platformPairs = @(
    @('addons\BotController\bin\win64\BotController.dll', 'addons\BotController\bin\linuxsteamrt64\BotController.so'),
    @('addons\RayTrace\bin\win64\RayTrace.dll', 'addons\RayTrace\bin\linuxsteamrt64\RayTrace.so')
)
foreach ($pair in $platformPairs) {
    if (-not ($pair | Where-Object { Test-Path -LiteralPath (Join-Path $root $_) -PathType Leaf })) {
        $missing.Add(($pair -join ' OR '))
    }
}

if ($missing.Count -gt 0) {
    $missing | ForEach-Object { Write-Error "Missing: $_" }
    throw "Server install is incomplete. Runtime capability checks will fail closed."
}
Write-Output "Static install layout passed. Use !status in chat and the M0 in-game checklist to verify capabilities and native hooks."

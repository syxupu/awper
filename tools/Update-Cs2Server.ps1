[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$SteamCmd,

    [Parameter(Mandatory)]
    [string]$ServerRoot
)

$ErrorActionPreference = 'Stop'
$steam = (Resolve-Path -LiteralPath $SteamCmd).Path
$root = (Resolve-Path -LiteralPath $ServerRoot).Path
if (Get-Process -Name 'cs2' -ErrorAction SilentlyContinue) {
    throw 'cs2.exe is running; stop it before SteamCMD update.'
}

$nativeArgs = @(
    '+force_install_dir', $root,
    '+login', 'anonymous',
    '+app_update', '730', 'validate',
    '+quit'
)
& $steam @nativeArgs
$exitCode = $LASTEXITCODE
if ($exitCode -ne 0) {
    throw "SteamCMD failed with exit code $exitCode"
}

$manifest = Join-Path $root 'steamapps\appmanifest_730.acf'
$content = Get-Content -LiteralPath $manifest -Raw
$buildMatch = [regex]::Match($content, '"buildid"\s+"(?<id>\d+)"')
$targetMatch = [regex]::Match($content, '"TargetBuildID"\s+"(?<id>\d+)"')
[ordered]@{
    ExitCode = $exitCode
    BuildId = if ($buildMatch.Success) { $buildMatch.Groups['id'].Value } else { $null }
    TargetBuildId = if ($targetMatch.Success) { $targetMatch.Groups['id'].Value } else { $null }
    ManifestLastWriteTime = (Get-Item -LiteralPath $manifest).LastWriteTime
} | ConvertTo-Json

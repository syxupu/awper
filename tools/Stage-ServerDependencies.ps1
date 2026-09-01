[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$StageDirectory,

    [Parameter(Mandatory)]
    [string]$BotControllerSource
)

$ErrorActionPreference = 'Stop'
$stage = [System.IO.Path]::GetFullPath($StageDirectory)
$botSource = (Resolve-Path -LiteralPath $BotControllerSource).Path
New-Item -ItemType Directory -Path $stage -Force | Out-Null

$rayAssets = @(
    [pscustomobject]@{
        Name = 'RayTrace-CSS-API-v1.0.16.tar.gz'
        Sha256 = 'E865CA551DA35AF31DC70F271840D1DCE932A84E1C1BD27AA5AD3191EFE6E1D4'
        Url = 'https://github.com/FUNPLAY-pro-CS2/Ray-Trace/releases/download/v1.0.16/RayTrace-CSS-API-v1.0.16.tar.gz'
    },
    [pscustomobject]@{
        Name = 'RayTrace-MM-v1.0.16-windows.tar.gz'
        Sha256 = '020B7B49CB249793A6840AF3EDFAF8D07C3C8A8069C8246972D113D047429C59'
        Url = 'https://github.com/FUNPLAY-pro-CS2/Ray-Trace/releases/download/v1.0.16/RayTrace-MM-v1.0.16-windows.tar.gz'
    }
)

foreach ($asset in $rayAssets) {
    $assetPath = Join-Path $stage $asset.Name
    $needsDownload = -not (Test-Path -LiteralPath $assetPath -PathType Leaf)
    if (-not $needsDownload) {
        $needsDownload = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash -ne $asset.Sha256
    }
    if ($needsDownload) {
        $nativeArgs = @(
            '--fail', '--location', '--ipv4', '--http1.1',
            '--retry', '5', '--retry-all-errors', '--retry-delay', '2',
            '--connect-timeout', '20', '--max-time', '300',
            '--continue-at', '-',
            '--output', $assetPath,
            ('https://gh-proxy.com/' + $asset.Url)
        )
        & curl.exe @nativeArgs
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "curl failed to download $($asset.Name) with exit code $exitCode"
        }
    }
    $actual = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
    if ($actual -ne $asset.Sha256) {
        Remove-Item -LiteralPath $assetPath -Force
        throw "Checksum mismatch for $($asset.Name): expected $($asset.Sha256), got $actual"
    }
}

$botBuildArgs = @('build', (Join-Path $botSource 'csharp\BotControllerImpl'), '-c', 'Release')
& dotnet @botBuildArgs
$botExitCode = $LASTEXITCODE
if ($botExitCode -ne 0) {
    throw "dotnet build for BotController ABI 19 managed components failed with exit code $botExitCode"
}

$botManagedStage = Join-Path $stage 'BotController-ABI19-managed'
$botShared = Join-Path $botManagedStage 'addons\counterstrikesharp\shared\BotControllerApi'
$botPlugin = Join-Path $botManagedStage 'addons\counterstrikesharp\plugins\BotControllerImpl'
New-Item -ItemType Directory -Path $botShared -Force | Out-Null
New-Item -ItemType Directory -Path $botPlugin -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $botSource 'csharp\BotControllerApi\bin\Release\BotControllerApi.dll') -Destination $botShared -Force
Copy-Item -LiteralPath (Join-Path $botSource 'csharp\BotControllerImpl\bin\Release\BotControllerImpl.dll') -Destination $botPlugin -Force

$archives = foreach ($asset in $rayAssets) {
    $assetPath = Join-Path $stage $asset.Name
    $nativeArgs = @('-tf', $assetPath)
    $entries = & tar.exe @nativeArgs
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "tar failed to inspect $assetPath with exit code $exitCode"
    }
    [pscustomobject]@{
        Name = $asset.Name
        Sha256 = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash
        Entries = @($entries)
    }
}

[ordered]@{
    StageDirectory = $stage
    RayTraceArchives = @($archives)
    BotControllerManagedFiles = @(
        Get-ChildItem -LiteralPath $botManagedStage -File -Recurse |
            Select-Object FullName, Length, @{ Name = 'Sha256'; Expression = { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } }
    )
} | ConvertTo-Json -Depth 6

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CsgoDirectory,

    [Parameter(Mandatory)]
    [string]$ServerRoot
)

$ErrorActionPreference = 'Stop'
$csgo = (Resolve-Path -LiteralPath $CsgoDirectory).Path
$root = (Resolve-Path -LiteralPath $ServerRoot).Path
$addons = Join-Path $csgo 'addons'
$css = Join-Path $addons 'counterstrikesharp'

function Get-ManagedAssemblyInfo {
    param([Parameter(Mandatory)][string]$Path)
    try {
        $name = [System.Reflection.AssemblyName]::GetAssemblyName($Path)
        [pscustomobject]@{
            Name = $name.Name
            Version = $name.Version.ToString()
            PublicKeyToken = if ($name.GetPublicKeyToken().Length) { [Convert]::ToHexString($name.GetPublicKeyToken()) } else { '' }
        }
    }
    catch {
        $null
    }
}

$interestingFiles = Get-ChildItem -LiteralPath $addons -File -Recurse -ErrorAction SilentlyContinue |
    Where-Object {
        $_.FullName -match '(?i)(BotController|RayTrace|counterstrikesharp|metamod|AwperTrainer)' -and
        $_.Extension -in @('.dll', '.so', '.vdf', '.json', '.deps.json', '.runtimeconfig.json')
    } |
    Sort-Object FullName

$files = foreach ($file in $interestingFiles) {
    $assembly = if ($file.Extension -eq '.dll') { Get-ManagedAssemblyInfo -Path $file.FullName } else { $null }
    [pscustomobject]@{
        FullName = $file.FullName
        Length = $file.Length
        LastWriteTime = $file.LastWriteTime
        FileVersion = $file.VersionInfo.FileVersion
        Assembly = $assembly
        Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    }
}

$startScript = Join-Path $root 'Start-TrackDirectorServer.ps1'
$configs = @(
    (Join-Path $css 'configs\core.json'),
    (Join-Path $css 'configs\plugins\BotControllerImpl\BotControllerImpl.json'),
    (Join-Path $css 'configs\plugins\CS2TrackDirector.Game\CS2TrackDirector.Game.json')
)

[ordered]@{
    Files = @($files)
    StartScript = if (Test-Path -LiteralPath $startScript) { Get-Content -LiteralPath $startScript -Raw } else { $null }
    Configs = @(
        foreach ($config in $configs) {
            if (Test-Path -LiteralPath $config -PathType Leaf) {
                [pscustomobject]@{ Path = $config; Content = Get-Content -LiteralPath $config -Raw }
            }
        }
    )
    RecentErrors = @(
        Get-ChildItem -LiteralPath (Join-Path $css 'logs') -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending |
            Select-Object -First 5 |
            ForEach-Object {
                $matches = Select-String -LiteralPath $_.FullName -Pattern 'error|fail|exception|abi|capab|BotController|RayTrace' -CaseSensitive:$false
                [pscustomobject]@{
                    Path = $_.FullName
                    Matches = @($matches | Select-Object -Last 100 | ForEach-Object { $_.Line })
                }
            }
    )
} | ConvertTo-Json -Depth 8

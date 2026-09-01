[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $ClientRoot,

    [Parameter(Mandatory)]
    [string] $AwperPackage,

    [bool] $InstallAutoexec = $true
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path -LiteralPath $ClientRoot).Path
$csgo = (Resolve-Path -LiteralPath (Join-Path $root 'game\csgo')).Path
$package = (Resolve-Path -LiteralPath $AwperPackage).Path
if (-not (Test-Path -LiteralPath (Join-Path $csgo 'gameinfo.gi') -PathType Leaf)) {
    throw "Resolved directory is not a CS2 client root: $root"
}

$tempParent = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$tempExtract = Join-Path $tempParent ('awper-client-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempExtract | Out-Null
try {
    & tar.exe @('-xf', $package, '-C', $tempExtract)
    if ($LASTEXITCODE -ne 0) { throw 'Failed to extract AwperTrainer package.' }

    $resourceMap = [ordered]@{
        'resources\awper_hud.vjs_c' = 'scripts\awper\awper_hud.vjs_c'
        'resources\awper_hud.vxml_c' = 'panorama\layout\custom_game\awper_hud.vxml_c'
        'resources\awper_hud.vcss_c' = 'panorama\styles\custom_game\awper_hud.vcss_c'
    }
    foreach ($entry in $resourceMap.GetEnumerator()) {
        $source = Join-Path $tempExtract $entry.Key
        if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { throw "Client HUD asset is missing: $source" }
        $destination = Join-Path $csgo $entry.Value
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath $source -Destination $destination -Force
    }

    $cfgDirectory = Join-Path $csgo 'cfg'
    New-Item -ItemType Directory -Path $cfgDirectory -Force | Out-Null
    $bindingsTarget = Join-Path $cfgDirectory 'awper_bindings.cfg'
    Copy-Item -LiteralPath (Join-Path $tempExtract 'awper_bindings.cfg') -Destination $bindingsTarget -Force

    $autoexecTarget = Join-Path $cfgDirectory 'autoexec.cfg'
    if ($InstallAutoexec) {
        $existing = if (Test-Path -LiteralPath $autoexecTarget -PathType Leaf) {
            [System.IO.File]::ReadAllText($autoexecTarget)
        } else { '' }
        if ($existing -notmatch '(?im)^\s*exec\s+awper_bindings(?:\.cfg)?\s*$') {
            if ($existing.Length -gt 0) {
                $backup = "$autoexecTarget.awper-backup-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
                Copy-Item -LiteralPath $autoexecTarget -Destination $backup -Force
            }
            $prefix = if ($existing.Length -gt 0 -and -not $existing.EndsWith("`n")) { "`r`n" } else { '' }
            [System.IO.File]::AppendAllText($autoexecTarget, "${prefix}exec awper_bindings`r`n", [System.Text.UTF8Encoding]::new($false))
        }
    }

    [ordered]@{
        ClientRoot = $root
        Bindings = $bindingsTarget
        Autoexec = if ($InstallAutoexec) { $autoexecTarget } else { $null }
        HudFiles = @($resourceMap.Values | ForEach-Object { Join-Path $csgo $_ })
        RestartRequired = $true
    } | ConvertTo-Json -Depth 4
}
finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath($tempExtract)
    $tempLeaf = Split-Path -Leaf $resolvedTemp
    $isInsideTemp = $resolvedTemp.StartsWith($tempParent, [System.StringComparison]::OrdinalIgnoreCase)
    $hasExpectedName = $tempLeaf.StartsWith('awper-client-', [System.StringComparison]::Ordinal)
    if ($isInsideTemp -and $hasExpectedName) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction SilentlyContinue
    }
}

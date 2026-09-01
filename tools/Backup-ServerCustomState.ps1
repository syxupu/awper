[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$ServerRoot
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $ServerRoot).Path
$csgo = (Resolve-Path -LiteralPath (Join-Path $root 'game\csgo')).Path
if (Get-Process -Name 'cs2' -ErrorAction SilentlyContinue) {
    throw 'cs2.exe is running; stop it before taking the pre-update backup.'
}

$backupParent = [System.IO.Path]::GetFullPath((Join-Path $root 'backups'))
$backupRoot = [System.IO.Path]::GetFullPath((Join-Path $backupParent ('pre-steam-update-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))))
if (-not $backupRoot.StartsWith($backupParent + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe backup target: $backupRoot"
}
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null

$relativeTargets = @(
    'Start-TrackDirectorServer.ps1',
    'Start-AwperValidationServer.ps1',
    'Stop-AwperValidationServer.ps1',
    'game\csgo\addons',
    'game\csgo\cfg\trackdirector.cfg',
    'game\csgo\cfg\server.cfg',
    'game\csgo\cfg\gamemode_competitive_server.cfg'
)

$copied = [System.Collections.Generic.List[string]]::new()
foreach ($relative in $relativeTargets) {
    $source = [System.IO.Path]::GetFullPath((Join-Path $root $relative))
    if (-not $source.StartsWith($root + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unsafe backup source: $source"
    }
    if (-not (Test-Path -LiteralPath $source)) { continue }
    $destination = Join-Path $backupRoot $relative
    $destinationParent = [System.IO.Path]::GetDirectoryName($destination)
    New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
    Copy-Item -LiteralPath $source -Destination $destination -Recurse -Force
    $copied.Add($relative)
}

$files = Get-ChildItem -LiteralPath $backupRoot -File -Recurse
[ordered]@{
    BackupDirectory = $backupRoot
    CopiedTargets = @($copied)
    FileCount = $files.Count
    TotalBytes = ($files | Measure-Object -Property Length -Sum).Sum
} | ConvertTo-Json -Depth 4

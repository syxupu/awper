[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$solution = Join-Path $PSScriptRoot 'AwperTrainer.slnx'
$restoreArgs = @('restore', $solution, '--nologo')
& dotnet @restoreArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE" }

$buildArgs = @('build', $solution, '--configuration', $Configuration, '--no-restore', '--nologo')
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with exit code $LASTEXITCODE" }

$testProjects = @(
    (Join-Path $PSScriptRoot 'tests\AwperTrainer.Core.Tests\AwperTrainer.Core.Tests.csproj'),
    (Join-Path $PSScriptRoot 'tests\AwperTrainer.Plugin.Tests\AwperTrainer.Plugin.Tests.csproj')
)
foreach ($testProject in $testProjects) {
    $testArgs = @('test', $testProject, '--configuration', $Configuration, '--no-build', '--nologo')
    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet test failed for $testProject with exit code $LASTEXITCODE" }
}

$formatArgs = @('format', $solution, '--verify-no-changes', '--no-restore')
& dotnet @formatArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet format verification failed with exit code $LASTEXITCODE" }

$scriptFiles = Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.ps1' -File -Recurse |
    Where-Object { $_.FullName -notlike (Join-Path $PSScriptRoot 'artifacts\*') }
foreach ($scriptFile in $scriptFiles) {
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$null, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) {
        $messages = $parseErrors | ForEach-Object { "$($_.Extent.StartLineNumber): $($_.Message)" }
        throw "PowerShell parse failed for $($scriptFile.FullName): $($messages -join '; ')"
    }
}

$pluginOutput = Join-Path $PSScriptRoot "src\AwperTrainer.Plugin\bin\$Configuration"
$packageScript = Join-Path $PSScriptRoot 'package.ps1'
& pwsh.exe -NoLogo -NoProfile -NonInteractive -File $packageScript -Configuration $Configuration -NoBuild
if ($LASTEXITCODE -ne 0) { throw "package.ps1 failed with exit code $LASTEXITCODE" }
Write-Output "Build verified. Plugin output: $pluginOutput"

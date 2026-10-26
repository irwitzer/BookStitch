# BookStitch development-only localization exporter. No absolute machine-specific paths.
[CmdletBinding()]
param(
    [ValidateSet('Development', 'Release', 'Verify', 'InitializeBaselines', 'AcceptTranslations')]
    [string]$Mode = 'Verify',
    [string]$Master,
    [string]$Resources,
    [string]$AcceptFile,
    [string]$StateDirectory
)
$ErrorActionPreference = 'Stop'
$toolDir = $PSScriptRoot
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $toolDir '../..'))
if (-not $Resources) { $Resources = Join-Path $repoRoot 'BookStitch/Resources' }
$Resources = [System.IO.Path]::GetFullPath($Resources)
if (-not $Master) { $Master = Join-Path $Resources 'BookStitch.LanguageMaster.json' }
$Master = [System.IO.Path]::GetFullPath($Master)
$project = Join-Path $toolDir 'LocalizationExporter.csproj'
if (-not (Test-Path -LiteralPath $Master -PathType Leaf)) {
    throw "Language Master fehlt: $Master. Die bisherige Master-v0.8-Datei zuerst als BookStitch.LanguageMaster.json ablegen."
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet SDK nicht gefunden. Benötigt wird das .NET 10 SDK.'
}
$argsForApp = @('--mode', $Mode.ToLowerInvariant(), '--master', $Master, '--resources', $Resources)
if ($AcceptFile) { $argsForApp += @('--accept-file', [System.IO.Path]::GetFullPath($AcceptFile)) }
if ($StateDirectory) { $argsForApp += @('--state-directory', [System.IO.Path]::GetFullPath($StateDirectory)) }
& dotnet run --project $project --configuration Release -- @argsForApp
if ($LASTEXITCODE -ne 0) { throw "LocalizationExporter meldet Fehlercode $LASTEXITCODE." }

[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ZigExecutable,
    [Parameter(Mandatory)][string]$WorkDirectory
)
$ErrorActionPreference = 'Stop'
$archive = Join-Path $PSScriptRoot 'libwebp/libwebp-1.6.0.tar.gz'
$expected = 'E4AB7009BF0629FD11982D4C2AA83964CF244CFFBA7347ECD39019A9E38C4564'
if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $expected) { throw 'libwebp source hash mismatch.' }
$work = [IO.Path]::GetFullPath($WorkDirectory)
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..')).TrimEnd('\') + '\'
if ($work.StartsWith($repository, [StringComparison]::OrdinalIgnoreCase)) { throw 'Use an external build directory.' }
New-Item -ItemType Directory -Path $work -Force | Out-Null
& tar -xzf $archive -C $work
if ($LASTEXITCODE -ne 0) { throw 'Source extraction failed.' }
$source = Join-Path $work 'libwebp-1.6.0'
$sources = @('dec','demux','dsp','utils') | ForEach-Object {
    Get-ChildItem -LiteralPath (Join-Path $source "src/$_") -Filter '*.c' | Select-Object -ExpandProperty FullName
}
$previousCache = $env:ZIG_GLOBAL_CACHE_DIR
try {
    $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $work 'zig-cache'
    & $ZigExecutable cc -target x86_64-windows-gnu -O2 -s -DNDEBUG -DWEBP_STATIC -I $source (Join-Path $PSScriptRoot 'webp-worker.c') @sources -o (Join-Path $PSScriptRoot 'cover-webp-worker.exe')
    if ($LASTEXITCODE -ne 0) { throw 'WebP worker build failed.' }
} finally { $env:ZIG_GLOBAL_CACHE_DIR = $previousCache }
Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'cover-webp-worker.exe') -Algorithm SHA256

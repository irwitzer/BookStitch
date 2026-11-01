#Requires -Version 5.1
# BookStitch public, interactive, Windows-only release packaging helper.
# Supports Windows PowerShell 5.1 and PowerShell 7. No repository changes.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$toolDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$settingsPath = Join-Path $toolDir 'builder-settings.json'
$innoPackage = 'JRSoftware.InnoSetup.7'

function Fail([string]$message) { throw $message }
function Ask([string]$label, [string]$default) {
    $answer = Read-Host ("{0} [{1}]" -f $label, $default)
    if ([string]::IsNullOrWhiteSpace($answer)) { return $default }
    return $answer.Trim().Trim('"')
}
function CanonicalPath([string]$path) {
    $expanded = [Environment]::ExpandEnvironmentVariables($path.Trim().Trim('"'))
    return [IO.Path]::GetFullPath($expanded)
}
function AssertExists([string]$path, [string]$name, [bool]$isDir = $false) {
    if ($isDir) { if (-not [IO.Directory]::Exists($path)) { Fail "$name not found: $path" } }
    elseif (-not [IO.File]::Exists($path)) { Fail "$name not found: $path" }
}

# Windows standard dialogs. STA is ensured by the BAT launcher.
function PrepareDialogs {
    if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
        Fail 'Windows file dialogs require STA. Start BookStitchReleaseBuilder_v3_Start.bat or launch PowerShell with -STA.'
    }
    Add-Type -AssemblyName System.Windows.Forms
}
function SelectFileDialog([string]$title, [string]$initialPath) {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    try {
        $dlg.Title = $title
        $dlg.Filter = 'Inno Setup script (*.iss)|*.iss'
        $dlg.CheckFileExists = $true
        $dlg.Multiselect = $false
        if ($dlg.PSObject.Properties['AutoUpgradeEnabled']) { $dlg.AutoUpgradeEnabled = $true }
        if ($initialPath -and [IO.File]::Exists($initialPath)) {
            $dlg.InitialDirectory = [IO.Path]::GetDirectoryName($initialPath)
            $dlg.FileName = [IO.Path]::GetFileName($initialPath)
        } else { $dlg.InitialDirectory = $toolDir }
        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Fail 'File selection cancelled.' }
        return CanonicalPath $dlg.FileName
    } finally { $dlg.Dispose() }
}
function SelectFolderDialog([string]$title, [string]$initialPath) {
    $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        $dlg.Description = $title
        $dlg.ShowNewFolderButton = $true
        if ($dlg.PSObject.Properties['AutoUpgradeEnabled']) { $dlg.AutoUpgradeEnabled = $true }
        if ($initialPath -and [IO.Directory]::Exists($initialPath)) {
            $dlg.SelectedPath = $initialPath
        } elseif ($initialPath) {
            $parent = $initialPath
            while ($parent -and -not [IO.Directory]::Exists($parent)) { $parent = [IO.Path]::GetDirectoryName($parent) }
            if ($parent) { $dlg.SelectedPath = $parent }
        }
        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Fail 'Folder selection cancelled.' }
        return CanonicalPath $dlg.SelectedPath
    } finally { $dlg.Dispose() }
}
function SelectOutputFilename([string]$description, [string]$default, [string]$extension) {
    Write-Host "  Enter = default filename: $default"
    $entered = Read-Host $description
    if ([string]::IsNullOrWhiteSpace($entered)) { return $default }
    $entered = $entered.Trim().Trim('"')
    if ([IO.Path]::GetFileName($entered) -ne $entered -or $entered -match '[\\/]' -or $entered -match '[<>:"|?*]' -or $entered.EndsWith('.') -or $entered.EndsWith(' ')) {
        Fail "Invalid filename: $entered (filename only, no path)."
    }
    if (-not $entered.EndsWith($extension,[StringComparison]::OrdinalIgnoreCase)) { $entered += $extension }
    if ($entered -eq $extension -or $entered.Length -gt 200) { Fail 'Invalid filename length.' }
    return $entered
}

function ResolveReleaseFilename([string]$template, [string]$tag, [string]$candidate) {
    # Migrate only former standard names; preserve literal custom filenames.
    if ($template -match '^BookStitch-(Setup|Portable)-\d+\.\d+\.\d+(?:\.\d+)?(?:-[A-Za-z0-9][A-Za-z0-9._-]{0,30})?\.(exe|zip)$') {
        $template = 'BookStitch-' + $Matches[1] + '-{Tag}.' + $Matches[2]
    }
    return $template.Replace('{Tag}', $tag).Replace('{Candidate}', $candidate)
}

function ReadSettings {
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) { return $null }
    try {
        $obj = Get-Content -LiteralPath $settingsPath -Raw -Encoding UTF8 | ConvertFrom-Json
        foreach ($key in @('IssPath','RepositoryPath','OutputPath')) {
            if (-not $obj.PSObject.Properties[$key] -or [string]::IsNullOrWhiteSpace([string]$obj.$key)) { throw "Missing $key" }
        }
        return $obj
    } catch {
        Write-Warning "Unable to read saved settings: $($_.Exception.Message). New input required."
        return $null
    }
}
function FindInno {
    $places = @(
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe')
    )
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $places += $cmd.Source }
    foreach ($p in $places) {
        if (-not $p -or -not [IO.File]::Exists($p)) { continue }
        $out = & $p --version 2>&1
        if ($LASTEXITCODE -eq 0 -and ("$out" -match '^(\d+)\.(\d+)(?:\.(\d+))?')) {
            $version = [version]("{0}.{1}.{2}" -f $Matches[1],$Matches[2],$(if ($Matches[3]) { $Matches[3] } else { '0' }))
            if ($version -ge [version]'7.1.0') { return $p }
        }
    }
    return $null
}
function EnsureInno {
    $iscc = FindInno
    if ($iscc) { return $iscc }
    Write-Host 'Inno Setup 7.1.0 or newer not found. Installing the latest version automatically.' -ForegroundColor Yellow
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) { Fail 'winget.exe is not available. Install Inno Setup 7 manually: https://jrsoftware.org/isdl.php' }
    # Send installer output to the console, not the function return pipeline.
    & $winget.Source install --id $innoPackage --exact --source winget --scope machine --accept-package-agreements --accept-source-agreements | Out-Host
    $installExitCode = $LASTEXITCODE
    if ($installExitCode -ne 0) {
        # An older installed version may require upgrade instead of install.
        & $winget.Source upgrade --id $innoPackage --exact --source winget --accept-package-agreements --accept-source-agreements | Out-Host
        $upgradeExitCode = $LASTEXITCODE
        if ($upgradeExitCode -ne 0) { Fail "Inno Setup installation/update failed (exit code $upgradeExitCode)." }
    }
    $iscc = FindInno
    if (-not $iscc) { Fail 'Inno Setup 7.1.0 or newer was not found after installation/update.' }
    Write-Host "Inno Setup successfully installed/updated: $iscc (version $(& $iscc --version))" -ForegroundColor Green
    return $iscc
}
function SelectIss([string]$old) {
    $scripts = @(Get-ChildItem -LiteralPath $toolDir -Filter '*.iss' -File)
    if ($scripts.Count -eq 1) {
        Write-Host "ISS file automatically detected in script folder: $($scripts[0].FullName)"
        return CanonicalPath $scripts[0].FullName
    }
    if ($scripts.Count -gt 1) {
        Write-Host 'Multiple ISS files found in script folder - please select one.'
    } else {
        Write-Host 'No ISS file found in script folder - please select one.'
    }
    return SelectFileDialog 'Select Inno Setup ISS file' $old
}
function GetVersion([string]$project) {
    [xml]$xml = Get-Content -LiteralPath $project -Raw -Encoding UTF8
    $versionNode = $xml.SelectSingleNode('//PropertyGroup/Version')
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) { Fail 'Version is missing from BookStitch.csproj.' }
    $version = $versionNode.InnerText.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { Fail "Unexpected version format: $version" }
    return $version
}
function Execute([string]$command, [string[]]$arguments, [string]$label) {
    Write-Host "`n== $label ==" -ForegroundColor Cyan
    & $command @arguments
    if ($LASTEXITCODE -ne 0) { Fail "$label failed (exit code $LASTEXITCODE)." }
}
function MakeZip([string]$folder, [string]$destination) {
    Add-Type -AssemblyName System.IO.Compression
    $parent = Split-Path -Parent $folder
    $rootName = Split-Path -Leaf $folder
    $allFiles = @(Get-ChildItem -LiteralPath $folder -File -Recurse)
    if ($allFiles.Count -lt 3) { Fail 'Publish output is unexpectedly empty; ZIP was not created.' }
    $attempt = 0
    while ($attempt -lt 6) {
        $attempt++
        $zip = $null
        try {
            if ([IO.File]::Exists($destination)) { [IO.File]::Delete($destination) }
            $zipStream = [IO.File]::Open($destination,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
            try {
                $zip = [System.IO.Compression.ZipArchive]::new($zipStream,[IO.Compression.ZipArchiveMode]::Create,$false)
                foreach ($f in $allFiles) {
                    $relative = $f.FullName.Substring($parent.Length).TrimStart([char]92,[char]47) -replace '\\','/'
                    $entry = $zip.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
                    $input = [IO.File]::Open($f.FullName,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
                    try {
                        $output = $entry.Open()
                        try { $input.CopyTo($output) } finally { $output.Dispose() }
                    } finally { $input.Dispose() }
                }
            } finally {
                if ($zip) { $zip.Dispose() } else { $zipStream.Dispose() }
            }
            return
        } catch {
            if ($attempt -ge 6) { throw }
            Write-Warning "ZIP attempt $attempt failed: $($_.Exception.Message); retrying."
            Start-Sleep -Seconds 2
        }
    }
}

try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { Fail 'This tool requires Windows.' }
    Write-Host 'BookStitch Release Builder (PowerShell 5.1 / 7)' -ForegroundColor Cyan
    Write-Host "Build script: $toolDir"
    $iscc = EnsureInno
    Write-Host "Inno Setup 7: $iscc"

    PrepareDialogs
    $settings = ReadSettings
    $reuse = $false
    if ($settings) {
        Write-Host "`nSaved settings:"
        Write-Host "  ISS file:  $($settings.IssPath)"
        Write-Host "  Repository: $($settings.RepositoryPath)"
        Write-Host "  Output:    $($settings.OutputPath)"
        $reply = Read-Host 'Build using these paths? (Y/N)'
        $reuse = ($reply -match '^(j|ja|y|yes)$')
    }
    if ($reuse) {
        $iss = CanonicalPath ([string]$settings.IssPath)
        $repo = CanonicalPath ([string]$settings.RepositoryPath)
        $outputRoot = CanonicalPath ([string]$settings.OutputPath)
    } else {
        $iss = SelectIss ''
        $repo = SelectFolderDialog 'Select the local BookStitch repository (folder containing the BookStitch subfolder)' (Join-Path (Split-Path -Parent $toolDir) 'BookStitch-Public')
        $outputRoot = SelectFolderDialog 'Select output folder for release files' (Join-Path (Split-Path -Parent $repo) 'BookStitch-Releases')
    }
    AssertExists $iss 'ISS file'
    if ([IO.Path]::GetExtension($iss) -ine '.iss') { Fail 'The selected file does not have the .iss extension.' }
    AssertExists $repo 'Repository' $true
    $project = Join-Path $repo 'BookStitch\BookStitch.csproj'
    $profile = Join-Path $repo 'BookStitch\Properties\PublishProfiles\EndUser.pubxml'
    $readme = Join-Path $repo 'README.md'
    $license = Join-Path $repo 'LICENSE'
    $notices = Join-Path $repo 'THIRD_PARTY_NOTICES.md'
    foreach ($item in @($project,$profile,$readme,$license,$notices)) { AssertExists $item 'Release input file' }
    $version = GetVersion $project
    if ($outputRoot.TrimEnd([char]92) -eq $repo.TrimEnd([char]92) -or $outputRoot.StartsWith(($repo.TrimEnd([char]92) + [IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)) { Fail 'The output folder must not be inside the source repository.' }
    $tag = $version
    $versionInfo = $version
    if ($versionInfo -match '^\d+\.\d+\.\d+$') { $versionInfo += '.0' }
    $buildStamp = Get-Date -Format 'yyyy.MM.dd--HH.mm.ss'
    $releaseDir = Join-Path $outputRoot ("{0}_BookStitch" -f $buildStamp)
    $publishDir = Join-Path $releaseDir "BookStitch-$tag"
    $setupName = "BookStitch-Setup-$tag.exe"
    $zipName = "BookStitch-Portable-$tag.zip"
    $zipPath = Join-Path $releaseDir $zipName
    $setupPath = Join-Path $releaseDir $setupName
    $shaPath = Join-Path $releaseDir "BookStitch-$tag-SHA256SUMS.txt"
    if (Test-Path -LiteralPath $releaseDir) { Fail "Release folder already exists; refusing to overwrite: $releaseDir" }
    foreach ($p in @($publishDir,$zipPath,$setupPath,$shaPath)) {
        if (Test-Path -LiteralPath $p) { Fail "Output already exists; refusing to overwrite: $p" }
    }
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $dotnet) { Fail '.NET SDK is missing. Install the .NET 10 SDK and try again.' }
    Execute $dotnet.Source @('--list-sdks') 'List .NET SDKs'
    $sdkList = & $dotnet.Source --list-sdks
    if (-not (@($sdkList | Where-Object { $_ -match '^10\.' }).Count)) { Fail 'No .NET 10 SDK is installed.' }

    # Preprocess validates the preprocessor, defines and basic ISS structure.
    # Full Pascal/script compilation is performed by ISCC after publish.
    $defines = @(
        "--define=MyAppVersion=$version",
        "--define=MyVersionInfoVersion=$versionInfo",
        "--define=SourceDir=$publishDir",
        "--define=SourceRoot=$repo",
        "--define=OutputDir=$releaseDir"
    )
    Write-Host "`n== ISS precheck (Inno 7) ==" -ForegroundColor Cyan
    $preErrorFile = Join-Path ([IO.Path]::GetTempPath()) ('BookStitch-iss-' + [Guid]::NewGuid().ToString('N') + '.err')
    try {
        # Prevent very large preprocessor output in the console.
        & $iscc --preprocess @defines $iss 1> $null 2> $preErrorFile
        $preExit = $LASTEXITCODE
        if ($preExit -ne 0) {
            $details = Get-Content -LiteralPath $preErrorFile -Raw -ErrorAction SilentlyContinue
            Fail "ISS precheck failed (code $preExit). Check or recreate the ISS file.`n$details"
        }
    } finally { Remove-Item -LiteralPath $preErrorFile -Force -ErrorAction SilentlyContinue }
    Write-Host 'ISS precheck successful. Full compilation will follow after publishing.' -ForegroundColor Green

    if (-not $reuse) {
        $save = [ordered]@{ IssPath=$iss; RepositoryPath=$repo; OutputPath=$outputRoot }
        $json = $save | ConvertTo-Json
        [IO.File]::WriteAllText($settingsPath,$json,(New-Object System.Text.UTF8Encoding($true)))
        Write-Host "Settings saved: $settingsPath"
    }

    Write-Host "`nSource: $repo"
    Write-Host "Output root: $outputRoot"
    Write-Host "Release folder: $releaseDir"
    Write-Host "Version: $tag"
    Write-Host "Setup file: $setupName"
    Write-Host "Portable ZIP: $zipName"
    Write-Host 'Release build starts without further prompts.' -ForegroundColor Cyan

    [IO.Directory]::CreateDirectory($releaseDir) | Out-Null
    Execute $dotnet.Source @('publish',$project,'-c','Release','-p:PublishProfile=EndUser','-o',$publishDir) 'Self-contained Windows x64 Publish'
    AssertExists (Join-Path $publishDir 'BookStitch.exe') 'Published BookStitch.exe'
    AssertExists (Join-Path $publishDir 'BookStitch.runtimeconfig.json') '.NET runtime configuration'
    AssertExists (Join-Path $publishDir 'TagLibSharp.dll') 'TagLibSharp runtime file'
    Write-Host "`n== Portable ZIP ==" -ForegroundColor Cyan
    MakeZip $publishDir $zipPath
    AssertExists $zipPath 'Portable-ZIP'
    if ((Get-Item -LiteralPath $zipPath).Length -lt 100000) { Fail 'ZIP appears incomplete.' }
    $setupBase = [IO.Path]::GetFileNameWithoutExtension($setupName)
    $innoArgs = @($defines + @("--output-filename=$setupBase",$iss))
    Execute $iscc $innoArgs 'Inno Setup 7 (Installer)'
    AssertExists $setupPath 'Setup-EXE'
    Write-Host "`n== SHA-256 ==" -ForegroundColor Cyan
    $hashLines = foreach ($file in @($zipPath,$setupPath)) {
        $h = Get-FileHash -LiteralPath $file -Algorithm SHA256
        '{0}  {1}' -f $h.Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file)
    }
    [IO.File]::WriteAllLines($shaPath,[string[]]$hashLines,(New-Object System.Text.UTF8Encoding($false)))
    Write-Host "`nCreated:"
    foreach ($item in @($publishDir,$zipPath,$setupPath,$shaPath)) { Write-Host "  $item" -ForegroundColor Green }
    exit 0
} catch {
    Write-Host "`nERROR: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

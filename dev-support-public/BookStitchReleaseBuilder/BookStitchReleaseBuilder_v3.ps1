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
    if ($isDir) { if (-not [IO.Directory]::Exists($path)) { Fail "$name nicht gefunden: $path" } }
    elseif (-not [IO.File]::Exists($path)) { Fail "$name nicht gefunden: $path" }
}

# Windows standard dialogs. STA is ensured by the BAT launcher.
function PrepareDialogs {
    if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
        Fail 'Windows-Dateidialoge benötigen STA. Bitte BookStitchReleaseBuilder_v3_Start.bat starten oder PowerShell mit -STA aufrufen.'
    }
    Add-Type -AssemblyName System.Windows.Forms
}
function SelectFileDialog([string]$title, [string]$initialPath) {
    $dlg = New-Object System.Windows.Forms.OpenFileDialog
    try {
        $dlg.Title = $title
        $dlg.Filter = 'Inno Setup Skript (*.iss)|*.iss'
        $dlg.CheckFileExists = $true
        $dlg.Multiselect = $false
        if ($dlg.PSObject.Properties['AutoUpgradeEnabled']) { $dlg.AutoUpgradeEnabled = $true }
        if ($initialPath -and [IO.File]::Exists($initialPath)) {
            $dlg.InitialDirectory = [IO.Path]::GetDirectoryName($initialPath)
            $dlg.FileName = [IO.Path]::GetFileName($initialPath)
        } else { $dlg.InitialDirectory = $toolDir }
        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Fail 'Dateiauswahl abgebrochen.' }
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
        if ($dlg.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { Fail 'Ordnerauswahl abgebrochen.' }
        return CanonicalPath $dlg.SelectedPath
    } finally { $dlg.Dispose() }
}
function SelectOutputFilename([string]$description, [string]$default, [string]$extension) {
    Write-Host "  Enter = Standardname: $default"
    $entered = Read-Host $description
    if ([string]::IsNullOrWhiteSpace($entered)) { return $default }
    $entered = $entered.Trim().Trim('"')
    if ([IO.Path]::GetFileName($entered) -ne $entered -or $entered -match '[\\/]' -or $entered -match '[<>:"|?*]' -or $entered.EndsWith('.') -or $entered.EndsWith(' ')) {
        Fail "Ungültiger Dateiname: $entered (nur Dateiname, kein Pfad)."
    }
    if (-not $entered.EndsWith($extension,[StringComparison]::OrdinalIgnoreCase)) { $entered += $extension }
    if ($entered -eq $extension -or $entered.Length -gt 200) { Fail 'Ungültige Dateinamenlänge.' }
    return $entered
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
        Write-Warning "Gespeicherte Einstellungen unlesbar: $($_.Exception.Message). Neue Eingabe erforderlich."
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
        if ($LASTEXITCODE -eq 0 -and ("$out" -match '\b7\.\d+')) { return $p }
    }
    return $null
}
function EnsureInno {
    $iscc = FindInno
    if ($iscc) { return $iscc }
    Write-Host "Inno Setup 7 fehlt oder hat nicht Version 7.x." -ForegroundColor Yellow
    $response = Read-Host "Jetzt mit winget installieren? (J/N)"
    if ($response -notmatch '^(j|ja|y|yes)$') { Fail 'Abgebrochen: Inno Setup 7 ist erforderlich.' }
    $winget = Get-Command winget.exe -ErrorAction SilentlyContinue
    if (-not $winget) { Fail 'winget.exe nicht verfügbar. Inno Setup 7 manuell installieren: https://jrsoftware.org/isdl.php' }
    & $winget.Source install --id $innoPackage --exact --source winget --scope machine --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) { Fail "winget-Installation fehlgeschlagen (Exitcode $LASTEXITCODE)." }
    $iscc = FindInno
    if (-not $iscc) { Fail 'Inno Setup 7 nach der Installation nicht gefunden bzw. Versionsprüfung fehlgeschlagen.' }
    return $iscc
}
function SelectIss([string]$old) {
    $scripts = @(Get-ChildItem -LiteralPath $toolDir -Filter '*.iss' -File)
    if ($scripts.Count -eq 1) {
        Write-Host "ISS im Skriptordner automatisch erkannt: $($scripts[0].FullName)"
        return CanonicalPath $scripts[0].FullName
    }
    if ($scripts.Count -gt 1) {
        Write-Host 'Mehrere ISS-Dateien im Skriptordner - bitte eine Datei wählen.'
    } else {
        Write-Host 'Keine ISS-Datei im Skriptordner - bitte eine Datei wählen.'
    }
    return SelectFileDialog 'Inno Setup ISS-Datei auswählen' $old
}
function GetVersion([string]$project) {
    [xml]$xml = Get-Content -LiteralPath $project -Raw -Encoding UTF8
    $versionNode = $xml.SelectSingleNode('//PropertyGroup/Version')
    if ($null -eq $versionNode -or [string]::IsNullOrWhiteSpace($versionNode.InnerText)) { Fail 'Version im BookStitch.csproj fehlt.' }
    $version = $versionNode.InnerText.Trim()
    if ($version -notmatch '^\d+\.\d+\.\d+(\.\d+)?$') { Fail "Unerwartetes Version-Format: $version" }
    return $version
}
function Execute([string]$command, [string[]]$arguments, [string]$label) {
    Write-Host "`n== $label ==" -ForegroundColor Cyan
    & $command @arguments
    if ($LASTEXITCODE -ne 0) { Fail "$label fehlgeschlagen (Exitcode $LASTEXITCODE)." }
}
function MakeZip([string]$folder, [string]$destination) {
    Add-Type -AssemblyName System.IO.Compression
    $parent = Split-Path -Parent $folder
    $rootName = Split-Path -Leaf $folder
    $allFiles = @(Get-ChildItem -LiteralPath $folder -File -Recurse)
    if ($allFiles.Count -lt 3) { Fail 'Publish-Ausgabe unerwartet leer; ZIP nicht erstellt.' }
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
            Write-Warning "ZIP-Versuch $attempt gescheitert: $($_.Exception.Message); erneuter Versuch."
            Start-Sleep -Seconds 2
        }
    }
}

try {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { Fail 'Dieses Werkzeug benötigt Windows.' }
    Write-Host 'BookStitch Release Builder (PowerShell 5.1 / 7)' -ForegroundColor Cyan
    Write-Host "Build-Skript: $toolDir"
    $iscc = EnsureInno
    Write-Host "Inno Setup 7: $iscc"

    PrepareDialogs
    $settings = ReadSettings
    $reuse = $false
    if ($settings) {
        Write-Host "`nLetzte Einstellungen:"
        Write-Host "  ISS-Datei:  $($settings.IssPath)"
        Write-Host "  Repository: $($settings.RepositoryPath)"
        Write-Host "  Ausgabe:    $($settings.OutputPath)"
        Write-Host "  Kennzeichnung: $($settings.Candidate)"
        $hasNames = [bool]($settings.PSObject.Properties['SetupName'] -and $settings.PSObject.Properties['ZipName'])
        if ($hasNames) {
            Write-Host "  Installer-EXE: $($settings.SetupName)"
            Write-Host "  Portable-ZIP: $($settings.ZipName)"
        }
        if (-not $hasNames -or ($hasNames -and
            ([string]::IsNullOrWhiteSpace([string]$settings.SetupName) -or [string]::IsNullOrWhiteSpace([string]$settings.ZipName)))) {
            Write-Host '  Alte Einstellungen ohne Dateinamen: einmalige Neueingabe erforderlich.' -ForegroundColor Yellow
        }
        $reply = Read-Host 'Mit diesen Einstellungen bauen? (J/N)'
        $reuse = ($reply -match '^(j|ja|y|yes)$') -and $hasNames
        if ($reuse) { $reuse = (-not [string]::IsNullOrWhiteSpace([string]$settings.SetupName)) -and (-not [string]::IsNullOrWhiteSpace([string]$settings.ZipName)) }
    }
    if ($reuse) {
        $iss = CanonicalPath ([string]$settings.IssPath)
        $repo = CanonicalPath ([string]$settings.RepositoryPath)
        $outputRoot = CanonicalPath ([string]$settings.OutputPath)
        $candidate = ([string]$settings.Candidate).Trim()
    } else {
        $iss = SelectIss ''
        $repo = SelectFolderDialog 'Lokales BookStitch-Repository auswählen (Ordner mit BookStitch-Unterordner)' (Join-Path (Split-Path -Parent $toolDir) 'BookStitch-Public')
        $outputRoot = SelectFolderDialog 'Ausgabeordner für Release-Dateien auswählen' (Join-Path (Split-Path -Parent $repo) 'BookStitch-Releases')
        Write-Host 'Release-Kennzeichnung (z.B. RC1 oder FINAL). Enter = kein Suffix.'
        $candidate = (Read-Host 'Suffix').Trim()
    }
    AssertExists $iss 'ISS-Datei'
    if ([IO.Path]::GetExtension($iss) -ine '.iss') { Fail 'Die ausgewählte Datei hat nicht die Endung .iss.' }
    AssertExists $repo 'Repository' $true
    $project = Join-Path $repo 'BookStitch\BookStitch.csproj'
    $profile = Join-Path $repo 'BookStitch\Properties\PublishProfiles\EndUser.pubxml'
    $readme = Join-Path $repo 'README.md'
    $license = Join-Path $repo 'LICENSE'
    $notices = Join-Path $repo 'THIRD_PARTY_NOTICES.md'
    foreach ($item in @($project,$profile,$readme,$license,$notices)) { AssertExists $item 'Release-Eingabedatei' }
    if ($candidate -ne '' -and $candidate -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,30}$') { Fail 'Ungültige Kennzeichnung. Bitte nur Buchstaben, Ziffern, Punkt, Unterstrich und Bindestrich.' }
    $version = GetVersion $project
    if ($outputRoot.TrimEnd([char]92) -eq $repo.TrimEnd([char]92) -or $outputRoot.StartsWith(($repo.TrimEnd([char]92) + [IO.Path]::DirectorySeparatorChar),[StringComparison]::OrdinalIgnoreCase)) { Fail 'Ausgabeordner darf nicht innerhalb des Quell-Repositories liegen.' }
    $tag = if ($candidate -eq '' -or $candidate -ieq 'FINAL') { $version } else { "$version-$candidate" }
    $versionInfo = $version
    if ($versionInfo -match '^\d+\.\d+\.\d+$') { $versionInfo += '.0' }
    $publishDir = Join-Path $outputRoot "BookStitch-$tag"
    if ($reuse) {
        $setupName = [string]$settings.SetupName
        $zipName = [string]$settings.ZipName
        Write-Host "`nGespeicherte Dateinamen werden ohne Rückfrage verwendet." -ForegroundColor Cyan
    } else {
        Write-Host "`nDateinamen der Release-Dateien wählen (Enter übernimmt den Standard)." -ForegroundColor Cyan
        $setupName = SelectOutputFilename 'Name für Installer-EXE' "BookStitch-Setup-$tag.exe" '.exe'
        $zipName = SelectOutputFilename 'Name für portable ZIP' "BookStitch-Portable-$tag.zip" '.zip'
    }
    foreach ($spec in @(@($setupName,'.exe'), @($zipName,'.zip'))) {
        $name = [string]$spec[0]
        $ext = [string]$spec[1]
        if ([string]::IsNullOrWhiteSpace($name) -or [IO.Path]::GetFileName($name) -ne $name -or
            $name -match '[\\/]'  -or $name -match '[<>:"|?*]' -or $name.EndsWith('.') -or
            $name.EndsWith(' ') -or -not $name.EndsWith($ext,[StringComparison]::OrdinalIgnoreCase) -or $name.Length -gt 200) {
            Fail "Ungültiger gespeicherter Dateiname: $name"
        }
    }
    $zipPath = Join-Path $outputRoot $zipName
    $setupPath = Join-Path $outputRoot $setupName
    $shaPath = Join-Path $outputRoot "BookStitch-$tag-SHA256SUMS.txt"
    foreach ($p in @($publishDir,$zipPath,$setupPath,$shaPath)) {
        if (Test-Path -LiteralPath $p) { Fail "Ausgabe existiert bereits, kein Überschreiben: $p" }
    }
    $dotnet = Get-Command dotnet.exe -ErrorAction SilentlyContinue
    if (-not $dotnet) { Fail '.NET SDK fehlt. .NET 10 SDK installieren und erneut starten.' }
    Execute $dotnet.Source @('--list-sdks') '.NET SDK anzeigen'
    $sdkList = & $dotnet.Source --list-sdks
    if (-not (@($sdkList | Where-Object { $_ -match '^10\.' }).Count)) { Fail 'Kein .NET 10 SDK installiert.' }

    # Preprocess validates the preprocessor, defines and basic ISS structure.
    # Full Pascal/script compilation is performed by ISCC after publish.
    $defines = @(
        "--define=MyAppVersion=$version",
        "--define=MyVersionInfoVersion=$versionInfo",
        "--define=SourceDir=$publishDir",
        "--define=SourceRoot=$repo",
        "--define=OutputDir=$outputRoot"
    )
    Write-Host "`n== ISS-Vorprüfung (Inno 7) ==" -ForegroundColor Cyan
    $preErrorFile = Join-Path ([IO.Path]::GetTempPath()) ('BookStitch-iss-' + [Guid]::NewGuid().ToString('N') + '.err')
    try {
        # Prevent very large preprocessor output in the console.
        & $iscc --preprocess @defines $iss 1> $null 2> $preErrorFile
        $preExit = $LASTEXITCODE
        if ($preExit -ne 0) {
            $details = Get-Content -LiteralPath $preErrorFile -Raw -ErrorAction SilentlyContinue
            Fail "ISS-Vorprüfung fehlgeschlagen (Code $preExit). ISS-Datei prüfen oder neu erstellen.`n$details"
        }
    } finally { Remove-Item -LiteralPath $preErrorFile -Force -ErrorAction SilentlyContinue }
    Write-Host 'ISS-Vorprüfung erfolgreich. Vollständige Kompilierung folgt nach Publish.' -ForegroundColor Green

    if (-not $reuse) {
        $save = [ordered]@{ IssPath=$iss; RepositoryPath=$repo; OutputPath=$outputRoot; Candidate=$candidate; SetupName=$setupName; ZipName=$zipName }
        $json = $save | ConvertTo-Json
        [IO.File]::WriteAllText($settingsPath,$json,(New-Object System.Text.UTF8Encoding($true)))
        Write-Host "Einstellungen gespeichert: $settingsPath"
    }

    Write-Host "`nQuelle: $repo"
    Write-Host "Ausgabe: $outputRoot"
    Write-Host "Version: $tag"
    Write-Host "Setup-Datei: $setupName"
    Write-Host "Portable ZIP: $zipName"
    $confirmation = Read-Host 'Release jetzt erstellen? (J/N)'
    if ($confirmation -notmatch '^(j|ja|y|yes)$') { Fail 'Abbruch auf Benutzerwunsch.' }

    [IO.Directory]::CreateDirectory($outputRoot) | Out-Null
    Execute $dotnet.Source @('publish',$project,'-c','Release','-p:PublishProfile=EndUser','-o',$publishDir) 'Self-contained Windows x64 Publish'
    AssertExists (Join-Path $publishDir 'BookStitch.exe') 'Veröffentlichte BookStitch.exe'
    AssertExists (Join-Path $publishDir 'BookStitch.runtimeconfig.json') '.NET Runtime-Konfiguration'
    AssertExists (Join-Path $publishDir 'TagLibSharp.dll') 'TagLibSharp-Laufzeitdatei'
    Write-Host "`n== Portable ZIP ==" -ForegroundColor Cyan
    MakeZip $publishDir $zipPath
    AssertExists $zipPath 'Portable-ZIP'
    if ((Get-Item -LiteralPath $zipPath).Length -lt 100000) { Fail 'ZIP scheint unvollständig.' }
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
    Write-Host "`nErstellt:"
    foreach ($item in @($publishDir,$zipPath,$setupPath,$shaPath)) { Write-Host "  $item" -ForegroundColor Green }
    exit 0
} catch {
    Write-Host "`nFEHLER: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

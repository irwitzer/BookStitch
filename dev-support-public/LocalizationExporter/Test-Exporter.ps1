# Smoke test. Writes logs only below LocalizationExporter/reports; modifies only a temporary master.
[CmdletBinding()]
param([string]$Master)
$ErrorActionPreference = 'Stop'
if (-not $Master) {
    $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
    $Master = Join-Path $root 'BookStitch/Resources/BookStitch.LanguageMaster.json'
}
if (-not (Test-Path -LiteralPath $Master -PathType Leaf)) { throw "Master fehlt: $Master" }
$exporter = Join-Path $PSScriptRoot 'Export-Resx.ps1'
if (-not (Test-Path -LiteralPath $exporter -PathType Leaf)) { throw "Export-Resx.ps1 fehlt: $exporter" }
$reportRoot = Join-Path $PSScriptRoot 'reports'
[IO.Directory]::CreateDirectory($reportRoot) | Out-Null
$runId = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8)
$logDir = Join-Path $reportRoot ('Test-' + $runId)
[IO.Directory]::CreateDirectory($logDir) | Out-Null
$temp = Join-Path $PSScriptRoot ('work\s-' + [guid]::NewGuid().ToString('N').Substring(0,16))
if (Test-Path -LiteralPath $temp) { throw 'Test workspace already exists' }
[IO.Directory]::CreateDirectory($temp) | Out-Null
$copy = Join-Path $temp 'BookStitch.LanguageMaster.json'
Copy-Item -LiteralPath $Master -Destination $copy
$results = New-Object System.Collections.ArrayList
$success = $false
function Run-Step([string]$Label, [string]$Mode, [bool]$ExpectFailure = $false) {
    $index = $results.Count + 1
    $stepLog = Join-Path $logDir ('{0:00}-{1}.log' -f $index, $Label)
    # Run in a separate PowerShell process to capture *all* native .NET output.
    $params = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $exporter,
        '-Mode', $Mode, '-Master', $copy, '-Resources', $temp, '-StateDirectory', $temp)
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & powershell.exe @params 2>&1 | Out-File -LiteralPath $stepLog -Encoding UTF8
        $code = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previousPreference }
    $reportFile = Join-Path $temp 'reports/BookStitch.LocalizationValidation.json'
    $warnings = $null; $errors = $null
    if (Test-Path -LiteralPath $reportFile) {
        try {
            $reportObj = Get-Content -LiteralPath $reportFile -Raw -Encoding UTF8 | ConvertFrom-Json
            $warnings = $reportObj.summary.warnings
            $errors = $reportObj.summary.errors
            # Keep complete diagnostics only for an unexpected result. Successful
            # steps retain counts in TestSummary.json, not repeated large reports.
            if (($code -eq 0) -eq $ExpectFailure) {
                Copy-Item -LiteralPath $reportFile -Destination (Join-Path $logDir ('{0:00}-{1}.json' -f $index, $Label)) -Force
            }
        } catch { }
    }
    $passed = if ($ExpectFailure) { $code -ne 0 } else { $code -eq 0 }
    $result = [pscustomobject]@{ step=$Label; mode=$Mode; expectedFailure=$ExpectFailure; exitCode=$code; passed=$passed; errors=$errors; warnings=$warnings; log=[IO.Path]::GetFileName($stepLog) }
    [void]$results.Add($result)
    $note = if ($ExpectFailure) { 'erwarteter Fehler' } else { 'normaler Lauf' }
    $status = if ($passed) { 'OK' } else { 'FEHLER' }
    Write-Host ("{0}: {1} ({2}; Exit {3}; Fehler {4}; Warnungen {5})" -f $status, $Label, $note, $code, $errors, $warnings)
    if (-not $passed) { throw "Schritt '$Label' unerwartet gescheitert. Siehe $stepLog" }
}
try {
    Write-Host "`nBookStitch LocalizationExporter - TEST (keine produktiven RESX-Aenderungen)"
    Write-Host "Protokollordner: $logDir`n"
    $beforeInitializationHash = (Get-FileHash -LiteralPath $copy).Hash
    $snapshot = Get-Content -LiteralPath $copy -Raw -Encoding UTF8 | ConvertFrom-Json
    $hasBaselines = @($snapshot.entries.PSObject.Properties | Where-Object { $_.Value.PSObject.Properties.Name -contains 'translationBase' }).Count -gt 0
    if ($hasBaselines) {
        Run-Step 'Existing-Baselines-Not-Overwritten' 'InitializeBaselines' $true
        if ((Get-FileHash -LiteralPath $copy).Hash -ne $beforeInitializationHash) { throw 'Reinitialization changed existing baselines.' }
    } else {
        Run-Step 'Baselines' 'InitializeBaselines'
    }
    $initializedHash = (Get-FileHash -LiteralPath $copy).Hash
    Run-Step 'Development' 'Development'
    Run-Step 'Verify-Development' 'Verify'
    Run-Step 'Release' 'Release'
    Run-Step 'Verify-Release' 'Verify'
    if ((Get-FileHash -LiteralPath $copy).Hash -ne $initializedHash) { throw 'Normal export changed Master or baselines.' }
    $content = [IO.File]::ReadAllText($copy, [Text.Encoding]::UTF8)
    $needle = 'Context: Global Settings > language selector'
    if (-not $content.Contains($needle)) { throw 'Referenzkommentar fuer Negativtest nicht gefunden.' }
    [IO.File]::WriteAllText($copy, $content.Replace($needle, 'Context: Global Settings > language selector [smoke-test changed]'), ([Text.UTF8Encoding]::new($false)))
    $changedSourceHash = (Get-FileHash -LiteralPath $copy).Hash
    Run-Step 'Release-With-Stale-Baseline' 'Release'
    Run-Step 'Development-After-Change' 'Development'
    Run-Step 'Verify-After-Change' 'Verify'
    if ((Get-FileHash -LiteralPath $copy).Hash -ne $changedSourceHash) { throw 'Export silently renewed baselines after source change.' }
    $staleReport = Get-Content -LiteralPath (Join-Path $temp 'reports/BookStitch.LocalizationValidation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if (@($staleReport.diagnostics | Where-Object Code -eq 'BASELINE_STALE').Count -eq 0) { throw 'Source-context change did not report stale baselines.' }
    $success = $true
} catch {
    Write-Host "`nFEHLER IM TEST: $($_.Exception.Message)" -ForegroundColor Red
} finally {
    $summary = [ordered]@{ outcome= $(if ($success) { 'TEST_PASSED' } else { 'TEST_FAILED' }); generatedAtUtc=(Get-Date).ToUniversalTime().ToString('o'); steps=@($results.ToArray()); logsDirectory=$logDir; productiveResourcesModified=$false }
    $json = $summary | ConvertTo-Json -Depth 6
    [IO.File]::WriteAllText((Join-Path $logDir 'TestSummary.json'), $json, ([Text.UTF8Encoding]::new($false)))
    $lines = @('BookStitch LocalizationExporter - Smoke-Test', "Ergebnis: $($summary.outcome)", 'PRODUKTIVE RESX: NICHT VERAENDERT', '', 'Einzelschritte:')
    foreach($r in $results) { $lines += ('{0}: {1}; Exit {2}; Fehler {3}; Warnungen {4}; Log {5}' -f $(if($r.passed){'OK'}else{'FEHLER'}),$r.step,$r.exitCode,$r.errors,$r.warnings,$r.log) }
    $lines += ''; $lines += 'WARNUNGEN SIND OFFENE BEFUNDE, NICHT AUTOMATISCH TESTFEHLER.'
    $lines += 'Release exportiert auch veraltete Baselines und meldet diese als offene Warnungen.'
    $lines += "Details: $logDir"
    [IO.File]::WriteAllLines((Join-Path $logDir 'TestSummary.txt'), $lines, ([Text.UTF8Encoding]::new($false)))
    if ($success -and (Test-Path -LiteralPath $temp)) {
        $workRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'work')).TrimEnd('\') + '\'
        $resolvedTemp = [IO.Path]::GetFullPath($temp)
        if (-not $resolvedTemp.StartsWith($workRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force -ErrorAction Stop
    } elseif (Test-Path -LiteralPath $temp) { Write-Host "Testdaten fuer Fehlerdiagnose erhalten: $temp" }
    Write-Host "`n=================================================="
    if($success) { Write-Host 'TEST BESTANDEN - Exporter-Verhalten technisch plausibel.' -ForegroundColor Green }
    else { Write-Host 'TEST NICHT BESTANDEN - Produktivexport vorerst NICHT starten.' -ForegroundColor Red }
    Write-Host 'Produktive RESX und Masterdatei wurden nicht veraendert.'
    Write-Host "Bericht: $(Join-Path $logDir 'TestSummary.txt')"
    Write-Host '=================================================='
}
if (-not $success) { exit 1 }
exit 0

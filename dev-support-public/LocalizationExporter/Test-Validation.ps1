# Isolated regression fixtures; productive resources and Master remain untouched.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$original = Get-Content (Join-Path $root 'BookStitch/Resources/BookStitch.LanguageMaster.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$work = Join-Path $PSScriptRoot ('work/v-' + [guid]::NewGuid().ToString('N').Substring(0,16))
if (Test-Path -LiteralPath $work) { throw 'Test workspace already exists' }
[IO.Directory]::CreateDirectory($work) | Out-Null
$master = Join-Path $work 'BookStitch.LanguageMaster.json'
$dll = Join-Path $PSScriptRoot 'bin/Release/net10.0/LocalizationExporter.dll'
$report = Join-Path $work 'reports/BookStitch.LocalizationValidation.json'
$utf8 = [Text.UTF8Encoding]::new($false)
$productiveCopies = @{}
foreach ($extension in @('json', 'txt')) {
    $path = Join-Path $root "BookStitch/Resources/BookStitch.LocalizationValidation.$extension"
    $productiveCopies[$path] = if (Test-Path -LiteralPath $path) { (Get-FileHash $path).Hash } else { $null }
}

function Save-Json($Value, $Path) { [IO.File]::WriteAllText($Path, (ConvertTo-Json -InputObject $Value -Depth 100), $utf8) }
function Run-Case([string]$Mode, [bool]$Success, [string]$Code = '', [string]$Accept = '') {
    $cli = @($dll, '--mode', $Mode, '--master', $master, '--resources', $work, '--state-directory', $work)
    if ($Accept) { $cli += @('--accept-file', $Accept) }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & dotnet @cli 2>&1; $codeValue = $LASTEXITCODE }
    finally { $ErrorActionPreference = $previous }
    if (($codeValue -eq 0) -ne $Success) { throw "Unexpected result: $Mode`n$output" }
    if ($Code) {
        $diagnostics = (Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json).diagnostics
        if (-not ($diagnostics | Where-Object Code -eq $Code)) { throw "Missing diagnostic $Code" }
    }
    Write-Host "PASS $Mode expectedSuccess=$Success $Code"
}
try {
& dotnet build (Join-Path $PSScriptRoot 'LocalizationExporter.csproj') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
$text = '{{literal}} {0:N2}'
$sha = [Security.Cryptography.SHA256]::Create()
try { $hash = ([BitConverter]::ToString($sha.ComputeHash($utf8.GetBytes($text)))).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
$fixture = @{
    sourceLocale='de'
    locales=@(@{locale='de';fileName='LocalizationResources.de.resx'}, @{locale='en';fileName='LocalizationResources.en.resx'})
    resxLayout=@{xmlDeclaration=$original.resxLayout.xmlDeclaration;rootElement='root';dataElement='data';headersInsertedBeforeDataIndex=0;resheaders=$original.resxLayout.resheaders;originalPerLocaleOrderOverrides=@{};originalWhitespaceProfiles=@{}}
    entries=@{Fixture=@{source=@{value=$text;comment='Context: isolated regression fixture'};translations=@{en=@{value=$text;comment=("SourceHash: $hash`nLock: language")}}}}
}
Save-Json $fixture $master
Run-Case 'initializebaselines' $true
if (@(Get-ChildItem -LiteralPath (Join-Path $work 'backups') -File -Filter '*.json').Count -ne 1) {
    throw 'Initialization must retain its test backup in the isolated workspace'
}
Run-Case 'release' $true
Run-Case 'verify' $true
$baseline = [IO.File]::ReadAllText($master)
# Canonical vectors independently calculated with Python json.dumps as documented.
# Include NBSP, non-BMP, combining marks, Unicode separators and JSON escapes.
$data = $baseline | ConvertFrom-Json
$data.entries.Fixture.source.value = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('R3LDtsOfZcKg8J+YgA=='))
$data.entries.Fixture.source.comment = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('Q29udGV4dDogZcyBIC8gw6nigKjigKk='))
$data.entries.Fixture.translations.en.value = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('wqsgQm9uam91csKgISDCuw=='))
$data.entries.Fixture | Add-Member -MemberType NoteProperty -Name reviews -Value @{en=@{status='ok';fingerprint='db054ca0497fe256871ffcebf55d909b7e311183a28664d8154fd1c751185df4'}}
Save-Json $data $master
Run-Case 'development' $true
$audit = Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json
if ($audit.diagnostics | Where-Object Code -eq 'REVIEW_STALE') { throw 'Canonical Unicode fingerprint rejected' }
$data.entries.Fixture.translations.en.value += ' changed'
Save-Json $data $master
Run-Case 'development' $true 'REVIEW_STALE'
$data = $baseline | ConvertFrom-Json
$data.entries.Fixture.source.value = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('IlwvCg0J'))
$data.entries.Fixture.source.comment = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('Q29udGV4dDog5pel5pys6Kqe'))
$data.entries.Fixture.translations.en.value = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('2KfZhNi52LHYqNmK2Kk='))
$data.entries.Fixture | Add-Member -MemberType NoteProperty -Name reviews -Value @{en=@{status='ok';fingerprint='aea2e9eafd07e7ea35a1165537e0880cf48b968bfe0c2ea453154286a0a95ae3'}}
Save-Json $data $master
Run-Case 'development' $true
$audit = Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json
if ($audit.diagnostics | Where-Object Code -eq 'REVIEW_STALE') { throw 'Canonical Unicode fingerprint rejected' }
$data.entries.Fixture.translations.en.value += ' changed'
Save-Json $data $master
Run-Case 'development' $true 'REVIEW_STALE'
[IO.File]::WriteAllText($master, $baseline, $utf8)
Run-Case 'release' $true
$manifest = Join-Path $work 'BookStitch.LocalizationExport.json'
$resource = Join-Path $work 'LocalizationResources.en.resx'
$manifestHash = (Get-FileHash $manifest).Hash
$resourceHash = (Get-FileHash $resource).Hash
foreach ($case in @(
    @{value='Broken {0:N2';code='INVALID_COMPOSITE_FORMAT'},
    @{value='Changed {0}';code='PLACEHOLDER_MISMATCH'},
    @{value='{{literal}} {{0:N2}}';code='PLACEHOLDER_MISMATCH'}
)) {
    $data = $baseline | ConvertFrom-Json
    $data.entries.Fixture.translations.en.value = $case.value
    Save-Json $data $master
    Run-Case 'development' $false $case.code
    if ((Get-FileHash $manifest).Hash -ne $manifestHash -or (Get-FileHash $resource).Hash -ne $resourceHash) { throw 'Failed export changed output' }
}
$data = $baseline | ConvertFrom-Json
$data.entries.Fixture.translations.en.value = 'Changed {0:N2}'
Save-Json $data $master
$lockedHash = (Get-FileHash $master).Hash
Run-Case 'development' $true 'LOCK_BASELINE_CONFLICT'
Run-Case 'release' $true 'LOCK_BASELINE_CONFLICT'
Run-Case 'verify' $true 'LOCK_BASELINE_CONFLICT'
if ((Get-FileHash $master).Hash -ne $lockedHash) { throw 'Export modified locked Master' }
[xml]$resx = Get-Content $resource -Raw -Encoding UTF8
if ($resx.root.data.value -ne 'Changed {0:N2}') { throw 'Locked value was silently corrected' }
$accept = Join-Path $work 'accept.json'
Save-Json @(@{key='Fixture';locale='en';note='Explicitly reviewed correction for isolated regression'}) $accept
Run-Case 'accepttranslations' $false '' $accept
Save-Json @(@{key='Fixture';locale='en';note='Too short';overrideLock=$true}) $accept
Run-Case 'accepttranslations' $false '' $accept
Save-Json @(@{key='Fixture';locale='en';note='Explicitly reviewed correction for isolated regression';overrideLock=$true}) $accept
Run-Case 'accepttranslations' $true '' $accept
$accepted = Get-Content $master -Raw -Encoding UTF8 | ConvertFrom-Json
if (@($accepted.entries.Fixture.lockOverrides).Count -ne 1 -or -not $accepted.entries.Fixture.lockOverrides[0].previousBase) { throw 'Override history missing' }
Run-Case 'release' $true
$accepted.entries.Fixture.source.comment += ' changed context'
Save-Json $accepted $master
Run-Case 'development' $true 'LOCK_BASELINE_CONFLICT'
$global = $baseline | ConvertFrom-Json
$global.entries.Fixture.source.comment += "`nLock: global"
$global.entries.Fixture.PSObject.Properties.Remove('translationBase')
Save-Json $global $master
Run-Case 'initializebaselines' $true
$global = Get-Content $master -Raw -Encoding UTF8 | ConvertFrom-Json
$global.entries.Fixture.translations.en.comment = "SourceHash: $hash"
$global.entries.Fixture.source.value = 'Changed source {0:N2}'
Save-Json $global $master
Run-Case 'development' $true 'LOCK_BASELINE_CONFLICT'
foreach ($pair in @(
    @{de='{{{0:N2}}}';en='{{{0:N2}}}'},
    @{de='{0,8:N2} {1} {0,8:N2}';en='{1} {0,8:N2} {0,8:N2}'},
    @{de='{{literal}}';en='{{escaped}}'}
)) {
    $data = $baseline | ConvertFrom-Json
    $data.entries.Fixture.source.value = $pair.de
    $data.entries.Fixture.translations.en.value = $pair.en
    $data.entries.Fixture.translations.en.comment = "SourceHash: $hash"
    Save-Json $data $master
    Run-Case 'development' $true
}
$data.entries.Fixture.source.value = 'Invalid source {'
Save-Json $data $master
Run-Case 'development' $false 'INVALID_COMPOSITE_FORMAT'
$rules = $baseline | ConvertFrom-Json
$rules.entries.Fixture.source.comment += "`nTranslationRule: KeepOriginal"
$rules.entries.Fixture.translations.en.value = 'Different {0:N2}'
Save-Json $rules $master
Run-Case 'release' $true 'KEEP_ORIGINAL_VIOLATION'
[xml]$ruleXml = Get-Content $resource -Raw -Encoding UTF8
if ($ruleXml.root.data.value -cne 'Different {0:N2}') { throw 'KeepOriginal warning silently rewrote target' }
$rules.entries.Fixture.source.comment = 'Context: fixture' + "`nTranslationRule: GermanEnglish"
$rules.locales += [pscustomobject]@{locale='fr';fileName='LocalizationResources.fr.resx'}
$rules.entries.Fixture.translations | Add-Member -MemberType NoteProperty -Name fr -Value ([pscustomobject]@{value='French {0:N2}';comment="SourceHash: $hash"})
Save-Json $rules $master
Run-Case 'release' $true 'GERMAN_ENGLISH_VIOLATION'
$rules.entries.Fixture.source.comment = 'Context: fixture'
$rules.entries.Fixture | Add-Member -MemberType NoteProperty -Name reviews -Value @{en=@{status='needs_correction';releaseBlocking=$true}}
Save-Json $rules $master
Run-Case 'release' $true 'BLOCKING_REVIEW'
# Stable regression for the original seven lock and two UI-fit conflicts. Do not
# require the productive Master to retain historical problems after they are resolved.
$nine = @{
    sourceLocale='de'; locales=@(@{locale='de';fileName='LocalizationResources.de.resx'})
    resxLayout=$fixture.resxLayout; entries=@{}
}
$nineLocales = @('en','es','fr','it','nl','fi','ru')
foreach ($locale in $nineLocales) { $nine.locales += @{locale=$locale;fileName="LocalizationResources.$locale.resx"} }
foreach ($key in @('UiAutoMerge','OriginalTagTitle','OriginalFileName')) {
    $targets = @{}
    foreach ($locale in $nineLocales) {
        $comment = "SourceHash: $hash"
        if ($key -eq 'UiAutoMerge') { $comment += "`nLock: language" }
        $targets[$locale] = @{value=$text;comment=$comment}
    }
    $nine.entries[$key] = @{source=@{value=$text;comment='Context: isolated nine-conflict fixture'};translations=$targets}
}
Save-Json $nine $master
Run-Case 'initializebaselines' $true
$nine = Get-Content $master -Raw -Encoding UTF8 | ConvertFrom-Json
$nine.entries.UiAutoMerge.source.comment += ' changed context'
$nine.entries.OriginalTagTitle | Add-Member -MemberType NoteProperty -Name uiFit -Value @{fi=@{status='pending_visual_verification';candidateFingerprint='historical'}}
$nine.entries.OriginalFileName | Add-Member -MemberType NoteProperty -Name uiFit -Value @{nl=@{status='pending_visual_verification';candidateFingerprint='historical'}}
Save-Json $nine $master
$nineHash = (Get-FileHash $master).Hash
foreach ($mode in @('development','release','verify')) {
    Run-Case $mode $true
    $audit = Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json
    $locks = @($audit.diagnostics | Where-Object { $_.Code -eq 'LOCK_BASELINE_CONFLICT' -and $_.Key -eq 'UiAutoMerge' -and $_.Severity -eq 'warning' })
    $fit = @($audit.diagnostics | Where-Object { $_.Code -eq 'UI_FIT_GUARD_VIOLATION' -and $_.Severity -eq 'warning' })
    if ($locks.Count -ne 7 -or $fit.Count -ne 2) { throw 'Expected nine isolated quality findings missing' }
    if ((Get-FileHash $master).Hash -ne $nineHash) { throw 'Nine-conflict export modified Master' }
    if (-not (Select-String -LiteralPath (Join-Path $work 'reports/BookStitch.LocalizationValidation.txt') -SimpleMatch 'LOCK_BASELINE_CONFLICT')) { throw 'Text report lost quality findings' }
}
$productiveMaster = Join-Path $root 'BookStitch/Resources/BookStitch.LanguageMaster.json'
$master = Join-Path $work 'CurrentMaster.json'
Copy-Item -LiteralPath $productiveMaster -Destination $master
$originalHash = (Get-FileHash $master).Hash
foreach ($mode in @('development', 'release', 'verify')) {
    Run-Case $mode $true
    $audit = Get-Content $report -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($audit.summary.errors -ne 0 -or $audit.summary.files -ne $original.locales.Count) { throw 'Current Master export failed' }
    if ((Get-FileHash $master).Hash -ne $originalHash) { throw 'Current Master changed during export' }
}
foreach ($locale in $original.locales) {
    [xml]$xml = Get-Content (Join-Path $work $locale.fileName) -Raw -Encoding UTF8
    foreach ($node in $xml.root.data) {
        $entry = $original.entries.PSObject.Properties[$node.name].Value
        $expected = if ($locale.locale -eq 'de') { $entry.source.value } else { $entry.translations.PSObject.Properties[$locale.locale].Value.value }
        if ([string]::IsNullOrWhiteSpace($expected)) {
            # Missing targets intentionally export the documented development fallback.
            $expected = if ($entry.source.comment -match 'TranslationRule:\s*GermanEnglish') { $entry.translations.en.value } else { $entry.source.value }
            if ($node.comment -notmatch 'DevelopmentFallback:') { throw "Missing fallback marker: $($node.name)/$($locale.locale)" }
        }
        if ($node.value -cne $expected) { throw "Export changed stored text: $($node.name)/$($locale.locale)" }
    }
}
$manifestHash = (Get-FileHash $manifest).Hash
[IO.File]::WriteAllText($master, '{broken json', $utf8)
Run-Case 'development' $false 'EXPORT_FAILURE'
if ((Get-FileHash $manifest).Hash -ne $manifestHash) { throw 'Invalid JSON changed manifest' }
foreach ($path in $productiveCopies.Keys) {
    $current = if (Test-Path -LiteralPath $path) { (Get-FileHash $path).Hash } else { $null }
    if ($current -ne $productiveCopies[$path]) { throw 'Isolated test modified productive report copies' }
}
# Relocate the runtime so default state also stays inside this isolated workspace.
# Omitting --state-directory exercises the productive report-copy path safely.
$runtime = Join-Path $work 'runtime/bin/Release/net10.0'
[IO.Directory]::CreateDirectory($runtime) | Out-Null
Copy-Item -Path (Join-Path $PSScriptRoot 'bin/Release/net10.0/*') -Destination $runtime
$copyResources = Join-Path $work 'copy-resources'
$copyMaster = Join-Path $work 'copy-master.json'
[IO.File]::WriteAllText($copyMaster, $baseline, $utf8)
foreach ($mode in @('development', 'release', 'verify', 'verify', 'development')) {
    $expectFailure = $mode -eq 'verify' -and (Test-Path -LiteralPath (Join-Path $copyResources 'tampered'))
    if ($mode -eq 'development' -and (Test-Path -LiteralPath $copyResources)) {
        [IO.File]::WriteAllText($copyMaster, '{broken json', $utf8)
        $expectFailure = $true
    }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & dotnet (Join-Path $runtime 'LocalizationExporter.dll') --mode $mode --master $copyMaster --resources $copyResources 2>&1
        $exitCode = $LASTEXITCODE
    } finally { $ErrorActionPreference = $previous }
    if (($exitCode -ne 0) -ne $expectFailure) { throw "Report-copy case failed: $mode $output" }
    foreach ($extension in @('json', 'txt')) {
        $name = "BookStitch.LocalizationValidation.$extension"
        if ((Get-FileHash (Join-Path $copyResources $name)).Hash -ne (Get-FileHash (Join-Path $work "runtime/reports/$name")).Hash) {
            throw 'Resource report is not the latest tool report'
        }
    }
    $audit = Get-Content (Join-Path $copyResources 'BookStitch.LocalizationValidation.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ($audit.mode -ne $mode -or $audit.usableForMode -eq $expectFailure -or (($audit.result -eq 'failed') -ne $expectFailure)) {
        throw 'Copied report lost mode or failure result'
    }
    if ($mode -eq 'verify') {
        [IO.File]::AppendAllText((Join-Path $copyResources 'LocalizationResources.en.resx'), 'drift', $utf8)
        [IO.File]::WriteAllText((Join-Path $copyResources 'tampered'), '', $utf8)
    }
}
Write-Host 'PASS report copies: export, verify, replacement and failed validation'
$workRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'work')).TrimEnd('\') + '\'
$resolvedWork = [IO.Path]::GetFullPath($work)
if (-not $resolvedWork.StartsWith($workRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path' }
Remove-Item -LiteralPath $resolvedWork -Recurse -Force -ErrorAction Stop
Write-Host 'ALL ISOLATED VALIDATION TESTS PASSED. Temporary fixtures, test backups and reports removed.'
} catch {
    Write-Host "Testdaten fuer Fehlerdiagnose erhalten: $work"
    throw
}

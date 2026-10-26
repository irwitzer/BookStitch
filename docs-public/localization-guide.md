# Localization contributor guide

BookStitch uses **one editorial source** for UI text: [`BookStitch.LanguageMaster.json`](../BookStitch/Resources/BookStitch.LanguageMaster.json). The generated `LocalizationResources.<locale>.resx` files are build artifacts, **not** translation sources. German (`source.value` and `source.comment`) is authoritative; English is the reference translation and neutral runtime fallback.

Read the [binding localization rules](../BookStitch/Resources/LOCALIZATION_RULES.md) before editing. They govern baselines, locks, review evidence, placeholders, UI-fit constraints and release acceptance.

## Files and tools

| Repository path | Purpose |
| --- | --- |
| `BookStitch/Resources/BookStitch.LanguageMaster.json` | Source text, translations and review metadata |
| `BookStitch/Resources/LOCALIZATION_RULES.md` | Normative editing and review rules |
| `BookStitch/Resources/LocalizationResources.<locale>.resx` | Exporter-generated resources |
| `BookStitch/Resources/BookStitch.LocalizationExport.json` | Generated export manifest |
| `dev-support-public/LocalizationExporter/` | Public exporter and validation scripts |

The exporter is a **development tool** and is not shipped with the installed application. Generated backups, reports, `work/`, `bin/` and `obj/` are not public source deliverables.

## Edit or translate strings

1. Find the stable key under `entries` in the Language Master. For a new UI key, provide German `source.value`, a contextual German `source.comment`, and `translations.en.value`. Ordinary feature development does not automatically retranslate other languages.
2. Treat the exact German value **and comment** as the semantic source. Record UI location, action meaning, placeholders, fixed terminology and narrow-control constraints where relevant. Do not rename existing keys as incidental cleanup.
3. For target-language changes, edit only `entries[KEY].translations[LOCALE].value` and any **justified** associated review metadata. Preserve placeholders, punctuation semantics, identifiers, existing translations and language-specific notes.
4. Inspect `TranslationRule: KeepOriginal`, `TranslationRule: GermanEnglish`, `Lock: global`, `Lock: language`, `TranslationNote: CompactUI`, `Length: critical` and pending `uiFit` records before changing protected text. Obtain explicit authorization for locked corrections; do not overwrite protected values automatically.
5. A changed source value **or developer comment** may invalidate translation baselines and reviews. Do not refresh hashes, `translationBase` or `reviews` merely to suppress warnings; never assert native-language or screenshot approval without evidence.

For a new UI language, register its locale in the Master **and** the application's language selection service (`UiLanguageService.cs`, and `UiLanguageIds.cs` if needed), then verify WPF resource inclusion and fallback behavior. A new RESX alone does not make a language selectable. Locale identifiers may differ between the UI and resource suffix.

## Export and verify on Windows

Requires the .NET 10 SDK and Windows PowerShell. From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\dev-support-public\LocalizationExporter\Export-Resx.ps1 -Mode Development
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\dev-support-public\LocalizationExporter\Export-Resx.ps1 -Mode Verify
```

`Development` generates resources; `Verify` compares the existing generated files and manifest with the Master without rewriting them. `Release` is also a technical export mode, **not** linguistic approval. Both export modes retain existing translations and report stale baselines, locks, UI-fit issues and rule conflicts as warnings. Invalid source JSON/XML, missing required German/English values and invalid format placeholders are technical errors.

When resource bytes change, review and commit the Language Master, generated RESX files and export manifest together. Do not hand-edit generated resources or the manifest. The validation report is diagnostic, not a release certificate. Run the exporter's `Test-Exporter.ps1` or `Test-Validation.ps1` for isolated checks; test reports are not release artifacts.

### Protected corrections

After an explicit, reviewed authorization, a narrowly scoped locked correction can be accepted using `AcceptTranslations -AcceptFile <path>` with an array such as:

```json
[{"key":"Example.Key","locale":"en","note":"Authorized correction after a documented review.","overrideLock":true}]
```

The note must explain the change (at least 20 characters); the lock remains and override history is preserved. This **does not** override placeholder or translation rules, pending UI-fit protection or linguistic approval. See the normative rules for the full contract.

## Genre catalogue

`Genre.Tag.*` and `Genre.Category.*` entries in the Language Master localize the shared genre choices. The catalogue shows library tags before categories, sorts enabled groups using the effective list culture and can optionally prefix five unchanged private tags. Existing user, imported, project and exported genre values remain **free text**; changing the UI language does not translate stored metadata. The English-only list preference affects display culture, not exported values.

## Contribution checklist

State the affected keys and locales, relevant terminology decisions, unresolved validation warnings and the exact checks performed. For narrow layouts, include WPF evidence where feasible. Export verification, translation review and visual testing are **separate** forms of evidence.

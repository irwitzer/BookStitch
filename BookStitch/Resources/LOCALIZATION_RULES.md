# BookStitch localization rules

**Applies to:** all localized UI resources under `BookStitch/Resources/`, including changes proposed by contributors or automated tools. **These rules take precedence** over the shorter [contributor guide](../../docs-public/localization-guide.md).

## 1. Single source of truth

- **Edit only `BookStitch.LanguageMaster.json`.** `LocalizationResources.<locale>.resx` files and `BookStitch.LocalizationExport.json` are generated; never edit them manually. `BookStitch.LocalizationValidation.json` and `.txt` are generated, ignored diagnostics, not approval records.
- German `entries[key].source.value` **and** `source.comment` define meaning; `translations.en.value` is the English reference and neutral WPF fallback. The operating-system culture determines the initial UI language. English fallback and German editorial ownership are intentionally different.
- Keep resource keys stable. Preserve `translations`, `translationBase`, `reviews`, `uiFit`, `reviewTracking`, `uiFitTracking`, `auditImports`, `locales`, `resxLayout`, comments and historic decisions. Never overwrite unrelated records to simplify a patch.
- Git tracks the stable Master filename and its history. Do not introduce a second active Master or independently maintained translation files. Keep paths repository-relative; do not commit local machine paths or generated tool reports.

## 2. Development and translation workflow

During normal feature development, maintain **German source text, English reference and German developer context**; do not bulk-retranslate other locales. A new key needs a stable name, accurate German text, matching English and a useful German comment. For modified source wording or semantics, check English and flag existing target languages for reassessment. Changing only `source.comment` can also make translations stale.

Developer comments should identify `Context`, `Meaning`, any `Placeholders`, fixed terms and narrow UI constraints (`TranslationNote: CompactUI`, `Length: critical`). Preserve existing context and decisions. Translate the actual behavior, not just a resource-key name: distinguish cancellation, discard, deletion, recovery and rebuild, and preserve all user instructions.

Before a release, review missing, changed and stale values for all supported locales, **including `gsw-CH` unless an exception is explicitly documented**. Respect the original German meaning, established terminology, UI width and previously approved translations. Register new UI languages in the application language service; a generated RESX alone is insufficient. Native-language review and WPF fit checks must be reported separately, with their limitations.

## 3. Baselines, fingerprints and reviews

**Translation currency and linguistic approval are different facts.** Neither an accepted baseline nor a clean export proves that a translation was reviewed.

- Historic `SourceHash` is SHA-256 of the exact UTF-8 German `source.value`, without trimming or Unicode normalization; it **does not** include the source comment. Never silently rewrite it.
- `translationBase` binds a target translation to the exact German source **value and comment** last assessed. A source change makes the target reviewable again. `InitializeBaselines` is a one-time migration, **not** translation approval; inspect the current Master before ever considering it and never repeat it as warning cleanup.
- `reviews[locale].fingerprint` hashes the compact UTF-8 JSON array `[source.value, source.comment, target.value]`, with unescaped non-ASCII characters, no extra whitespace and standard JSON escapes. For German, `target.value` is the German source value. Do not renew stale fingerprints without a real review; preserve historical evidence.
- Only the review statuses `ok`, `needs_correction` and `corrected` exist. `corrected` requires both an actual change **and subsequent review**. Missing review means *not reviewed*. Review notes, dates and earlier audit records remain intact.
- A `uiFit` check is distinct from linguistic review. An unresolved or pending visual candidate must not be described as screenshot-approved.

## 4. Translation protections

| Marker | Requirement |
| --- | --- |
| `TranslationRule: KeepOriginal` | Every locale uses the German source value unchanged. |
| `TranslationRule: GermanEnglish` | German uses the source value; every other locale uses **exactly** the English reference. |
| `Lock: global` | Protect all values for that key from automatic change. |
| `Lock: language` | Protect the marked locale's value; keep `LockReason`. |
| `TranslationNote: CompactUI` and `Length: critical` | Keep the narrowest actual control readable; do not replace approved compact wording automatically. |
| `uiFit[locale]` pending evidence | Preserve protected candidate and evidence; do not claim visual approval. |

A lock does **not** make stale content current. If a rule, lock or UI-fit guard conflicts with a proposed edit, **stop and report it**, rather than forcing export or silently changing values. Before modifying protected Master content, present the existing wording, proposed wording and protection reason to the requester and obtain authorization. For approved UI-fit changes, retain dated evidence and test the relevant controls before adding a language lock; keep any explicitly approved, documented exceptions.

`AcceptTranslations -AcceptFile <path>` accepts specifically reviewed key/locale baselines. For an explicitly approved locked correction, the file may include `"overrideLock": true` plus a meaningful `"note"` of at least 20 characters. Preserve the lock and `entries[key].lockOverrides` history. This does not bypass translation rules, placeholder validation or pending/obsolete UI-fit checks, and does not itself certify linguistic review. Never perform blanket baseline acceptance after a source change.

## 5. Export and validation

Use the exporter appropriate to the **repository context**. The two tool copies have different responsibilities; do not treat them as interchangeable working directories:

- **Private development repository:** use `dev-support/project-tools/LocalizationExporter/` for normal development, exports, validation and tests. Its `backups/`, `reports/`, `work/`, `bin/` and `obj/` are local/generated working areas and must not be carried into the public repository.
- **Public repository:** use `dev-support-public/LocalizationExporter/`, the distributable exporter implementation provided for contributors.
- **When both directories exist:** the private exporter is the default for internal work. The public copy is a **publication artifact**, not the internal working exporter. Do not run routine internal exports/tests there or create local reports, backups, `work/`, `bin/` or `obj/` as part of preparing the public copy. If the private exporter is unavailable, do not silently substitute the public copy; report the discrepancy.
- **Output isolation:** when running isolated tests or otherwise generating exporter state, configure the supported `-StateDirectory` to point to an appropriate ignored location outside `dev-support-public/`. Keep generated RESX/manifest only at their intended project resource paths. Do not assume that documentation or Git ignore rules alone prevent generated files.

The active exporter's `Export-Resx.ps1` supports `Development`, `Release`, `Verify`, `InitializeBaselines` and `AcceptTranslations`; tool checks include `Test-Exporter.ps1` and `Test-Validation.ps1` where present. Verify the actual available scripts and options in the selected copy before execution.

- **Development / Release:** generate technically valid RESX from the Master, preserving existing values. Both report stale translations, protection conflicts and review/UI-fit issues as **quality warnings**; neither provides linguistic or visual release approval. Missing target values may use a documented temporary fallback, never an invented approved translation.
- **Verify:** validate existing RESX and manifest against the Master, without rewriting either. Diagnostic reports may be refreshed.
- **InitializeBaselines / AcceptTranslations:** explicitly requested Master mutations with their own safeguards; not ordinary export modes.
- Preserve stable RESX names, culture mappings, entries, comments, .NET resource headers, `resxLayout`, WPF build integration and deterministic output. Parse full .NET composite-format placeholders, including indices, alignment, format specifiers, repeats and escaped braces. Reordering complete placeholders may be valid; losing or changing them is not.
- Invalid JSON/XML, absent required German/English values, broken placeholders or failed resource generation are **technical failures**; a failed export must not leave a misleading successful manifest or silently destroy valid resources.
- On a successful export, review and commit the Master, generated RESX and `BookStitch.LocalizationExport.json` consistently. `Verify` should then succeed for the same inputs and exporter version. Retain Git byte preservation for hashed files (`.gitattributes`, `-text` where configured).

`BookStitch.LocalizationExport.json` records export time/mode, Master and export-input hashes, exporter version, generated resource files and their hashes, and validation results. Export timestamps are evidence, not a substitute for content matching. `BookStitch.LocalizationValidation.*` and exporter-generated `reports/`, `work/`, `backups/`, `bin/` and `obj/` directories are local diagnostics/build state, not translation sources or publication content. Keep these artifacts out of the public copy and out of public transfers; `-StateDirectory` isolates tool state for supported test workflows.

The exporter separates technical success from language quality: even when `usableForMode` is true, `releaseReady` is false and `releaseReadiness` is `not-assessed-by-exporter`. Preserve unresolved warning details and review-history evidence. Report real QA results, never manufacture approvals.

## 6. Review before publication

Verify Master structure and stable keys; source and translation completeness; punctuation, placeholders, terminology and fixed identifiers; baseline staleness; locks and pending UI-fit records; RESX/manifest consistency; registration and runtime fallback for each language. Inspect narrow windows, non-Latin fonts, long text, menus, warnings and accessibility labels in actual WPF where available. Do not equate automated validation with native-language certification or full visual sign-off.

Never translate resource keys, persistent IDs, machine-readable tag names, technical constants or format placeholders. Keep approved names and examples as documented in developer comments. Do not claim tests, approvals or guarantees that have not actually occurred.

Changes to these rules or the exporter contract require aligned updates to the Master schema/metadata, exporter, tests and the [contributor guide](../../docs-public/localization-guide.md). No competing localization policy document should be maintained.

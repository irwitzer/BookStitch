# BookStitch – Changelog (English)

BookStitch for Windows version history, newest release first.

## [2.1.0] – October 2026

### Added

#### Cover import and preparation
- Drag images directly from supported browsers onto the cover area in the main window or metadata editor, when image data or an accessible image URL is available.
- Covers with a shortest edge of at least 200 pixels are accepted. Automatic square output uses 1000, 2000 or 3000 pixels, with adaptive JPEG quality.
- Choose Automatic, White or Black for transparent cover backgrounds in global metadata settings. The choice applies to newly imported covers.
- Bundled WebP decoding works without an installed Windows WebP extension. Cover processing also handles image orientation and color profiles; ImageSharp is no longer required.

## [2.0.0] – October 2026

### Added

#### Architecture and engineering
- Substantial software architecture overhaul: more modular code, clearer separation of UI, project state, media analysis, export and processing workflows, with centralized lifecycle and error handling.
- Windows test suite extensively reorganized and rebuilt with stronger focused regression coverage for UI, metadata, chapters, disc import, resumption and export.

#### Formats, covers and export
- Local audio support for M4A, M4B, MP3, WAV, FLAC, OGG, AIFF and legacy WMA (subject to the installed FFmpeg capabilities).
- AAC-based M4A or M4B output, selectable mono/stereo presets up to 256 kbps, and configurable parallel processing.
- JPEG, PNG and WebP cover import, cover preview, and improved cover preparation and embedding.
- Optional automatic merge or manual review before merging; reuse of prepared audio when compatible with the selected preset.
- New exports write QuickTime chapter markers. Existing Nero chapter structures in previously created files are preserved during supported editing paths; **new exports do not write both chapter formats simultaneously**.
- More precise chapter timing through improved duration arithmetic and AAC padding handling; exact tolerances depend on source and container timing resolution.

#### Track management and projects
- Automatic and manual track ordering, reorder, exclude and restore actions, improved technical track-number recognition, and duplicate/gap warnings.
- Embedded chapter hierarchy and start-time display within the track grid.
- Improved project resumption and re-export, including safe rollback of interrupted source additions, reconversions and merge attempts.


#### Metadata and tagging
- Comprehensive metadata editor with drafts, confirmed values and candidate suggestions.
- Clear distinction between values extracted from source media, automatically suggested values and manually entered metadata.
- Multi-value editing, ordering and output controls for metadata fields that support multiple entries.
- Autocomplete, field search, source evidence and source-occurrence information.
- Expanded audiobook fields, including structured series/part information and original title.
- Localized catalogue of 74 genres, with configurable catalogue and personal-genre preferences.
- Player interoperability options: compatible information in Description; optional Narrator-to-Composer and Title-to-Album mappings when enabled.

#### Chapters and track organization
- Substantially expanded automatic chapter analysis and candidate normalization.
- Multilingual chapter markers and number-word recognition with conservative removal of technical numbering.
- Enhanced chapter previews and editing, including embedded chapter children and timeline start positions.
- Chapter modes including explicit no-chapters output.

#### Audio and performance
- Parallel FFprobe analysis to accelerate initial inspection of large multi-track projects.
- More rigorous AAC pass-through checks for codec, channels, audio streams, bitrate/preset and sample rate.
- Project-specific sample-rate decisions and configurable target sample-rate preferences.
- Original source bitrate, sample rate and mono/stereo information shown in the track grid.
- Per-track and overall progress indicators.

#### CD and project features
- **Drive Circle is an integrated, standard feature**, coordinating up to five configured optical drives, including drive order and disc changes.
- Improved disc recognition, selected-drive persistence, resumption and duplicate-disc protection.
- Project integrity inspection, guarded repair/rebuild from intact original data, versioned project layouts and migration support.
- Extended project retention, cleanup and management options.

#### User interface and languages
- UI support for **21 language variants**, including Bernese Swiss German, Japanese and both Chinese variants.
- Reworked main window, project setup, metadata editing, project browser and settings.
- New interactive FAQ/help section.
- More accessible UI labels and multilingual metadata search, including Unicode and CJK input.

### Changed
- Significantly improved extraction, aggregation and selection of metadata from local files and disc projects, with user-confirmed values taking precedence.
- More consistent source provenance, metadata persistence, cover selection, suppression and output-field policies.
- Better automatic track ordering, chapter proposals, embedded-chapter handling and chapter timestamp calculations.
- Better reuse of eligible existing AAC audio, safer treatment of mixed sample rates and prepared caches.
- Faster, more responsive analysis and track-grid handling for large projects; clearer errors and review dialogs.
- More reliable pausing, stopping, saving, resuming and re-exporting, including multi-disc and merge operations.
- Guarded rollback for adding sources, reconversion and interrupted merge/export attempts.
- More responsive optical-drive refresh and safer switching between projects.

### Fixed
- Cumulative chapter drift related to duration rounding and AAC padding.
- Technical chapter-number transitions such as 099/100 and 999/1000 without destroying meaningful numeric titles.
- Audio-CD raw-sector addressing that could truncate beginnings or corrupt last-track extraction.
- Several MP3-disc identity, duplicate-import, resume and failure-recovery cases.
- Final-output conflict and cancelled-export recovery, preserving pre-existing output and source data.
- Metadata dropdown, candidate selection, Unicode search and field-persistence issues.
- Maximized-window bounds, UI states and FFmpeg progress callback handling.

### Verification
- Real-media checks covered large track sets, multi-disc work, decoding, chapter positioning and unchanged originals. These are reported test results, not a guarantee for every hardware configuration.

### Unchanged principles
- Local processing; no account required for audiobook creation.
- Source audio is neither renamed nor overwritten.
- AAC-based M4A and M4B export.

## [1.0.0] – 26 July 2026

### Initial public release
- C# / WPF Windows desktop application for joining audio tracks into complete audiobooks.
- Import of local audio folders, MP3/data CDs and audio CDs, including multi-disc projects.
- Track display, manual reordering and exclusion of unwanted tracks.
- FFmpeg/FFprobe analysis and AAC conversion; M4A/M4B export and selectable encoding presets.
- Initial audiobook metadata editing and output for fields such as title, author, narrator and genre, plus cover artwork.
- Chapter suggestions based on source information and embedded chapters in finished files.
- Persistent project states, resumption of supported interrupted workflows, progress and warning messages.
- Optical-drive selection and disc-change workflow; initial audio-CD detection and ripping support.
- Output folder and filename settings, custom application dialogs and notification options.
- Source files processed locally without renaming or overwriting originals.
- Source published under **GPL-3.0-only**.

*Scope:* This records the feature baseline present in 1.0.0, not every development commit as a separate release. The fully developed 2.0 metadata editor, 21-language UI and standard Drive Circle implementation are listed under 2.0.0 rather than retroactively attributed to 1.0.0.

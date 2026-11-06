# BookStitch – Changelog (English)

BookStitch for Windows development history, newest version first. Version 1.0.0 was the initial public release. Versions 2.0.0 through 2.3.0 were unpublished development states consolidated into the 2.4.0 release.

## [2.4.0] – 6 November 2026

Includes all previously unpublished development states documented below, including 2.3.0.

- Field-specific external multi-value import with semicolon and eligible slash separators, preserved raw source evidence and correctly paired structured series/part values. Manual multi-value entry continues to use semicolons.
- Added Sort Composers, Goodreads URL and MusicBrainz Work ID; removed active Barcode and MusicBrainz Release ID fields without confusing release IDs with work IDs.
- Unified Year and Publication into a precise Release Date with native MP4 date output; improved native subtitle and sorting fields while preserving year, month or full-date precision.
- Unconfirmed source-value suggestions for every sort field, including dynamically inherited Album/Composer values. Suggestions require explicit acceptance before output.
- Compact AAC pass-through decision immediately after every eligible new folder import, showing the recommended profile and preserved/re-encoded track durations. Explicit mono/stereo alternatives and a project-specific original-bitrate mode; HE-AAC remains excluded and Re-encode all remains binding.
- Project-scoped reuse of complete audio-probe evidence, targeted invalidation and fewer redundant probes, decoding checks, remuxes and encoder calibrations. Valid confirmed plans are reused at Start; final stream/timing checks and safety fallbacks remain in place.
- New default track-column layout with Start hidden and corresponding IN/OUT values in the same position. Four localized technical pairs show bitrate, sample rate (Hz), channels and AAC profile; prepared OUT values update live, including after pause/resume, with measured stream properties taking priority.
- More compact track columns with font- and DPI-aware AutoFit, sort-indicator space only when needed, and column-menu access even when columns are hidden. Manual visibility choices take precedence over automatic IN/OUT switching until the default layout is restored.
- Track rows update as local copy/conversion checkpoints complete; middle-click scrolling remains available during preparation. Every eligible new import gets its own pass-through choice, without a duplicate preset warning after confirmation; the general bitrate advisory now allows differences of up to 15 kbit/s.
- Embedded-cover decoder errors no longer invalidate otherwise valid audio or hide existing chapters. Removed the extra profile status row while retaining progress feedback.
- Updated application/Start-menu icons and packaging assets; harmonized metadata, sorting and AAC labels across all 21 UI languages.

## [2.3.0] – Unreleased development state

- Improved saving and closure of failed MP3-CD/audio-CD projects: choose Save for disc-boundary recovery or Delete for complete managed-project cleanup and an empty workspace. Metadata and cover survive saving; external original media are preserved.
- Consolidates the unpublished 2.2.1 development improvements documented below, including more precise chapter warnings and robust audio analysis and optical-drive handling. Updated release notes and documentation for 2.3.0.

## [2.2.1] – Unreleased development state

- After a failed MP3-CD or audio-CD import, Close project now offers Save or Delete. Saving uses the same disc-boundary recovery as closing the window; deleting removes the managed project and resets the workspace. Dialog translated and checked in all 21 UI languages.

- Omit already-resolved source track-tag conflicts when a reliable filename inventory matches every working track number; keep ambiguous conflicts and independent duplicate, gap and ordering warnings.
- Drain FFprobe output streams directly before accepting results and wait asynchronously for optical-drive access; preserve cancellation and the completion boundary of running native operations.
- Avoid false duplicate-chapter warnings for proven, consecutive zero-padded filename families when every source track tag contains the same number. Keep one representative metadata conflict per family; preserve source tags, ordering and chapter proposals.

## [2.2.0] – Unreleased development state

Includes the unpublished 2.1.7 development state; neither 2.1.7 nor 2.2.0 has been released.

- Automatic narrator recognition from complete German and English credits at the end of titles, including credits without parentheses; user-confirmed metadata is preserved.
- Custom chapters use the selected album correctly, preserve independent chapter titles and handle technical chapter numbering more reliably.
- Manual order and exclusions survive MP3-CD import and resume. Missing project sources remain visible and prevent incomplete exports.
- Pausing at the next MP3-CD dialog now cancels running AAC jobs in a controlled way.
- Additional warning when an original track-number tag conflicts with the number in the filename.
- Reworked ivory, teal and gold branding and DPI-specific ordering icons; additional appearance variants can be previewed for the session in Developer Tools. Deep Petrol is the new default and fallback background.
- Larger main window on startup and corrected Drive Circle continuation after accepting an audio CD.

- Faster track reordering preserves existing rows and selection; chapter suggestions and timelines remain synchronized.
- Fix automatic chapter proposals with trailing file numbers across 100/1000 and disc boundaries.
- Resume missing live AAC conversions from verified existing MP3-CD copies after pause; retain final completeness checks.

- Disable track reordering throughout pause; commit language autocomplete values with Enter; avoid repeated column autosizing on reorder and use complete filename inventories for sequence warnings.

### Added

#### Cover import and preparation
- Drag images directly from supported browsers onto the cover area in the main window or metadata editor, when image data or an accessible image URL is available.
- Covers with a shortest edge of at least 200 pixels are accepted. Automatic square output uses 1000, 2000 or 3000 pixels, with adaptive JPEG quality.
- Choose Automatic, White or Black for transparent cover backgrounds in global metadata settings. The choice applies to newly imported covers.
- Bundled WebP decoding works without an installed Windows WebP extension. Cover processing also handles image orientation and color profiles; ImageSharp is no longer required.

## [2.0.0] – Unreleased development state (notes from October 2026)

### Added

#### Architecture and engineering
- Substantial architecture overhaul: more modular code, clearer separation of UI, project data, media analysis, export and processing workflows, and centralized state and error handling.
- Windows test suite thoroughly reorganized and expanded with stronger focused regression tests for UI, metadata, chapters, disc import, project resumption and export.

#### Formats, covers and export
- Local audio support for M4A, M4B, MP3, WAV, FLAC, OGG, AIFF and legacy WMA, subject to the installed FFmpeg capabilities.
- AAC-based M4A/M4B output, selectable mono/stereo bitrates up to 256 kbps and configurable parallel processing.
- JPEG, PNG and WebP cover import, preview, preparation and embedding.
- Automatic or manually confirmed merge after preparation; compatible prepared audio can be reused.
- New exports write chapter markers in the QuickTime format.
- More accurate chapter timing through improved duration calculations and AAC padding handling; accuracy depends on the source and container time resolution.

#### Track management and project workflows
- Automatic and manual track ordering, including reordering, exclusion and restoration; improved technical track-number recognition and gap/duplicate warnings.
- Expanded display of nested embedded chapters and their start times directly in the track list.
- Improved project resumption and re-export with controlled rollback of interrupted additions, reconversions and merge attempts.

#### Metadata and tagging
- Comprehensive metadata editor with drafts, confirmed values and suggestions.
- Distinction between values obtained from sources, automatically suggested values and manually entered metadata.
- Multi-value processing for supported metadata fields, including editing, ordering and output options.
- Candidate-based entry, autocomplete, source evidence and cross-field search.
- Expanded metadata fields, including structured series/series number, original title and other audiobook information.
- Localized genre catalogue with 74 entries and configurable genre selection.
- Additional audiobook player compatibility options: information in Description, Narrator → Composer and Title → Album, where enabled.

#### Chapters
- Significantly expanded analysis and normalization of chapter candidates.
- Multilingual technical chapter markers and number words, with conservative removal of purely technical numbering.
- Expanded chapter preview and editing, including embedded subchapters and timeline information.
- Configurable chapter modes, including intentional output without chapters.

#### Audio analysis, output and performance
- Parallelized FFprobe calls for faster analysis of large projects.
- Expanded AAC pass-through checks of actual codec, channel, stream and sample-rate properties.
- Project-specific sample-rate management and additional target sample-rate options.
- Display of original source bitrate, sample rate and mono/stereo properties.
- Expanded progress feedback, including per-track progress.

#### CD workflows and projects
- Fully integrated **Drive Circle** for up to five configured optical drives; not an experimental feature.
- Expanded disc recognition and drive management, including persistence of selected drives and recognition of required media.
- More robust multi-disc project resumption with disc identification and protection against accidental duplicate imports.
- Project integrity inspection with guarded repair options and reconstruction of derived data from intact originals.
- Versioned project layout and controlled migration of older project states.
- Improved project management with retention periods, deletion functions and safe automatic maintenance.

#### User interface and localization
- **21 UI languages**, including Bernese Swiss German and simplified and traditional Chinese.
- Reworked main view, metadata editor, project preparation, settings and project continuation.
- New interactive FAQ/help section.
- Expanded accessibility labels and improved multilingual search, including Unicode and CJK input.

### Changed

#### Metadata
- Thoroughly revised reading, evaluation and consolidation of metadata from local audio, MP3/data CDs and audio CDs.
- More reliable prioritization of source values and user-confirmed values.
- More reliable metadata persistence, cover selection, output policies and suppression of deliberately removed values.
- Improved M4A/M4B tag output and preservation; clearer compatibility options for different players.

#### Chapters and ordering
- Improved automatic track ordering and more robust treatment of disc and track numbers.
- More accurate chapter detection from filenames, tags and existing chapter markers.
- More reliable timestamps, including long track lists and mixed embedded chapters.
- Better preview, display and editing of chapter suggestions.

#### Conversion and export
- Improved reuse of eligible AAC audio without re-encoding when technical requirements are met.
- More robust checks and handling of mixed sample rates.
- Optimized processing of already prepared and cached AAC tracks.
- Faster, more responsive loading, display and analysis of large projects.
- Clearer export warnings, error details and dialogs.

#### Project and CD workflows
- More reliable pausing, resumption and saving, including interrupted disc and merge operations.
- Improved rollback protection when adding more sources, reconverting and interrupting exports.
- More responsive drive detection and targeted refresh on media changes.
- Safer switching between projects and protection against inconsistent intermediate states.

### Fixed
- Cumulative chapter timing deviations caused by AAC padding and rounding reduced or resolved.
- Chapter numbering issues at transitions such as 099/100 and 999/1000 resolved.
- Technical chapter numbers are less likely to be taken for actual chapter titles; meaningful numeric titles are protected.
- Fixed audio-CD sector access to prevent truncated track beginnings and incorrectly read final tracks.
- Improved MP3-CD disc association, duplicate detection and recovery after import errors.
- Fixes to resumption, project state and protection of existing source and output files.
- Fixed multiple metadata dropdown, candidate selection and Unicode search issues.
- Improved window and dialog presentation, including maximizing within the usable screen area.
- More robust FFmpeg progress callback and background error handling.

### Verification
- Real-media checks covered large track sets, multi-disc work, decoding, chapter positioning and unchanged originals. These are reported test results, not a guarantee for every hardware configuration.

### Unchanged principles
- Local audio processing without a cloud account.
- Original audio files are not overwritten or renamed.
- AAC-based M4A or M4B output.

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

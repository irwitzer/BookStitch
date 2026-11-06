# BookStitch 2.4.0 – Release Notes

**Release date:** 6 November 2026
**Platform:** Windows

BookStitch 2.4.0 brings together the improvements developed since the initial 1.0.0 release. It offers a more capable audiobook metadata editor, clearer chapter handling, faster AAC preparation, more dependable CD and project workflows, and a redesigned interface with 21 language variants.

Versions 2.0.0 through 2.3.0 were internal, unpublished development states. Their improvements are included in **2.4.0**, rather than representing separate public releases.

## Highlights in 2.4.0

### Metadata, tagging and sorting
- Import external multi-value metadata using field-specific rules for semicolons and eligible slash separators. Manual multi-value entry continues to use semicolons. Repeated tags and original source evidence are retained, while structured series names and part numbers stay correctly paired.
- Add **Sort Composers**, **Goodreads URL** and **MusicBrainz Work ID**. Barcode and MusicBrainz Release ID are no longer active metadata fields; an existing release ID is not misidentified as a work ID.
- Use one **Release Date** value across Quick Access and Publication, replacing the separate Year field. Year-only, year-and-month and full-date precision are preserved through native MP4 date metadata. Native subtitle and sorting-field handling is improved.
- All sort fields can suggest values from their corresponding metadata, including values inherited through optional Title-to-Album and Narrator-to-Composer mappings. Suggestions remain unconfirmed until explicitly accepted and do not silently enable export.

### AAC pass-through and faster preparation
- For each eligible new local-folder import, a compact decision dialog appears after analysis when a pass-through choice is needed. It shows the recommended audio profile and the durations to preserve or re-encode. Choose the optimized pass-through plan, **Re-encode all** using your settings, or return to the project.
- Eligible AAC-LC audio can be evaluated for mono/stereo alternatives and, where suitable, a project-specific original-bitrate mode. **HE-AAC is not eligible for pass-through.** A confirmed **Re-encode all** choice remains in effect across review, preset changes and resume.
- Technical analysis results are reused per source within the project. Valid confirmed plans do not undergo the same full checks again at Start; changed inputs invalidate only the affected evidence.
- Reduce redundant probing, short decoding, remux operations and encoder calibration. ADTS checking uses a short sample before the full remux during confirmed preparation. Work is parallelized within controlled limits, with additional performance diagnostics.
- Preserve final audio-stream and timing validation, with a controlled fallback when lossless reuse is unsafe. Disc projects and older project workflows retain their established behavior.
- After an accepted pass-through choice, a redundant conflicting bitrate warning is no longer shown. The general bitrate advisory accepts differences of up to and including 15 kbit/s.

### Clearer track information and live progress
- A new default column layout places corresponding **IN** and **OUT** values together and hides the **Start** column by default. Four technical pairs display **bitrate, sample rate (Hz), channels and profile**; **Type** and **Codec** remain separate.
- OUT values appear as individual tracks finish successful copy or conversion checkpoints, including after pause and resume. Measured output-stream values take precedence over planned settings, and rows update even while source paths still refer to external originals.
- Automatic IN/OUT visibility adapts to the workflow while respecting manual column choices; restoring the default layout restores automatic behavior.
- More compact columns use font- and DPI-aware AutoFit. Sort indicators reserve space only when displayed, and the column menu remains accessible even when columns are hidden. Middle-click autoscroll remains available during preparation.

### Reliability, appearance and localization
- A failure limited to decoding embedded cover artwork no longer causes otherwise valid audio to be rejected or its embedded chapters to disappear. Genuine audio failures and ambiguous container errors still receive appropriate safeguards.
- Simplified the main status display without removing progress or status feedback.
- Refreshed application icons, Windows Start-menu assets and packaging resources.
- Harmonized AAC, metadata, sorting and technical-column terminology across **all 21 interface language variants**.

## Other major improvements included since 1.0.0

### A fuller audiobook metadata editor
- Edit source-derived metadata, drafts, automatic suggestions and confirmed values while keeping their origin and confirmation status clear.
- Add, remove, reorder and selectively export multiple values in supported fields; search across metadata candidates and use autocomplete.
- Better aggregate tags from local files, MP3/data CDs and audio CDs, with user-confirmed choices taking priority.
- Use a localized catalogue of **74 genres** with configurable preferences.
- Improve compatibility with different audiobook players through optional information in Description, Narrator-to-Composer and Title-to-Album mappings, and more reliable M4A/M4B metadata output.
- Automatically recognize narrator credits at the ends of German and English titles, including credits without parentheses, without replacing confirmed metadata.

### Cover artwork
- Import **JPEG, PNG and WebP** images, preview them and embed prepared artwork into exported audiobooks.
- Drag an image from supported browsers into the main window or metadata editor when image data or an accessible image URL is provided.
- Accept images with a shortest edge of at least **200 pixels** and prepare square covers at **1000, 2000 or 3000 pixels** with adaptive JPEG quality.
- Choose **Automatic, White or Black** backgrounds for newly imported transparent artwork. Cover processing respects image orientation and color profiles; bundled WebP decoding does not require a separate Windows WebP extension.

### Chapters and track organization
- Improve automatic chapter detection from filenames, tags and existing chapters, including multilingual chapter patterns and technical numbering across **099/100**, **999/1000** and disc boundaries.
- Edit chapter titles and preview chapter structures, including nested embedded chapters and their timeline positions. Custom chapters use the selected album while preserving independent chapter titles.
- Improve timestamp precision by addressing duration rounding and AAC padding. Actual accuracy remains subject to the source and container timing resolution.
- New exports write **QuickTime chapter markers**.
- Reorder, exclude and restore tracks; preserve manual order and selection during supported operations. Warn about duplicate, missing or inconsistent numbering without generating false duplicate-chapter warnings for proven filename sequences.

### Disc imports and resilient projects
- **Drive Circle** is a standard integrated feature supporting up to **five configured optical drives**, drive order and disc changes in multi-disc workflows.
- Improve disc identification, duplicate protection, optical-drive responsiveness, MP3/data-CD handling and audio-CD extraction, including corrections to raw-sector addressing.
- Following a failed MP3-CD or audio-CD import, **Close project** offers **Save** or **Delete**. Save retains completed discs, metadata and artwork while allowing recovery at the failed disc boundary; Delete removes the managed project after active operations stop and returns to an empty workspace. External source media remain untouched.
- More reliable pause, resume and re-export protect project state. Interrupted AAC jobs can be resumed or rescheduled from verified working copies; missing project sources remain visible and block incomplete output.
- Add project-integrity checks, controlled repair/reconstruction, migration support and guarded rollback for interrupted source additions, reconversion and merge/export operations.

### Audio formats, export and interface
- Import local **M4A, M4B, MP3, WAV, FLAC, OGG, AIFF and legacy WMA** audio, subject to available FFmpeg capabilities. Export AAC-based **M4A or M4B** with mono/stereo presets up to **256 kbps**.
- Benefit from parallel media analysis, configurable processing concurrency, automatic or manually reviewed merging, and reuse of compatible prepared audio.
- Use the redesigned main window, project browser, settings and metadata navigation, including an interactive FAQ/help area and improved Unicode/CJK input and search.
- Benefit from a substantial internal architecture and regression-test-suite overhaul focused on metadata, chapters, CD imports, recovery and export. The default appearance uses the updated **Deep Petrol** theme.

## Important behavior

BookStitch processes audiobook files **locally**, without requiring an account. It does **not rename or overwrite original audio files**. Export files use AAC audio in an M4A or M4B container. Some import and conversion capabilities depend on the FFmpeg components available with the installation.

For a version-by-version technical history, see **Changelog-EN.md**. Versions **2.0.0–2.3.0** are development milestones included in this release, not separately published versions.

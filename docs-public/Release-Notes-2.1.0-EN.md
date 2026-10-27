# BookStitch 2.1.0 – Release Notes

BookStitch 2.1.0 is a major update for creating and managing complete audiobooks. It introduces a much more capable metadata workflow, substantially better chapter handling, more reliable CD and project processing, faster analysis of large audiobook collections, and a new cover-processing pipeline with direct browser drag-and-drop. Existing audiobook projects remain compatible.

## What's new and improved

BookStitch has also received a substantial internal architecture overhaul, with more modular components and a thoroughly restructured Windows regression-test suite.

### Comprehensive metadata editor
- A complete tagging workflow with editable drafts, values extracted from audio files and CDs, automatic suggestions and manually entered tags.
- A clear indication of where metadata came from and whether a value is suggested or user-confirmed.
- Multi-value support in appropriate fields: add, edit, reorder and selectively export several values per field.
- Greatly improved reading and aggregation of metadata across local audio files, MP3/data CDs and audio-CD projects.
- Better search, candidate selection, autocomplete and metadata editing.
- A localized genre catalogue with 74 entries and genre preferences.

### Cover import and preparation
- Import JPEG, PNG and WebP covers and embed artwork into finished audiobooks.
- Drag images directly from supported browsers onto the cover area in the main window or metadata editor, when image data or an accessible image URL is available.
- Covers with a shortest edge of at least 200 pixels are accepted. Automatic square output uses 1000, 2000 or 3000 pixels, with adaptive JPEG quality.
- Choose Automatic, White or Black for transparent cover backgrounds in global metadata settings. The choice applies to newly imported covers.
- Bundled WebP decoding works without an installed Windows WebP extension. Cover processing also handles image orientation and color profiles; ImageSharp is no longer required.

### Better audiobook player compatibility
- Additional metadata export options for players and library systems that interpret fields differently.
- Compatibility information can be placed in the Description field.
- Configurable Narrator-to-Composer and Title-to-Album mappings for compatible player workflows.
- More reliable M4A/M4B metadata writing.

### Much better chapters
- Reworked automatic chapter detection from file names, track tags and existing embedded chapters.
- Improved title cleanup, chapter numbering and multilingual chapter patterns, even in large audiobooks.
- Manual chapter editing, enhanced previews and clearer chapter structure.
- More precise export timing to avoid cumulative chapter-marker drift.
- New exports write QuickTime chapters; compatible editing paths preserve existing Nero chapter structures in older files. New exports do not create both chapter formats simultaneously.

### Faster analysis and improved export
- Parallel FFprobe analysis for substantially faster inspection of projects containing many tracks.
- More robust AAC pass-through detection and validation, reusing suitable AAC data without unnecessary re-encoding.
- Better handling and display of original bitrate, sample rate and channel layout, plus additional sample-rate options.
- More reliable M4A/M4B exports, with verification of audio and chapter behavior.

### CD import and Drive Circle
- **Drive Circle is now a fully integrated, standard feature**, not an experimental option.
- Coordinate up to five optical drives with configurable order and automatic disc changes in multi-disc projects.
- Improved disc identification, duplicate protection and resumption after interrupted imports.
- Refined workflows for MP3/data discs and audio CDs, with clearer status and progress feedback.

### Projects, interface and languages
- More reliable pause, resume, save, recover and re-export workflows.
- Project integrity inspection and controlled repair options for older or damaged project states.
- Reworked UI, track list, settings and metadata navigation.
- **21 UI language variants**, including Bernese Swiss German.
- An interactive FAQ/help area.
- Numerous stability and usability fixes, especially for larger projects.

### Formats and track management
- Import M4A/M4B, MP3, WAV, FLAC, OGG, AIFF and legacy WMA (depending on installed FFmpeg capabilities); export AAC-based M4A or M4B.
- Select mono/stereo presets up to 256 kbps, with automatic merge or manual review.
- More flexible track ordering, exclusion and restoration, including embedded chapter hierarchy and start times.

## Still important

Audio processing remains local. BookStitch does not rename or overwrite original audio files, and exports AAC-based M4A or M4B audiobooks.

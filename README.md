# BookStitch

BookStitch is a calm workspace for audiobook projects: import tracks, review warnings, edit metadata, create chapters, and export a complete audiobook file.

## Installation

BookStitch is available in the Microsoft Store:

https://apps.microsoft.com/detail/9P9MH8WL8KGP

Install from the command line with WinGet using the Microsoft Store source:

```powershell
winget install 9P9MH8WL8KGP --source msstore
```

Alternatively, a classic Windows installer and a portable ZIP are available on the GitHub release page:

https://github.com/irwitzer/BookStitch/releases

## What BookStitch can do

- Import local audio files, MP3 CDs, data CDs and audio CDs.
- Merge individual MP3, WAV and other audio files into one complete audiobook file.
- Create M4A and M4B audiobook files with chapter markers, cover art and metadata.
- Review, sort and rename tracks, and exclude tracks from the export when needed.
- Show typical problems such as duplicate tracks, missing tracks, unclear ordering or chapter issues.
- Read existing metadata and tag information from source files.
- Edit cover art, title, album, author, narrator and genre.
- Detect chapter markers from existing source files and carry them over into the output file.
- Generate chapter suggestions from file names and track information.
- Automatically number chapter suggestions in natural language.
- Check, convert and prepare source files for export with FFmpeg and FFprobe.
- Save project states, pause work and continue later.
- Support CD workflows with multiple optical drives through Drive Circle.
- Configure notifications flexibly: sound scheme, audio notifications and window focus can be configured separately.

## Concept

BookStitch is a Windows desktop application for creating complete audiobook files from local audio files, MP3/data CDs and audio CDs. Export is designed for AAC-based M4A and M4B files with chapters and metadata.

The project is intended as a stable, transparent and data-integrity-focused audiobook workflow. It focuses on safe project states, reliable CD and audio-CD import pipelines, clear feedback in the user interface and predictable export behavior.

Tracks are shown in a clear list. BookStitch detects and marks typical problems such as incorrect sort order, duplicate tracks, missing tracks or unclear chapter structures. In addition to automatic sorting, tracks can be sorted by different criteria, such as path, tags, CD information, file name or manual order. Individual tracks can be excluded from the export when needed.

Automatic chapter-marker detection helps create traceable chapters in the output file. If source files do not contain usable chapter information, BookStitch can generate chapter suggestions in natural language from file names and track information and number them cleanly. Tracks excluded from the export are taken into account.

Metadata and cover art are edited directly in the project and remain part of the traceable working state. Title, album, author, narrator, genre and cover art can be reviewed and adjusted before export. Cover images are prepared for audiobook output; BookStitch warns about problematic or too-small images so the output can be checked deliberately. Extended metadata is planned but not yet fully implemented.

BookStitch works project by project: import, review, conversion and export remain available as a traceable project state. This makes it possible to prepare larger audiobook projects safely, pause them and continue later without losing track of sources, tracks, warnings and output state.

The original files remain protected. BookStitch only reads source files and does not modify them. MP3, WAV and other audio files, as well as CD contents, are not overwritten, renamed or edited directly. All processing steps happen inside the BookStitch project and in the generated working and output files.

For CD-based audiobook projects, BookStitch includes the Drive Circle feature. Up to five optical drives can be loaded with CDs and processed one after another. This reduces manual disc changes and makes longer import runs more convenient, especially for large audiobooks with many discs.

For CD and MP3/data-CD workflows, BookStitch uses detection and fingerprinting to distinguish inserted media more safely. Already processed CDs can be recognized and reported, so a disc is not accidentally imported twice and an audio CD is not processed in the wrong workflow. This protects the project state and helps build clean, complete audiobook imports.

BookStitch is designed to accompany longer processing runs in a calm and traceable way. Audio notifications, sound scheme and window focus can be configured independently. This allows BookStitch to either work discreetly in the background or draw more attention to completed steps, required input and CD changes.

## Audio processing

BookStitch uses FFmpeg and FFprobe for audio analysis, conversion and export. FFmpeg is not bundled with BookStitch, but it can be downloaded and configured from within BookStitch.

Source files are checked, prepared and converted when needed. Export is designed for AAC-based audiobook files in M4A and M4B containers.

The output can include chapter markers, cover art and metadata. The original files are not modified.

## CD workflows

BookStitch supports dedicated workflows for MP3/data CDs and audio CDs.

For MP3 and data CDs, the existing audio files are read directly and added to the project. Audio CDs are read through the dedicated audio-CD workflow and prepared for further processing.

Drive Circle can be used for larger CD projects. Up to five optical drives can be processed one after another. BookStitch detects inserted media, checks them in the context of the project and warns about already processed or incorrectly inserted CDs.

## Chapters and metadata

BookStitch can take existing chapter information from source files and prepare it for the output file. If no usable chapter information is available, chapter suggestions can be generated from file names and track information and numbered automatically.

Chapter naming can be adjusted, including numbering with or without leading zeros. This keeps chapters consistently named, even when many individual tracks or multiple CDs are being processed.

Basic metadata such as title, album, author, narrator and genre can be edited in the project and written to the finished audiobook file during export. Extended metadata is planned but not yet fully implemented.

Cover images can be imported from JPEG, PNG and WebP files. If needed, covers are automatically cropped to a square format using center crop. BookStitch shows a warning for cover images that are too small, so the output quality can be checked deliberately.

## Privacy

BookStitch works locally on your own Windows PC.

The actual audiobook processing does not require a user account or an internet connection. The app does not collect usage data, analyze data or transmit files, metadata or usage information to servers or cloud services. Audio files, project files, cover art and metadata remain on the local system.

BookStitch contains no advertising, tracking or telemetry. Advertising, data analysis and data-based business models are not included and are not planned.

FFmpeg and FFprobe are used only for local audio analysis, conversion and export. This processing also takes place locally.

## Requirements

BookStitch requires Windows.

FFmpeg and FFprobe are used for audio analysis, conversion and export. If FFmpeg is not yet available, installation can be started and configured directly in BookStitch.

Building from source requires the .NET Desktop SDK. The normal installation through the Microsoft Store, WinGet or a GitHub release does not require a separate .NET SDK.

## Building from source

BookStitch can be built from source with Visual Studio or through the .NET command line.

Development requires the .NET Desktop SDK. Visual Studio with the .NET desktop development and WPF workloads is recommended.

Build from the command line:

```powershell
dotnet build
```

Run automated tests:

```powershell
dotnet test
```

`dotnet test` builds the project and runs the tests in the test project.

## License

BookStitch is licensed under the GNU General Public License v3.0 (`GPL-3.0-only`). The full license terms are available in the [LICENSE](LICENSE) file.

The license allows use, study, modification and redistribution, including commercial use. If modified versions are distributed, they must be distributed under the same license terms, and the corresponding source code must also be made available.

Third-party components are licensed under the respective licenses of their copyright holders. More information is available in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Assets

BookStitch-specific icons, logos and bundled notification sounds were created for this project and are part of BookStitch.

Third-party components, external tools and third-party content are listed separately. More information is available in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Project repositories

This public repository contains the public source code states, release notes, documentation and downloadable release artifacts for BookStitch.

Internal development may happen in a separate development repository before reviewed source code states are published.

## Contributing

Bug reports, notes and improvement suggestions can be submitted through GitHub Issues.

Larger changes should be discussed in an issue first, so they fit the goals and architecture of BookStitch.

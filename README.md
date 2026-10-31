# BookStitch

**Turn audio tracks and CDs into complete audiobooks.**

BookStitch is a free, open-source Windows application built with C# and WPF. Import audio files or CDs, organize tracks, create chapters, edit metadata, add cover art, and export complete M4A or M4B audiobooks — all in one workflow.

Your original audio files remain untouched.

**[Official Website](https://boxstitch.app)** · **[Download BookStitch](https://github.com/irwitzer/BookStitch/releases)** · **[Microsoft Store](https://apps.microsoft.com/detail/9P9MH8WL8KGP)** · **[Report an Issue](https://github.com/irwitzer/BookStitch/issues)**

## Features

- **Import audio and CDs:** M4A, M4B, MP3, WAV, FLAC, OGG, AIFF and legacy WMA (depending on FFmpeg support). Supports MP3/data CDs, audio CDs and multi-disc sets.
- **Organize tracks:** Automatic ordering, manual reordering, exclusion and restoration, with warnings for missing or duplicate track numbers.
- **Create chapters:** Detect embedded chapters, generate suggestions from filenames and metadata, edit chapter titles and review timelines. Exports include QuickTime chapters.
- **Edit rich metadata:** Review and confirm metadata suggestions, manage authors, narrators, genres, series and other supported multi-value fields. Includes a localized 74-genre catalogue.
- **Improve player compatibility:** Configurable metadata output, including Narrator-to-Composer and Title-to-Album mappings and compatible descriptive information.
- **Manage cover art:** Import images from files or supported browsers, adjust transparency backgrounds, preview artwork and embed covers. WebP decoding is bundled.
- **Prepare and export:** AAC mono/stereo presets up to 256 kbps, smart AAC reuse without unnecessary transcoding, optional review before merging, and M4A/M4B export.
- **Resume projects:** Save and reopen projects, add sources, reconvert and export again while preserving project state.
- **Drive Circle:** Coordinate up to five optical drives with automatic disc changeover for multi-disc projects.
- **Use your language:** 21 supported UI language variants, including Bernese Swiss German.

## Installation

Download BookStitch from [GitHub Releases](https://github.com/irwitzer/BookStitch/releases) or install it through the [Microsoft Store](https://apps.microsoft.com/detail/9P9MH8WL8KGP).

For portable versions, extract the complete archive and launch `BookStitch.exe`.

You can also install the Microsoft Store version using WinGet:

```powershell
winget install 9P9MH8WL8KGP --source msstore
```

**FFmpeg and FFprobe are required** for audio analysis and export but are not bundled with BookStitch. Use an existing installation or launch their setup from within the application.

Packaged Windows builds do not require the .NET SDK. An optical drive is only needed for CD import.

## Quick Start

1. Add an audio folder, individual files or a CD project.
2. Review track order, chapters, metadata and cover art.
3. Choose an output folder and AAC preset. Enable **Auto Merge** for automatic processing or disable it to review tracks before merging.
4. Open your finished M4A or M4B audiobook in your preferred player.

## Privacy

**Your files stay yours.**

BookStitch processes your audio locally. It does not collect personal data, track usage, send telemetry or upload your files. No account is required.

Optional actions such as downloading FFmpeg, retrieving cover art from a URL or opening external links may connect to third-party services.

For more information, see the [Privacy Policy](https://boxstitch.app/#privacy).

## Build from Source

Building requires Windows and the **.NET 10 SDK**. Visual Studio with the .NET desktop development workload is optional.

From the repository root:

```powershell
dotnet build ./BookStitch/BookStitch.csproj -c Release
dotnet run --project ./BookStitch/BookStitch.csproj -c Release --no-build
dotnet test ./BookStitch.Tests/BookStitch.Tests.csproj -c Release
```

To publish a self-contained Windows x64 distribution:

```powershell
dotnet publish ./BookStitch/BookStitch.csproj -c Release -p:PublishProfile=EndUser -o ./artifacts/BookStitch
```

Distribute the complete published directory and its dependencies. See the [build and packaging notes](docs-public/release-build-notes.md).

## Documentation

- [Release Notes 2.2.0](docs-public/Release-Notes-2.2.0-EN.md)
- [Changelog (English)](Changelog-EN.md)
- [Localization Guide](docs-public/localization-guide.md) · [Localization Rules](BookStitch/Resources/LOCALIZATION_RULES.md)

## Contributing and Support

Report bugs or suggest improvements through [GitHub Issues](https://github.com/irwitzer/BookStitch/issues).

When reporting a problem, please include your BookStitch version, Windows version, steps to reproduce, and expected versus actual behavior. Remove personal information from logs before sharing them.

Translations are maintained in the [Language Master](BookStitch/Resources/BookStitch.LanguageMaster.json). Generated RESX files should not be edited directly.

## License

BookStitch is licensed under [GNU GPL v3.0 only](LICENSE) (`GPL-3.0-only`).

Third-party dependencies and assets have separate license terms. See [Third-Party Notices](THIRD_PARTY_NOTICES.md).

# BookStitch

**Turn audio tracks and CDs into complete audiobooks.**

BookStitch is an open-source Windows desktop application built with C# and WPF. It brings audio import, track ordering, chapter editing, metadata, cover art and AAC export together in one project workflow. Create M4A or M4B audiobooks without modifying your original audio files.

[Download BookStitch](https://github.com/irwitzer/BookStitch/releases) · [Microsoft Store](https://apps.microsoft.com/detail/9P9MH8WL8KGP) · [Report an issue](https://github.com/irwitzer/BookStitch/issues)

## Features

- **Import local audio and CDs:** M4A, M4B, MP3, WAV, FLAC, OGG, AIFF and legacy WMA (depending on FFmpeg support); MP3/data CDs and audio CDs, including multi-disc sets.
- **Manage tracks:** automatic ordering, manual reorder, exclusion and restoration, plus warnings for missing or duplicated track numbers.
- **Create chapters:** detect existing embedded chapters, generate chapter suggestions from names and tags, edit titles and review chapter timelines. New exports write QuickTime chapters.
- **Edit rich metadata:** draft and confirm values, inspect source-derived suggestions, and manage supported multi-value fields such as authors, narrators, genres and series. A localized 74-genre catalogue is available.
- **Improve player compatibility:** configurable metadata output, including Narrator-to-Composer and Title-to-Album mappings and compatible descriptive information.
- **Manage cover art:** import images from files or drag them from supported browsers, choose automatic/white/black transparency backgrounds, preview artwork and embed it into the finished audiobook. WebP decoding is bundled.
- **Prepare and export:** select AAC mono/stereo presets up to 256 kbps, reuse qualifying AAC without unnecessary transcoding, review before merge or enable Auto Merge, and export M4A or M4B.
- **Resume projects:** pause or continue supported workflows, reopen projects, add sources, reconvert and export again with protected project state.
- **Drive Circle:** coordinate up to five optical drives in configurable order with automatic disc changeover for multi-disc projects. Drive Circle is a standard BookStitch feature.
- **Use your language:** 21 supported UI language variants, including Bernese Swiss German.

## Installation

Download a Windows package from [GitHub Releases](https://github.com/irwitzer/BookStitch/releases) or install from the [Microsoft Store](https://apps.microsoft.com/detail/9P9MH8WL8KGP). For portable packages, extract the complete archive and launch `BookStitch.exe` from the extracted directory.

Install the Store listing through WinGet:

```powershell
winget install 9P9MH8WL8KGP --source msstore
```

**FFmpeg and FFprobe are required** for audio analysis and export but are not bundled with BookStitch. Configure an existing installation or start their setup from inside the application. Packaged Windows builds do not require you to install the .NET SDK; an optical drive is only needed for CD import.

## Quick start

1. Add an audio folder, local audio files or a CD project.
2. Review track order, exclusions, chapter proposals, metadata and cover art.
3. Choose an output folder and AAC preset. Use **Auto Merge** to finish automatically or disable it to review the prepared tracks before merging.
4. Open the resulting M4A or M4B audiobook in your preferred player.

## Privacy

BookStitch processes audio locally and does not upload your audio library or require an account. Source audio is read without being renamed or overwritten. Optional activities such as obtaining FFmpeg, fetching a cover by URL or opening external resources require a network connection.

## Build from source

Building requires Windows and the **.NET 10 SDK**. Visual Studio with the .NET desktop development workload is optional. From the repository root:

```powershell
dotnet build ./BookStitch/BookStitch.csproj -c Release
dotnet run --project ./BookStitch/BookStitch.csproj -c Release --no-build
dotnet test ./BookStitch.Tests/BookStitch.Tests.csproj -c Release
```

To publish a self-contained Windows x64 distribution:

```powershell
dotnet publish ./BookStitch/BookStitch.csproj -c Release -p:PublishProfile=EndUser -o ./artifacts/BookStitch
```

Distribute the full published directory and its dependencies. See [build and packaging notes](docs-public/release-build-notes.md).

## Documentation

- [Release notes 2.1.7](docs-public/Release-Notes-2.1.7-EN.md)

- [Changelog (English)](Changelog-EN.md)
- [Localization guide](docs-public/localization-guide.md) · [Localization rules](BookStitch/Resources/LOCALIZATION_RULES.md)

## Contributing and support

Report bugs and suggest improvements through [GitHub Issues](https://github.com/irwitzer/BookStitch/issues). Include the BookStitch version, Windows version, steps to reproduce, expected and actual behavior. Remove personal information from logs before sharing them.

Translations are maintained in the [Language Master](BookStitch/Resources/BookStitch.LanguageMaster.json); generated RESX files should not be edited directly.

## License

BookStitch is licensed under [GNU GPL v3.0 only](LICENSE) (`GPL-3.0-only`). Third-party dependencies and assets have separate license terms; see [Third-Party Notices](THIRD_PARTY_NOTICES.md).

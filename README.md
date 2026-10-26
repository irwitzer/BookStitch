# BookStitch

**Turn audio tracks and CDs into a complete audiobook.**

BookStitch is an open-source Windows desktop application built with C# and WPF. Organize tracks, edit chapters and metadata, add cover art, and export an AAC-based **M4A or M4B** audiobook in one project workflow.

Source audio is read without renaming or overwriting it. Processing takes place locally in separate project and output files.

[Download releases](https://github.com/irwitzer/BookStitch/releases) · [Microsoft Store](https://apps.microsoft.com/detail/9P9MH8WL8KGP) · [Report a bug](https://github.com/irwitzer/BookStitch/issues)

## Features

- **Import:** local audio files, MP3/data CDs and audio CDs, including multi-disc projects.
- **Review:** order or exclude tracks and inspect duplicate, gap and chapter warnings.
- **Chapters:** preserve existing markers or generate and edit chapters from file names and tags.
- **Metadata:** edit title, author, narrator, genre, other supported fields and JPEG/PNG/WebP cover art.
- **Export:** prepare audio with FFmpeg/FFprobe and produce a chaptered, tagged M4A or M4B file.
- **Projects:** save work and resume supported workflows; configure sounds and window-focus notifications.
- **Languages:** 21 UI language variants, with an optional localized genre catalogue.

**Drive Circle** can coordinate up to five optical drives for CD projects. It remains experimental and depends on the drives and discs used.

## Install and use

Choose a Windows package from [GitHub Releases](https://github.com/irwitzer/BookStitch/releases) or the [Microsoft Store](https://apps.microsoft.com/detail/9P9MH8WL8KGP). For the portable ZIP, extract **all** files and run `BookStitch.exe` from that folder.

To install the Store listing with WinGet:

```powershell
winget install 9P9MH8WL8KGP --source msstore
```

**FFmpeg and FFprobe are required** for audio analysis and export but are not bundled with BookStitch. Configure an existing installation or start their setup from the application. An optical drive is needed only for CD import; packaged releases do not require the .NET SDK.

1. Choose an audio folder or CD source, output folder and export preset.
2. Review track order, warnings, metadata, cover and chapters.
3. Prepare the project. Turn off **Auto Merge** to review prepared tracks before merging, or keep it enabled for automatic continuation.
4. Check the resulting audiobook in your preferred player.

## Privacy and network access

Audiobook processing, project files and source media stay on your computer; BookStitch does not upload your audio library or provide usage telemetry. Optional online operations are separate: MusicBrainz audio-CD metadata lookup sends disc-identification information, importing a cover from a URL retrieves that image, and downloading FFmpeg or opening external links requires a network connection. These actions are not needed for ordinary local audio processing.

## Build and test

Building requires **Windows and the .NET 10 SDK**. Visual Studio with the .NET desktop development workload is optional. From the repository root:

```powershell
dotnet build ./BookStitch/BookStitch.csproj -c Release
dotnet run --project ./BookStitch/BookStitch.csproj -c Release --no-build
dotnet test ./BookStitch.Tests/BookStitch.Tests.csproj -c Release
```

Some media, hardware and large-project tests require external tools, fixtures or explicit opt-in. Follow the test guidance in the repository and report what was actually run.

For a self-contained Windows x64 publish:

```powershell
dotnet publish ./BookStitch/BookStitch.csproj -c Release -p:PublishProfile=EndUser -o ./artifacts/BookStitch
```

Distribute the **complete** published directory, including runtime dependencies. See [Build and packaging notes](docs-public/release-build-notes.md).

## Contributing

Focused bug reports, fixes, documentation changes and translations are welcome via [GitHub Issues](https://github.com/irwitzer/BookStitch/issues) and pull requests. Discuss substantial changes first. For bug reports, include the application and Windows versions, steps to reproduce, expected and actual results; remove personal paths or other sensitive data from logs.

Localization uses a single [Language Master](BookStitch/Resources/BookStitch.LanguageMaster.json), not manually edited RESX files. Read the [localization guide](docs-public/localization-guide.md) and [localization rules](BookStitch/Resources/LOCALIZATION_RULES.md) before proposing translations.

## License

BookStitch is licensed under [GNU GPL v3.0 only](LICENSE) (`GPL-3.0-only`). Third-party dependencies and assets are subject to their respective terms; see [Third-Party Notices](THIRD_PARTY_NOTICES.md).

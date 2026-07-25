# BookStitch

BookStitch is a Windows desktop application for creating complete audiobook files from local audio files, MP3/data CDs and audio CDs.

The project is intended as a stable, transparent and data-integrity-focused successor to MarkAble2-style audiobook workflows. It focuses on safe project states, reliable CD/audio-CD import pipelines, clear UI feedback and predictable export behavior.

## Current status

BookStitch is currently in a release-candidate phase. Public releases should be treated as pre-1.0 software until the first final V1 release is published.

## Main features

- Create complete audiobook files from multiple local audio files.
- Import and process MP3/data-CD sources.
- Read and rip audio CDs.
- Convert source tracks with FFmpeg.
- Preserve and edit audiobook metadata.
- Build chapter metadata for the final audiobook file.
- Use project states to keep long-running workflows understandable and recoverable.

## Requirements

- Windows.
- .NET 10 desktop runtime/SDK for building from source.
- FFmpeg and FFprobe for audio probing, conversion and export workflows.

BookStitch can detect configured FFmpeg/FFprobe paths and also supports setup workflows from inside the application.

## Building from source

Open the solution in Visual Studio or build from a .NET command line:

```powershell
dotnet build
```

Run the automated test project with:

```powershell
dotnet test
```

## License

BookStitch is licensed under the GNU General Public License v3.0 only (`GPL-3.0-only`). See [LICENSE](LICENSE).

This license allows use, study, modification and redistribution, including commercial use. If modified versions are distributed, they must be distributed under the same license terms and the corresponding source code must be made available under the GPL.

Third-party components are licensed separately by their respective copyright holders. See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).

## Assets

BookStitch-specific icons, logos and bundled WAV notification sounds were created for this project and are included as part of BookStitch unless otherwise stated.

## Project repositories

The public repository is intended for public source releases, release notes, documentation and downloadable release artifacts. Internal development may happen in a separate private development repository before reviewed source snapshots are published.

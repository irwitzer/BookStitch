# BookStitch – Windows Release Builder v3

This builder creates a self-contained Windows x64 portable folder, portable ZIP, Inno Setup installer EXE and SHA-256 checksums. It does not publish to GitHub.

## Start

1. Place the PowerShell script, BAT launcher and the chosen `.iss` file in the same folder. Start `BookStitchReleaseBuilder_v3_Start.bat`.
2. The builder checks for Inno Setup **7.1.0 or newer** and uses WinGet (`JRSoftware.InnoSetup.7`) to install or upgrade it when required, before the build begins.
3. If saved paths exist, choose whether to reuse them. Otherwise, select the ISS file (automatically selected if exactly one exists), repository root and output root. The .NET 10 SDK is also required.
4. There are **no suffix prompts, filename prompts or final build confirmation**. After selecting or confirming the paths the build begins.

## Output layout

Each run creates a new folder in the chosen output root, named `yyyy.MM.dd--HH.mm.ss_BookStitch`, for example:

```
2026.11.01--09.12.35_BookStitch/
    BookStitch-2.3.0/                  (portable publish folder)
    BookStitch-Portable-2.3.0.zip
    BookStitch-Setup-2.3.0.exe
    BookStitch-2.3.0-SHA256SUMS.txt
```

The version is read from `BookStitch/BookStitch.csproj`. The new folder must not already exist, and the output root must be outside the source repository. The builder refuses to overwrite existing outputs. `builder-settings.json` retains only the ISS, repository and output-root paths; existing settings with older suffix/name fields are still accepted, but those fields are ignored.

The original publish, ZIP, installer and hash operations are preserved. Windows execution was not available for an end-to-end test here.

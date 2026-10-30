# Build and packaging

BookStitch is a Windows WPF application targeting **.NET 10**. Build and test it on Windows with the .NET 10 SDK. Run the manual commands below from the repository root.

## Build and test

```powershell
dotnet build ./BookStitch/BookStitch.csproj -c Release
dotnet test ./BookStitch.Tests/BookStitch.Tests.csproj -c Release
```

Some tests require Windows/WPF, optical drives, external tools, or test media. Consult their requirements before running broader suites. Application tests are maintained in the Windows test project shown above; library regression tests are under `libraries/CoverImageDrop/`. `dotnet test BookStitch.slnx -c Release` runs all five test projects. Report only tests that actually ran.

## Windows release builder (recommended)

The public repository includes a classic Windows release builder at [`dev-support-public/BookStitchReleaseBuilder/`](../dev-support-public/BookStitchReleaseBuilder/). It produces these artifacts from a local repository checkout:

- A complete self-contained **Windows x64 portable folder** and its **ZIP**.
- A classic **Inno Setup 7 installer EXE**.
- A **SHA-256 checksums** file for the ZIP and installer.

Start the builder with its BAT launcher. It uses PowerShell 7 when available, otherwise Windows PowerShell 5.1. It checks for Inno Setup 7 and can offer installation through WinGet with user consent. The **.NET 10 SDK** is also required. The builder selects the `.iss` script, repository and output paths, supports custom release labels and filenames, and can reuse locally saved settings.

See the **README in the builder folder** for the interactive workflow, file selection, settings, and output naming. The builder does not publish to GitHub or modify the repository source. Do not commit its local `builder-settings.json` or generated release files.

## Manual portable Windows x64 package

The `EndUser` publish profile specifies a **self-contained, non-trimmed `win-x64`** deployment with separate runtime files and no debug symbols:

```powershell
dotnet publish ./BookStitch/BookStitch.csproj -c Release -p:PublishProfile=EndUser -o ./artifacts/BookStitch
```

Use a **fresh, empty publish destination**. The publish output, not ordinary `bin/` build output, is the basis for both distributable formats. Package the **entire** directory, not just `BookStitch.exe`. In particular, verify `TagLibSharp.dll`, `BookStitch.runtimeconfig.json`, and the .NET/Windows Desktop runtime files. The CoverImageDrop assemblies and complete `CoverCodecs/` folder (WebP worker and license notices) are also required. Do not remove dependencies to reduce package size. Check portable startup on a compatible clean Windows machine without a separately installed .NET runtime.

The product version is manually increased only in `BookStitch/BookStitch.csproj`. .NET derives the assembly, file and informational versions from it, and Settings → Info reads that assembly metadata. Both release builders pass the project version to Inno Setup for `AppVersion`, `AppVerName` and `VersionInfoVersion`. Use `BookStitch-Setup-{Tag}.exe` and `BookStitch-Portable-{Tag}.zip` as saved filename templates; they are resolved from the current project version and optional candidate on every run. Release descriptions and notes remain editorially maintained. A filename suffix such as `RC1` does not by itself change the version embedded in the executable.

## External audio tools

BookStitch uses **FFmpeg** and **FFprobe** for audio processing. They are not bundled in the standard publish payload. Users may configure an existing installation or use the application's setup flow. Distributors who bundle FFmpeg must comply with the license obligations of the **specific build** distributed. See [Third-Party Notices](../THIRD_PARTY_NOTICES.md).

## Installer, Store, and WinGet

The classic Windows installer uses **Inno Setup 7** and the `.iss` script shipped with the builder. The portable ZIP and installer should be generated from the same verified publish payload. **Microsoft Store packaging is a separate MSIX workflow**, not part of this builder. No Store/MSIX packaging project is present in this repository; check the Store package version separately for each Store release. Code-signing certificates and private signing infrastructure are not included in the repository.

Before publishing, verify installation, upgrade, uninstall, portable startup, version information, and that the installer preserves user projects unless deletion is explicitly confirmed. Review dependency/security advisories, including any NuGet warnings; successful compilation alone is not release approval.

For a WinGet manifest, verify that the published installer URL and SHA-256 match the **exact release asset**; installation scope, installer technology, and unattended command-line switches must match the tested installer. Do not publish untested silent-install switches.

Generated ZIPs, installer EXEs, MSIX/AppxUpload files, signatures, local caches, and machine-specific build outputs are not source files and should not be committed.

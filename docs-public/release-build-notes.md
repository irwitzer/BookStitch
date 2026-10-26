# Build and packaging

BookStitch is a Windows WPF application targeting **.NET 10**. Use a Windows machine with the .NET 10 SDK to build and test the application. Run commands from the repository root.

## Build and test

```powershell
dotnet build ./BookStitch/BookStitch.csproj -c Release
dotnet test ./BookStitch.Tests/BookStitch.Tests.csproj -c Release
```

Some tests require Windows/WPF, optical drives, external tools, or test media. Consult test requirements before running broader suites; do not report a successful full-suite run unless it actually completed. All application tests are maintained in the Windows test project shown above.

## Portable Windows x64 package

BookStitch's `EndUser` publish profile specifies a **self-contained, non-trimmed `win-x64`** deployment, with separate runtime files and no debug symbols:

```powershell
dotnet publish ./BookStitch/BookStitch.csproj -c Release -p:PublishProfile=EndUser -o ./artifacts/BookStitch
```

Use a **fresh, empty publish destination**. The publish output, not `bin/` build output, is the basis for distributable packages. Package the **entire** directory, not just `BookStitch.exe`. In particular, check for `TagLibSharp.dll`, the `.runtimeconfig.json` file, and .NET/Windows Desktop runtime files. Do not remove libraries or runtime files to reduce ZIP size. Verify the ZIP starts and operates on a clean compatible Windows system without a separately installed .NET runtime.

The product version is specified in `BookStitch/BookStitch.csproj`; package filenames and release descriptions must match the built version. Publish only release artifacts that have been tested.

## External audio tools

BookStitch uses **FFmpeg** and **FFprobe** for audio processing. These executables are not bundled in the application source or standard publish payload; users can configure an existing installation or use the application's setup flow. If a distributor bundles FFmpeg, they must review and meet the license obligations of the **specific build distributed**. See [Third-Party Notices](../THIRD_PARTY_NOTICES.md).

## Installer, Store and WinGet

Installer and Microsoft Store packages are separate delivery formats. Build each from an equivalent **verified** application payload and test installation, uninstallation, and startup on the target system. Installer scripts and signing infrastructure are not included in this repository unless explicitly published.

For a WinGet manifest, verify that the published installer URL and SHA-256 match the **exact release asset**; installer technology, scope, and unattended command-line switches must match the actual installer. Do not publish untested silent-install switches.

Generated ZIPs, MSIX/AppxUpload files, signatures, local build caches, and machine-specific build output are not source files and should not be committed.

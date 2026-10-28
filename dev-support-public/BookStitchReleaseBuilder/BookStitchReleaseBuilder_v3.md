# BookStitch – Windows Release Builder

This tool creates a **self-contained Windows x64 portable folder**, a **portable ZIP**, a **classic Inno Setup 7 installer EXE**, and **SHA-256 checksums** from a local checkout of the BookStitch repository. It does not create Microsoft Store packages or upload releases.

## Quick start

1. On Windows, keep the launcher, PowerShell script, and `.iss` file together in this directory. The supplied installer script is `BookStitch-Installer-2.0-Public-Inno-7.1.0.iss`.
2. Double-click the supplied `BookStitchReleaseBuilder_v3_Start.bat`. It prefers **PowerShell 7** and falls back to **Windows PowerShell 5.1**; both use STA mode for Windows file/folder dialogs.
3. The builder checks for **Inno Setup 7.x** and offers to install it via **WinGet** if absent (approval and administrator access may be needed). You must also have the **.NET 10 SDK** installed; the builder checks for it but does not install it automatically.
4. If exactly one `.iss` file is alongside the launcher, it is selected automatically. If there are none or several, choose the script in the Windows file picker. The builder runs a **basic Inno preprocessor check**; complete Inno compilation happens later.
5. Choose the **repository root** (containing `BookStitch/BookStitch.csproj`) and an **output directory** using Windows folder pickers. The output directory must be outside the repository.
6. At the suffix prompt, **Enter means no suffix**; `FINAL` also means no suffix. Other values, such as `RC1`, are appended to the release filenames. Enter the **installer EXE name** and the **portable ZIP name** separately; **Enter accepts the displayed default** for each.
7. Review the selected paths and filenames, then confirm the build. Existing outputs are not overwritten.

## Reusing settings

The builder saves its settings next to the scripts in **`builder-settings.json`**: ISS path, repository, destination, suffix, setup filename, and ZIP filename. On subsequent starts it displays those values. Answering **Yes** reuses them, without asking again for either filename or the suffix; you still confirm the build itself. Answering **No** reopens the selection workflow. Older settings without filenames require one-time entry.

The public tool is supplied without a preconfigured `builder-settings.json`. A local settings file is created when settings are saved. Keep generated release artifacts outside the source repository.

## Outputs and safety

Typical filenames for version 2.1.7 with suffix `RC1`:

- `BookStitch-2.1.7-RC1/` – complete portable publish folder.
- `BookStitch-Portable-2.1.7-RC1.zip` – ZIP containing that top-level folder.
- `BookStitch-Setup-2.1.7-RC1.exe` – classic installer.
- `BookStitch-2.1.7-RC1-SHA256SUMS.txt` – hashes of the ZIP and installer.

The program version is read from `BookStitch/BookStitch.csproj`. The builder publishes the application once and packages both formats from that same output. It does **not** edit source code, delete existing output folders, overwrite existing artifacts, or publish to GitHub. If the selected output names already exist, choose a different destination or suffix/name instead.

The portable app includes its required .NET runtime, but **FFmpeg/FFprobe are not bundled by default**. A successful compile is not a substitute for installation, upgrade, uninstall, portable-startup, or security testing; review any NuGet dependency advisories before distribution.

## Requirements

- Windows x64; .NET **10 SDK**.
- Inno Setup **7.x** (installation via WinGet can be offered by the builder).
- Network connectivity if required build dependencies have not been restored already.

## Run directly from PowerShell

From this directory, instead of double-clicking the BAT:

```powershell
pwsh.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\BookStitchReleaseBuilder_v3.ps1
# Windows PowerShell 5.1 alternative:
powershell.exe -STA -NoProfile -ExecutionPolicy Bypass -File .\BookStitchReleaseBuilder_v3.ps1
```

For manual build details and Store/WinGet distinctions, see [`docs-public/release-build-notes.md`](../../docs-public/release-build-notes.md).

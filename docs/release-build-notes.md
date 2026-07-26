# Release build notes

This document describes important release-build details for BookStitch.
It is intentionally generic and does not contain private local paths.

## Release artifacts

Release artifacts should be created outside the Git repository.

Do not commit generated release outputs such as publish folders, installer output folders, setup executables, portable ZIP packages, local release scripts with private paths, or local Inno Setup scripts with private paths.

Recommended local artifact structure:

<external-release-root>/
  publish/
    BookStitch-<version>/
  installer/
  portable/

## Versioning

Release candidates may use versions such as 1.0.0-rc1, 1.0.0-rc2 or 1.0.0-rc3.
A final public release should use a stable version such as 1.0.0.

The application version is defined in BookStitch/BookStitch.csproj.

## Publish process

Before creating installer or portable ZIP artifacts, publish the application in Release configuration.

Typical steps:

dotnet clean .\BookStitch.slnx -c Release
dotnet build .\BookStitch.slnx -c Release
dotnet test .\BookStitch.slnx -c Release --no-build
dotnet publish .\BookStitch\BookStitch.csproj -c Release -o "<external-release-root>\publish\BookStitch-<version>"

## TagLibSharp runtime dependency

BookStitch uses TagLibSharp as a runtime dependency.
The publish output must contain TagLibSharp.dll.

On some machines, TagLibSharp.dll may be copied with Hidden and/or System file attributes.
This can make the file easy to miss during manual inspection and may cause packaging mistakes.

Before creating a portable ZIP or installer, verify that TagLibSharp.dll exists and remove Hidden/System attributes if necessary.

Example:

dir "<external-release-root>\publish\BookStitch-<version>\TagLibSharp.dll" -Force
attrib -H -S "<external-release-root>\publish\BookStitch-<version>\TagLibSharp.dll"
dir "<external-release-root>\publish\BookStitch-<version>\TagLibSharp.dll" -Force

Expected file mode after correction should not include Hidden or System attributes.

## Portable ZIP

A portable ZIP package can be created from the published application folder.
The ZIP should contain the application files directly or inside a clearly named top-level folder such as BookStitch-<version>/.
The portable ZIP should not contain build caches, source files, Git data or local release scripts.

## Installer

Installer creation is local release infrastructure.
If an Inno Setup script contains private local paths, it should live outside the Git repository.

Before compiling the installer, verify that the publish folder exists, BookStitch.exe exists, TagLibSharp.dll exists, TagLibSharp.dll is not Hidden/System, the installer output folder is outside the repository, and the generated setup filename matches the release version.

Example setup filename: BookStitch-Setup-<version>.exe

## Git hygiene

Before committing release-related source changes:

git status --short --branch
git diff --check
git diff --stat
git diff

Do not commit generated artifacts or private local release infrastructure.

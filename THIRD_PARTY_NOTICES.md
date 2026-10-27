# Third-party notices

BookStitch's own code is distributed under [GNU GPL v3.0 only](LICENSE). Dependencies and third-party material retain their own applicable licenses. This summary does **not** replace those licenses or their required notices; consult the linked upstream terms and the notices supplied with any distributed binary package.

## Application dependencies

The following versions are declared in `BookStitch/BookStitch.csproj` in the reviewed source snapshot; **recheck them against the final release build**.

| Component | Declared version | Use | License information |
| --- | --- | --- | --- |
| [TagLibSharp](https://www.nuget.org/packages/TagLibSharp) | `2.3.0` | Audio metadata reading and writing | See NuGet package and upstream license; verify applicable LGPL terms and bundled notices. |


Cover image handling uses the first-party CoverImageDrop libraries and Windows WPF/WIC imaging APIs.

## Bundled WebP decoder

`CoverCodecs/cover-webp-worker.exe` statically links the decoding portion of
[libwebp 1.6.0](https://developers.google.com/speed/webp/) under its BSD-style license
and accompanying patent grant. It also includes MinGW-w64 startup/runtime code
(ZPL 2.1 and component notices) and Zig compiler runtime code (MIT).
The required notices are shipped in `CoverCodecs/`: `libwebp-COPYING.txt`,
`libwebp-PATENTS.txt`, `MINGW-COPYING.txt`, and `ZIG-RUNTIME-LICENSE.txt`.
The worker source, exact upstream source archive, build script and SHA-256 provenance
are retained under `libraries/CoverImageDrop/native/`. No system-wide codec registration
or FFmpeg installation is required for cover decoding. This binary targets Windows x64.

## External audio tools

[FFmpeg and FFprobe](https://ffmpeg.org/legal.html) are used for audio analysis, conversion and export but are **not included in the standard BookStitch source or `EndUser` publish payload**. FFmpeg licensing varies with the binaries and build configuration (including LGPL/GPL and optional components). Anyone redistributing a specific FFmpeg build must provide its applicable notices, licenses and required source-code access independently.

## Development and test packages

The test projects use packages such as `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `coverlet.collector`. Consult each project's actual package references and upstream licenses. They are not automatically part of the application release payload.

## Project media and artwork

BookStitch-specific branding, icons and notification assets accompany this project. The applicable rights for any third-party-derived image, sound, recording or other asset must be checked **individually before redistribution**. Do not assume an asset is GPL-licensed merely because it appears in this repository.

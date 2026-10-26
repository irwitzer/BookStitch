# Third-party notices

BookStitch's own code is distributed under [GNU GPL v3.0 only](LICENSE). Dependencies and third-party material retain their own applicable licenses. This summary does **not** replace those licenses or their required notices; consult the linked upstream terms and the notices supplied with any distributed binary package.

## Application dependencies

The following versions are declared in `BookStitch/BookStitch.csproj` in the reviewed source snapshot; **recheck them against the final release build**.

| Component | Declared version | Use | License information |
| --- | --- | --- | --- |
| [TagLibSharp](https://www.nuget.org/packages/TagLibSharp) | `2.3.0` | Audio metadata reading and writing | See NuGet package and upstream license; verify applicable LGPL terms and bundled notices. |
| [SixLabors.ImageSharp](https://www.nuget.org/packages/SixLabors.ImageSharp) | `3.1.12` | Cover image handling | [Six Labors licensing](https://sixlabors.com/pricing/); verify applicable split-license rights for your distribution. |

**BookStitch 2 release decision:** ImageSharp remains pinned to `3.1.12`, with its known security advisories acknowledged. The maintainer has decided against a package update for this release because of licensing constraints. Replacement or revision of this dependency is deferred to BookStitch 3. This decision does not resolve the advisories or change the applicable license obligations.

## External audio tools

[FFmpeg and FFprobe](https://ffmpeg.org/legal.html) are used for audio analysis, conversion and export but are **not included in the standard BookStitch source or `EndUser` publish payload**. FFmpeg licensing varies with the binaries and build configuration (including LGPL/GPL and optional components). Anyone redistributing a specific FFmpeg build must provide its applicable notices, licenses and required source-code access independently.

## Development and test packages

The test projects use packages such as `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, and `coverlet.collector`. Consult each project's actual package references and upstream licenses. They are not automatically part of the application release payload.

## Project media and artwork

BookStitch-specific branding, icons and notification assets accompany this project. The applicable rights for any third-party-derived image, sound, recording or other asset must be checked **individually before redistribution**. Do not assume an asset is GPL-licensed merely because it appears in this repository.

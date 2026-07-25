# Third-Party Notices

This file summarizes third-party components and services used by BookStitch. BookStitch's own source code is licensed under GPL-3.0-only; third-party components remain under their own license terms.

This file is a notice summary, not a replacement for the original third-party license texts.

## Runtime dependencies

### TagLibSharp

- Package: `TagLibSharp`
- Version used by this project: `2.3.0`
- Purpose: Reading and writing audio metadata.
- License: `LGPL-2.1-only`
- Source / package information: NuGet package metadata and upstream project information.

### SixLabors.ImageSharp

- Package: `SixLabors.ImageSharp`
- Version used by this project: `3.1.12`
- Purpose: Cover image loading, processing and export preparation.
- License: Six Labors Split License, Version 1.0
- Source / package information: NuGet package metadata and upstream project information.

## External tools

### FFmpeg and FFprobe

BookStitch uses FFmpeg and FFprobe for audio probing, decoding, conversion, ripping and export workflows.

FFmpeg/FFprobe are external tools and are not part of BookStitch's own source code. Their license obligations depend on the exact FFmpeg build and configuration used or redistributed. Release packages that bundle FFmpeg/FFprobe must include the corresponding FFmpeg license information and comply with the license terms of the bundled build.


## Test and development dependencies

The test project uses additional development-time dependencies, including:

- `Microsoft.NET.Test.Sdk`
- `xunit`
- `xunit.runner.visualstudio`
- `coverlet.collector`

These packages are used for building and testing BookStitch and remain under their respective license terms.

## BookStitch project assets

The BookStitch-specific icons, logo and bundled WAV notification sounds were created for BookStitch. Unless otherwise stated, they are distributed together with BookStitch under the same GPL-3.0-only project license.

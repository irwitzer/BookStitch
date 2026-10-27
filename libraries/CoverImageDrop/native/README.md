# Bundled WebP worker (Windows x64)

`cover-webp-worker.exe` is a first-party C host statically linked with the decoder,
demuxer and supporting DSP/utilities of upstream libwebp 1.6.0. No encoder, WIC
registration, .NET runtime, FFmpeg or third-party DLL is needed by this worker.
The original source archive and notices are in `libwebp/`; provenance.json records
source and binary hashes. MinGW/Zig runtime notices ship with the executable.

## Rebuild

Use Zig 0.13.0's `zig.exe` and an external work directory:

```powershell
./Build-WebPWorker.ps1 -ZigExecutable <zig.exe> -WorkDirectory <external-directory>
```

The script verifies the upstream source hash before extraction. Review the resulting
binary and update its recorded hash after changes. Runtime packages include the
executable and notices, not the source archive or build toolchain.

## Protocol and bounds

One process handles one `inspect` or `decode` request. Stdin is bounded encoded WebP.
Stdout is little-endian uint32 magic CID1, width, height, frame count, ICC length,
EXIF length, then ICC/EXIF bytes and (decode only) straight-alpha BGRA pixels.
The decoder returns the first fully composited canvas, including animated inputs.
Native and managed boundaries independently check dimensions and output sizes.
Limits: 32 MiB input, 24 million pixels, 8192 per edge, 1 MiB per ICC/EXIF payload.
The worker applies a Windows Job process-memory limit of 384 MiB before parsing.
The parent serializes worker requests, kills failed/timed-out workers and imposes
15 seconds per request (including waiting for the worker gate). This is process
isolation, not a low-privilege security sandbox. Native parser updates remain required.

WebP bypasses installed WIC decoders for consistent behavior. EXIF orientation and
ICC data feed the same WPF orientation/sRGB/alpha stages as other image inputs.
Malformed ICC profiles produce a structured color-profile warning and sRGB assumption.

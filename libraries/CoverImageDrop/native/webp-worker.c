// BookStitch first-party WebP boundary. Protocol v1: stdin WebP, stdout CID1 header,
// ICC, EXIF, and (decode only) one fully composited straight-alpha BGRA canvas.
#include <windows.h>
#include <stdio.h>
#include <stdint.h>
#include <stdlib.h>
#include <string.h>
#include <fcntl.h>
#include <io.h>
#include "src/webp/decode.h"
#include "src/webp/demux.h"
#define MAX_BYTES (32u * 1024u * 1024u)
#define MAX_PIXELS 24000000u
#define MAX_META (1024u * 1024u)
static int write32(uint32_t v) { return fwrite(&v, 4, 1, stdout) == 1; }
static int write_chunk(const WebPDemuxer* demux, const char* tag, WebPChunkIterator* it) {
  memset(it, 0, sizeof(*it));
  if (!WebPDemuxGetChunk(demux, tag, 1, it)) return 1;
  return it->chunk.size <= MAX_META;
}
int main(int argc, char** argv) {
  if (argc != 2 || (strcmp(argv[1], "inspect") && strcmp(argv[1], "decode"))) return 2;
  SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
  HANDLE job = CreateJobObjectW(NULL, NULL);
  JOBOBJECT_EXTENDED_LIMIT_INFORMATION limits = {0};
  limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_PROCESS_MEMORY;
  limits.ProcessMemoryLimit = 384u * 1024u * 1024u;
  if (!job || !SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, sizeof(limits)) ||
      !AssignProcessToJobObject(job, GetCurrentProcess())) return 3;
  _setmode(_fileno(stdin), _O_BINARY); _setmode(_fileno(stdout), _O_BINARY);
  uint8_t* input = (uint8_t*)malloc(MAX_BYTES + 1u);
  if (!input) return 4;
  size_t length = fread(input, 1, MAX_BYTES + 1u, stdin);
  if (!length || length > MAX_BYTES || ferror(stdin)) return 5;
  WebPBitstreamFeatures features;
  if (WebPGetFeatures(input, length, &features) != VP8_STATUS_OK || features.width <= 0 || features.height <= 0 ||
      features.width > 8192 || features.height > 8192 || (uint64_t)features.width * features.height > MAX_PIXELS) return 6;
  WebPData data = {input, length};
  WebPDemuxer* demux = WebPDemux(&data);
  if (!demux) return 7;
  uint32_t w = WebPDemuxGetI(demux, WEBP_FF_CANVAS_WIDTH), h = WebPDemuxGetI(demux, WEBP_FF_CANVAS_HEIGHT);
  uint32_t frames = WebPDemuxGetI(demux, WEBP_FF_FRAME_COUNT);
  if (!w || !h || w > 8192 || h > 8192 || (uint64_t)w * h > MAX_PIXELS || !frames) return 8;
  WebPChunkIterator icc, exif;
  if (!write_chunk(demux, "ICCP", &icc) || !write_chunk(demux, "EXIF", &exif)) return 9;
  uint8_t* pixels = NULL;
  WebPAnimDecoder* decoder = NULL;
  if (!strcmp(argv[1], "decode")) {
    WebPAnimDecoderOptions options;
    if (!WebPAnimDecoderOptionsInit(&options)) return 10;
    options.color_mode = MODE_BGRA; options.use_threads = 0;
    decoder = WebPAnimDecoderNew(&data, &options);
    int timestamp;
    if (!decoder || !WebPAnimDecoderGetNext(decoder, &pixels, &timestamp)) return 11;
  }
  int ok = write32(0x31444943u) && write32(w) && write32(h) && write32(frames) &&
      write32((uint32_t)icc.chunk.size) && write32((uint32_t)exif.chunk.size);
  if (icc.chunk.size) ok = ok && fwrite(icc.chunk.bytes, 1, icc.chunk.size, stdout) == icc.chunk.size;
  if (exif.chunk.size) ok = ok && fwrite(exif.chunk.bytes, 1, exif.chunk.size, stdout) == exif.chunk.size;
  if (pixels) { size_t size = (size_t)w * h * 4; ok = ok && fwrite(pixels, 1, size, stdout) == size; }
  ok = ok && fflush(stdout) == 0;
  if (decoder) WebPAnimDecoderDelete(decoder);
  WebPDemuxReleaseChunkIterator(&icc); WebPDemuxReleaseChunkIterator(&exif);
  WebPDemuxDelete(demux); free(input); CloseHandle(job);
  return ok ? 0 : 12;
}

using BookStitch.Models;

namespace BookStitch.Services;

public sealed record ActiveChapterResult(
    string Mode,
    IReadOnlyList<EffectiveChapterInfo> RenderedTimeline,
    ChapterAutoEngineResult? EngineResult = null);

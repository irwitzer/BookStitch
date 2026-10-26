using BookStitch.Models;

namespace BookStitch.Services;

public sealed record AudioDiscRunPreparation(
    ExportPreset Preset,
    int ParallelJobs);

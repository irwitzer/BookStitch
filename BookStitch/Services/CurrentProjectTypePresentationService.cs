namespace BookStitch.Services;

public static class CurrentProjectTypePresentationService
{
    public static CurrentProjectTypePresentation Create(
        LoadedProjectStateSnapshot loadedProject,
        bool hasActiveAudioDiscProject,
        bool hasActiveMp3DiscProject,
        bool hasCurrentProjectWorkFolder)
    {
        if (loadedProject.IsAudioDisc || hasActiveAudioDiscProject)
        {
            return new CurrentProjectTypePresentation(
                CurrentProjectSourceKind.AudioDisc,
                UiSymbolResources.ProjectTypeAudioDisc);
        }

        if (loadedProject.IsMp3Disc || hasActiveMp3DiscProject)
        {
            return new CurrentProjectTypePresentation(
                CurrentProjectSourceKind.Mp3Disc,
                UiSymbolResources.ProjectTypeMp3Disc);
        }

        if (loadedProject.IsLocal || hasCurrentProjectWorkFolder)
        {
            return new CurrentProjectTypePresentation(
                CurrentProjectSourceKind.LocalFiles,
                UiSymbolResources.ProjectTypeLocalFiles);
        }

        return CurrentProjectTypePresentation.None;
    }
}

public enum CurrentProjectSourceKind
{
    None,
    LocalFiles,
    Mp3Disc,
    AudioDisc
}

public sealed record CurrentProjectTypePresentation(
    CurrentProjectSourceKind SourceKind,
    string Glyph)
{
    public static CurrentProjectTypePresentation None { get; } =
        new(CurrentProjectSourceKind.None, string.Empty);

    public bool IsVisible => SourceKind != CurrentProjectSourceKind.None;
}
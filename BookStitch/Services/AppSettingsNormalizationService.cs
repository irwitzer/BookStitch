using BookStitch.Models;

namespace BookStitch.Services;

public static class AppSettingsNormalizationService
{
    public static void Normalize(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.IncludeGenreLibraryTags && !settings.IncludeGenreCategories)
            settings.IncludeGenreLibraryTags = true;
        if (string.IsNullOrWhiteSpace(settings.DefaultGenre))
            settings.DefaultGenre = GenreListService.FallbackGenre;

        settings.DefaultSampleRateHz = SampleRateSettingsService.Normalize(settings.DefaultSampleRateHz);
        settings.UiLanguage = UiLanguageService.Normalize(settings.UiLanguage);
        settings.SuppressedWarnings = (settings.SuppressedWarnings ?? [])
            .Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        settings.DefaultFileNameTemplate = settings.DefaultFileNameTemplate ?? string.Empty;
        if (settings.TrackGridColumns is not null)
        {
            foreach (var column in settings.TrackGridColumns)
            {
                if (string.Equals(column.Key, "Kapitelvorschlag", StringComparison.Ordinal))
                    column.Key = "ChapterProposal";
            }
        }

        settings.ProjectRetentionDays =
            ProjectIndexService.NormalizeRetentionDays(settings.ProjectRetentionDays);
        settings.ProjectRetentionMode = ProjectRetentionPolicyService.NormalizeMode(settings.ProjectRetentionMode);
        if (string.IsNullOrWhiteSpace(settings.OutputFolder))
            settings.OutputFolder = OutputFolderFallbackService.GetDesktopFallbackFolder();
        settings.OutputFolderLayout =
            OutputFolderLayoutService.NormalizeLayout(settings.OutputFolderLayout);
        settings.OutputFolderPrimaryAction =
            OutputFolderPrimaryActionService.Normalize(settings.OutputFolderPrimaryAction);
        settings.ChapterSettings =
            ChapterSettingsService.Normalize(settings.ChapterSettings, legacyUseLeadingZeros: true);
        settings.MetadataPreferences ??= new MetadataPreferences();
        MetadataPreferencesService.Normalize(settings.MetadataPreferences);
        settings.EventProfile =
            EventProfileSettingsService.NormalizeProfile(settings.EventProfile).ToString();
        settings.SoundLibrary =
            SoundSettingsService.NormalizeLibrary(settings.SoundLibrary).ToString();
        settings.FocusProfile =
            FocusSettingsService.NormalizeProfile(settings.FocusProfile).ToString();
        settings.SoundVolumePercent =
            SoundSettingsService.NormalizeVolumePercent(settings.SoundVolumePercent);
        AppearanceSettingsService.Normalize(settings);
        CoverWarningSettingsService.Normalize(settings);
    }
}

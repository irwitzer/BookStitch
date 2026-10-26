using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AppSettingsNormalizationServiceTests
{
    [Theory]
    [InlineData("{}")]
    [InlineData("{\"SoundLibrary\":null,\"CoverWarningThresholdPixels\":-1,\"ChapterSettings\":null}")]
    [InlineData("{\"SoundLibrary\":\"invalid\",\"CoverWarningThresholdPixels\":123,\"ChapterSettings\":{\"Mode\":\"invalid\",\"NumberingStyle\":\"invalid\"}}")]
    public void MissingOrInvalidSettings_UseBassLargeCoverWarningAndPaddedAutoChapters(string json)
    {
        var settings = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(json)!;
        AppSettingsNormalizationService.Normalize(settings);
        Assert.Equal("Bass", settings.SoundLibrary);
        Assert.Equal(1000, settings.CoverWarningThresholdPixels);
        Assert.Equal("Auto", settings.ChapterSettings!.Mode);
        Assert.Equal("ZeroPadded", settings.ChapterSettings.NumberingStyle);
    }

    [Fact]
    public void Empty_genre_group_configuration_recovers_public_tags_without_changing_existing_values()
    {
        var settings = new AppSettings
        {
            IncludeGenreLibraryTags = false, IncludeGenreCategories = false,
            DefaultGenre = "Mein Genre", UsePrivateGenreList = true, GenreListAlwaysEnglish = true
        };
        AppSettingsNormalizationService.Normalize(settings);
        Assert.True(settings.IncludeGenreLibraryTags);
        Assert.False(settings.IncludeGenreCategories);
        Assert.True(settings.GenreListAlwaysEnglish);
        Assert.True(settings.UsePrivateGenreList);
        Assert.Equal("Mein Genre", settings.DefaultGenre);
        settings.DefaultGenre = "";
        AppSettingsNormalizationService.Normalize(settings);
        Assert.Equal("Audiobook", settings.DefaultGenre);
    }

    [Fact]
    public void Normalize_PreservesExplicitSoundCoverAndChapterPreferences()
    {
        var settings = new AppSettings
        {
            SoundLibrary = "Gentle", CoverWarningThresholdPixels = 500,
            ChapterSettings = new ChapterSettings { Mode = "Custom", NumberingStyle = "Plain" }
        };
        AppSettingsNormalizationService.Normalize(settings);
        Assert.Equal("Gentle", settings.SoundLibrary);
        Assert.Equal(500, settings.CoverWarningThresholdPixels);
        Assert.Equal("Custom", settings.ChapterSettings.Mode);
        Assert.Equal("Plain", settings.ChapterSettings.NumberingStyle);
    }

    [Fact]
    public void Normalize_AppliesAllExistingSettingsPolicies()
    {
        var settings = new AppSettings
        {
            ProjectRetentionDays = 999,
            ProjectRetentionMode = "invalid",
            OutputFolderLayout = "invalid",
            OutputFolderPrimaryAction = "invalid",
            EventProfile = "invalid",
            FocusProfile = "invalid",
            SoundLibrary = "invalid",
            SoundVolumePercent = 500,
            CoverWarningThresholdPixels = 1234,
            BackgroundPreset = "invalid",
            TextColorPreset = "invalid"
        };

        AppSettingsNormalizationService.Normalize(settings);

        Assert.Equal(360, settings.ProjectRetentionDays);
        Assert.Equal(ProjectRetentionModes.Days, settings.ProjectRetentionMode);
        Assert.Equal(
            OutputFolderLayoutService.NormalizeLayout("invalid"),
            settings.OutputFolderLayout);
        Assert.Equal(OutputFolderPrimaryActionService.DefaultAction, settings.OutputFolderPrimaryAction);
        Assert.Equal(
            EventProfileSettingsService.NormalizeProfile("invalid").ToString(),
            settings.EventProfile);
        Assert.Equal(
            SoundSettingsService.NormalizeLibrary("invalid").ToString(),
            settings.SoundLibrary);
        Assert.Equal(
            FocusSettingsService.NormalizeProfile("invalid").ToString(),
            settings.FocusProfile);
        Assert.Equal(
            SoundSettingsService.NormalizeVolumePercent(500),
            settings.SoundVolumePercent);
        Assert.Equal(CoverWarningSettingsService.DefaultThresholdPixels, settings.CoverWarningThresholdPixels);
        Assert.Equal(AppearanceSettingsService.DefaultBackgroundPreset, settings.BackgroundPreset);
        Assert.Equal(AppearanceSettingsService.DefaultTextColorPreset, settings.TextColorPreset);
    }

    [Fact]
    public void Normalize_RejectsNullSettings()
    {
        Assert.Throws<ArgumentNullException>(
            () => AppSettingsNormalizationService.Normalize(null!));
    }

    [Fact]
    public void Normalize_PreservesLegacyChapterProposalColumnLayout()
    {
        var settings = new AppSettings
        {
            TrackGridColumns = [new TrackGridColumnLayoutItem
            {
                Key = "Kapitelvorschlag", DisplayIndex = 4, IsVisible = false, Width = 175
            }]
        };

        AppSettingsNormalizationService.Normalize(settings);

        var column = Assert.Single(settings.TrackGridColumns);
        Assert.Equal("ChapterProposal", column.Key);
        Assert.Equal(4, column.DisplayIndex);
        Assert.False(column.IsVisible);
        Assert.Equal(175, column.Width);
    }

    [Fact]
    public void Normalize_FreshChapterSettingsDefaultToAutoPlainAndSamplePreview()
    {
        var settings = new AppSettings();

        AppSettingsNormalizationService.Normalize(settings);

        Assert.Equal("Auto", settings.ChapterSettings!.Mode);
        Assert.Equal("ZeroPadded", settings.ChapterSettings.NumberingStyle);
        Assert.Equal("Sample", settings.ChapterSettings.PreviewDataSource);
    }

    [Fact]
    public void Normalize_MetadataPreferences_PreservesOrderAndRepairsLocksAndPolicies()
    {
        var settings = new AppSettings
        {
            MetadataPreferences = new MetadataPreferences
            {
                PreferredGenres = [" Krimi ", "", "krimi", "Hörbuch"],
                GenrePreferenceLocked = true,
                PreferredLanguages = [" de_ch ", "invalid language", "en-US"],
                LanguagePreferenceLocked = true,
                OutputPolicies = new Dictionary<string, OutputFieldPolicy>
                {
                    [ProjectMetadataField.Genres.ToString()] = OutputFieldPolicy.Disabled,
                    ["OriginalReleaseDate"] = OutputFieldPolicy.Disabled,
                    ["PublicationDate"] = OutputFieldPolicy.Disabled,
                    ["FutureField"] = OutputFieldPolicy.Disabled
                }
            }
        };

        AppSettingsNormalizationService.Normalize(settings);

        Assert.Equal(["Krimi", "Hörbuch"], settings.MetadataPreferences.PreferredGenres);
        Assert.Equal(["de-CH", "en-US"], settings.MetadataPreferences.PreferredLanguages);
        Assert.True(settings.MetadataPreferences.GenrePreferenceLocked);
        Assert.True(settings.MetadataPreferences.LanguagePreferenceLocked);
        Assert.Equal(OutputFieldPolicy.Disabled,
            MetadataPreferencesService.GetPolicy(settings.MetadataPreferences, ProjectMetadataField.Genres));
        Assert.DoesNotContain("FutureField", settings.MetadataPreferences.OutputPolicies.Keys);
        Assert.DoesNotContain("OriginalReleaseDate", settings.MetadataPreferences.OutputPolicies.Keys);
        Assert.DoesNotContain("PublicationDate", settings.MetadataPreferences.OutputPolicies.Keys);
    }

    [Fact]
    public void Normalize_InvalidChapterSettingsFallbackToSafeDefaults()
    {
        var settings = new AppSettings
        {
            ChapterSettings = new ChapterSettings
            {
                Mode = "Unfug",
                NumberingStyle = "Unfug",
                PreviewDataSource = "Unfug",
                Slot1 = "Unfug",
                Separator1 = "Unfug"
            }
        };

        AppSettingsNormalizationService.Normalize(settings);

        Assert.Equal("Auto", settings.ChapterSettings!.Mode);
        Assert.Equal("ZeroPadded", settings.ChapterSettings.NumberingStyle);
        Assert.Equal("Sample", settings.ChapterSettings.PreviewDataSource);
        Assert.Equal("Empty", settings.ChapterSettings.Slot1);
        Assert.Equal("Dash", settings.ChapterSettings.Separator1);
    }

    [Fact]
    public void Normalize_PreservesLanguageNeutralChapterSettingIds()
    {
        var settings = new AppSettings
        {
            ChapterSettings = new ChapterSettings
            {
                Mode = "Custom",
                NumberingStyle = "SpacePadded",
                PreviewDataSource = "CurrentProject",
                Slot1 = "BookTitle",
                Separator1 = "Dash",
                Slot2 = "Number",
                Separator2 = "Pipe",
                Slot3 = "OriginalTagTitle"
            }
        };

        AppSettingsNormalizationService.Normalize(settings);

        Assert.Equal("Custom", settings.ChapterSettings!.Mode);
        Assert.Equal("SpacePadded", settings.ChapterSettings.NumberingStyle);
        Assert.Equal("CurrentProject", settings.ChapterSettings.PreviewDataSource);
        Assert.Equal("BookTitle", settings.ChapterSettings.Slot1);
        Assert.Equal("Dash", settings.ChapterSettings.Separator1);
        Assert.Equal("Number", settings.ChapterSettings.Slot2);
        Assert.Equal("Pipe", settings.ChapterSettings.Separator2);
        Assert.Equal("OriginalTagTitle", settings.ChapterSettings.Slot3);
    }

    [Fact]
    public void Normalize_NoNumberingReplacesOnlyNumberSlotsWithEmpty()
    {
        var settings = new AppSettings
        {
            ChapterSettings = new ChapterSettings
            {
                Mode = ChapterSettingsService.ModeCustom,
                NumberingStyle = ChapterNumberingStyle.None.ToString(),
                Slot1 = "Number",
                Separator1 = "Dash",
                Slot2 = "BookTitle",
                Separator2 = "Pipe",
                Slot3 = "Number"
            }
        };

        AppSettingsNormalizationService.Normalize(settings);

        Assert.Equal("Empty", settings.ChapterSettings!.Slot1);
        Assert.Equal("BookTitle", settings.ChapterSettings.Slot2);
        Assert.Equal("Empty", settings.ChapterSettings.Slot3);
        Assert.Equal("Dash", settings.ChapterSettings.Separator1);
        Assert.Equal("Pipe", settings.ChapterSettings.Separator2);
    }

}

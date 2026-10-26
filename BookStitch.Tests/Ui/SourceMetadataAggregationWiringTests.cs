using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class SourceMetadataAggregationWiringTests
{
    private static readonly string MainWindowSource = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void NewSourceReset_AppliesLockedGlobalPreferencesBeforeNotifyingMetadataEditor()
    {
        var reset = SourceContract.ExtractMember(MainWindowSource, "private void ResetMetadataForNewDiscProject()");
        SourceContract.AssertInOrder(reset,
            "_projectMetadata.Clear", "_projectMetadataSession.BeginNew",
            "ProjectMetadataPreviewAggregationService.Apply", "ProjectMetadataInitializationState.InitialSetOpen",
            "_developmentMetadataDialog?.NotifyProjectChanged()");
    }

    [Theory]
    [InlineData("private ProjectMetadataInitializationOptions CreateMp3DiscProjectAggregationOptions()")]
    [InlineData("private ProjectMetadataInitializationOptions CreateAudioDiscProjectAggregationOptions()")]
    [InlineData("private ProjectMetadataInitializationOptions CreateLocalFolderAggregationOptions(")]
    public void EveryImportKindPassesPersistedLanguageAndWorkTypeLocksToCanonicalAggregation(string signature)
    {
        var options = SourceContract.ExtractMember(MainWindowSource, signature);
        Assert.Contains("PreferredLanguages: _settings.MetadataPreferences.PreferredLanguages", options);
        Assert.Contains("IsLanguagePreferenceFixed: _settings.MetadataPreferences.LanguagePreferenceLocked", options);
        Assert.Contains("PreferredAudioWorkType: _settings.MetadataPreferences.PreferredAudioWorkType", options);
        Assert.Contains("IsAudioWorkTypePreferenceFixed: _settings.MetadataPreferences.AudioWorkTypePreferenceLocked", options);
    }

    [Fact]
    public void Resume_aggregates_initialized_metadata_with_options_for_the_loaded_project_kind()
    {
        var resume = SourceContract.ExtractMember(
            MainWindowSource,
            "private async Task LoadResumeProjectIntoWorkspaceAsync(ProjectResumePlan plan)");

        var aggregation = SourceContract.ExtractBetween(
            resume,
            "ApplySourceMetadataAggregation(",
            "MeasureResumePhase(\"resume.cover\"");

        Assert.Contains("ProjectMetadataInitializationState.ProjectMetadataInitialized", aggregation, StringComparison.Ordinal);
        Assert.Contains("CreateLocalFolderAggregationOptions(loadResult.SelectedFolder)", aggregation, StringComparison.Ordinal);
        Assert.Contains("CreateMp3DiscProjectAggregationOptions()", aggregation, StringComparison.Ordinal);
        Assert.Contains("CreateAudioDiscProjectAggregationOptions()", aggregation, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "private async Task AddMoreLocalSourcesAsync()",
        "ProjectMetadataInitializationState.SubsequentSource",
        "CreateLocalFolderAggregationOptions(scanRoot, candidates.Select(item => item.SourcePath).ToArray())")]
    [InlineData(
        "private async Task<bool> ImportMp3DiscProjectAsync(",
        "ProjectMetadataInitializationState.InitialSetComplete",
        "CreateMp3DiscProjectAggregationOptions()")]
    [InlineData(
        "private async Task RunPreparedAudioDiscRipAsync(",
        "ProjectMetadataInitializationState.InitialSetComplete",
        "CreateAudioDiscProjectAggregationOptions()")]
    [InlineData(
        "private async Task<bool> PrepareCurrentLocalProjectSourcesAsync()",
        "ProjectMetadataInitializationState.InitialSetComplete",
        "CreateLocalFolderAggregationOptions(sourceFolder, sourceFiles)")]
    public void Completed_source_imports_use_the_expected_aggregation_state_and_options(
        string memberSignature,
        string state,
        string options)
    {
        var workflow = SourceContract.ExtractMember(MainWindowSource, memberSignature);

        var callStart = workflow.IndexOf("ApplySourceMetadataAggregation(", StringComparison.Ordinal);
        Assert.True(callStart >= 0, $"Aggregation call not found in {memberSignature}.");
        var optionsIndex = workflow.IndexOf(options, callStart, StringComparison.Ordinal);
        Assert.True(optionsIndex >= 0, $"Aggregation options not found in {memberSignature}: {options}");

        var aggregationCall = workflow[callStart..(optionsIndex + options.Length)];
        Assert.Contains(state, aggregationCall, StringComparison.Ordinal);
    }

    [Fact]
    public void Aggregation_logs_only_actionable_identity_and_majority_conflicts()
    {
        var aggregation = SourceContract.ExtractMember(MainWindowSource, "private void ApplySourceMetadataAggregation(");

        Assert.Contains("MetadataAggregationDiagnosticCode.StrongIdentityConflict", aggregation, StringComparison.Ordinal);
        Assert.Contains("MetadataAggregationDiagnosticCode.AmbiguousMajority", aggregation, StringComparison.Ordinal);
        Assert.Contains("WriteApplicationEvent(", aggregation, StringComparison.Ordinal);
    }
}

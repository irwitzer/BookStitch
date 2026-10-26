using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class MetadataProjectionWiringTests
{
    private static readonly string MainWindowSource = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);

    [Fact]
    public void Series_summary_reads_the_first_named_canonical_series_entry()
    {
        var series = SourceContract.ExtractMember(MainWindowSource, "public string Series");

        Assert.Contains("_projectMetadata.CanonicalMetadata", series, StringComparison.Ordinal);
        Assert.Contains("ProjectMetadataField.Series", series, StringComparison.Ordinal);
        Assert.Contains("FirstOrDefault(item => !string.IsNullOrWhiteSpace(item.Name))", series, StringComparison.Ordinal);
    }

    [Fact]
    public void Series_changes_refresh_the_summary_and_output_name()
    {
        var aggregation = SourceContract.ExtractMember(MainWindowSource, "private void ApplySourceMetadataAggregation(");
        var refresh = SourceContract.ExtractMember(MainWindowSource, "private void RefreshMetadataDependentUi()");

        SourceContract.AssertInOrder(
            aggregation,
            "result.AppliedFields.Contains(ProjectMetadataField.Series)",
            "OnPropertyChanged(nameof(Series));",
            "OnPropertyChanged(nameof(OutputFileNamePreview));");
        Assert.Contains("OnPropertyChanged(nameof(Series));", refresh, StringComparison.Ordinal);
        Assert.Contains("OnPropertyChanged(nameof(OutputFileNamePreview));", refresh, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("public string OutputFileNamePreview =>")]
    [InlineData("private string BuildFinalOutputPath()")]
    public void Output_naming_receives_the_current_series_projection(string memberSignature)
    {
        var naming = SourceContract.ExtractMember(MainWindowSource, memberSignature);

        Assert.Contains("Series", naming, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_metadata_summary_remains_a_canonical_projection_after_editor_cleanup()
    {
        var summary = SourceContract.ExtractMember(MainWindowSource, "private MetadataSummaryPresentation MetadataSummaryPresentation =>");
        Assert.Contains("MetadataSummaryPresentationService.Create(", summary, StringComparison.Ordinal);
        foreach (var property in new[] { "BookTitle", "Author", "Narrator", "Album", "Genre" })
            Assert.Contains(property, summary, StringComparison.Ordinal);

        var xaml = RepositorySource.LoadXaml(["BookStitch", "MainWindow.xaml"]);
        Assert.Contains(xaml.Descendants(), element => element.Name.LocalName == "TextBlock" && element.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value == "{Binding MetadataSummaryTitle}"));
        Assert.Contains(xaml.Descendants(), element => element.Name.LocalName == "TextBlock" && element.Attributes().Any(a => a.Name.LocalName == "Text" && a.Value == "{Binding MetadataSummaryDetails}"));
    }

    [Fact]
    public void Compatibility_preview_projects_current_metadata_without_touching_the_session()
    {
        var preview = SourceContract.ExtractMember(
            MainWindowSource,
            "private FinalAudioTagData CreateCurrentCompatibilityPreviewTagData()");

        Assert.Contains("_projectMetadata.CanonicalMetadata", preview, StringComparison.Ordinal);
        Assert.Contains("ProjectMetadataOutputProjectionService().Create(", preview, StringComparison.Ordinal);
        Assert.DoesNotContain("_projectMetadataSession.", preview, StringComparison.Ordinal);
    }
}

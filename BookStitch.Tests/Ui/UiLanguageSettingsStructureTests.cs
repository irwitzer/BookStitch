using System.Xml.Linq;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class UiLanguageSettingsStructureTests
{
    [Fact]
    public void LanguageSettingsSelectsAndSavesStableLanguageId()
    {
        var xaml = RepositorySource.LoadXaml(["BookStitch", "Controls", "SettingsLanguageControl.xaml"]);
        var combo = xaml.Descendants().Single(element =>
            element.Name.LocalName == "ComboBox" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "UiLanguageComboBox"));
        Assert.Equal("UiLanguageComboBox_Changed", combo.Attribute("SelectionChanged")?.Value);
        Assert.Equal("{StaticResource AppearanceCompactComboBoxStyle}", combo.Attribute("Style")?.Value);
        var restartButton = xaml.Descendants().Single(element =>
            element.Name.LocalName == "Button" &&
            element.Attributes().Any(attribute => attribute.Name.LocalName == "Name" && attribute.Value == "UiLanguageRestartButton"));
        Assert.Equal("{StaticResource SettingsSecondaryButtonStyle}", restartButton.Attribute("Style")?.Value);
        Assert.Equal("UiLanguageRestartButton_Click", restartButton.Attribute("Click")?.Value);

        var code = RepositorySource.Read(["BookStitch", "Controls", "SettingsLanguageControl.xaml.cs"]);
        Assert.Contains("UiLanguageService.Choices", code, StringComparison.Ordinal);
        Assert.Contains("choice.Id == UiLanguageService.Normalize(Settings.UiLanguage)", code, StringComparison.Ordinal);
        Assert.Contains("Settings.UiLanguage = choice.Id", code, StringComparison.Ordinal);
        Assert.Contains("UiLanguageRestartButton.Visibility = restartNeeded ? Visibility.Visible : Visibility.Collapsed", code,
            StringComparison.Ordinal);
        Assert.Contains("app.AppliedUiLanguageId", code, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyAtStartup", SourceContract.ExtractMember(code,
            "private void UiLanguageComboBox_Changed(object sender, SelectionChangedEventArgs e)"), StringComparison.Ordinal);
        Assert.Contains("_requestApplicationRestart?.Invoke()", code, StringComparison.Ordinal);
        Assert.Contains("requestApplicationRestart: RequestApplicationRestart",
            RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]), StringComparison.Ordinal);
    }

    [Fact]
    public void TrackGridChapterColumnUsesTechnicalKey()
    {
        var xaml = RepositorySource.Read(["BookStitch", "MainWindow.xaml"]);
        var code = RepositorySource.Read(["BookStitch", "MainWindow.xaml.cs"]);
        Assert.Contains("local:TrackGridChildColumn.Key=\"ChapterProposal\"", xaml, StringComparison.Ordinal);
        Assert.Contains("GetTrackGridColumnKey(column), \"ChapterProposal\"", code, StringComparison.Ordinal);
        Assert.DoesNotContain("column.Header?.ToString(), \"Kapitelvorschlag\"", code, StringComparison.Ordinal);
    }
}

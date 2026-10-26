using System.Xml.Linq;
using BookStitch.Models;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class NotificationSettingsStructureTests
{
    [Theory]
    [InlineData("BookStitch/Controls/SettingsGeneralControl.xaml", "EventProfileComboBox", "SoundLibraryComboBox")]
    [InlineData("BookStitch/Dialog/DialogPreviewWindow.xaml", "PreviewEventProfileComboBox", "PreviewSoundLibraryComboBox")]
    public void Notification_selectors_expose_all_event_profiles_and_sound_libraries(
        string relativePath,
        string eventProfileName,
        string soundLibraryName)
    {
        var document = RepositorySource.LoadXaml(relativePath.Split('/'));
        var eventProfile = ComboBox(document, eventProfileName);
        var soundLibrary = ComboBox(document, soundLibraryName);

        Assert.Equal(Enum.GetNames<EventProfile>(), OptionTags(eventProfile));
        Assert.Equal(Enum.GetNames<SoundLibrary>(), OptionTags(soundLibrary));
        Assert.All(
            eventProfile.Elements().Concat(soundLibrary.Elements()),
            item => Assert.False(string.IsNullOrWhiteSpace(Attribute(item, "Content"))));

        if (relativePath.EndsWith("DialogPreviewWindow.xaml", StringComparison.Ordinal))
        {
            Assert.Equal("{StaticResource LargeResizableWindowStyle}", Attribute(document.Root!, "Style"));
            Assert.Contains(document.Descendants(), element =>
                element.Name.LocalName == "LargeResizableWindowHeader" &&
                Attribute(element, "CloseRequested") == "CloseButton_Click");
        }
    }

    [Fact]
    public void Settings_volume_changes_use_the_debounced_global_settings_owner()
    {
        var code = RepositorySource.Read(["BookStitch", "Controls", "SettingsGeneralControl.xaml.cs"]);
        var handler = SourceContract.ExtractMember(code, "private void SoundVolumeSlider_ValueChanged");

        SourceContract.AssertInOrder(
            handler,
            "Settings.SoundVolumePercent =",
            "_settingsManager!.SaveDebounced();",
            "UpdateSoundVolumeText();");
        Assert.DoesNotContain("SettingsService.Save", code, StringComparison.Ordinal);
    }

    private static XElement ComboBox(XContainer document, string name) =>
        document.Descendants().Single(element =>
            element.Name.LocalName == "ComboBox" && Attribute(element, "Name") == name);

    private static string[] OptionTags(XElement comboBox) =>
        comboBox.Elements()
            .Where(element => element.Name.LocalName == "ComboBoxItem")
            .Select(element => Attribute(element, "Tag"))
            .ToArray();

    private static string Attribute(XElement element, string name) =>
        element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? string.Empty;
}

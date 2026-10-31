using BookStitch.Dialog;
using BookStitch.Services;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BookStitch.Controls;

public partial class SettingsInfoControl : UserControl
{
    private const string GitHubProjectUrl = "https://github.com/irwitzer/BookStitch";
    private const string ThirdPartyNoticesUrl = "https://github.com/irwitzer/BookStitch/blob/main/THIRD_PARTY_NOTICES.md";

    public SettingsInfoControl()
    {
        InitializeComponent();
    }

    private void InfoSidebarImage_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var radius = Math.Max(0, InfoSidebarBorder.CornerRadius.TopLeft - InfoSidebarBorder.BorderThickness.Left);
        InfoSidebarImage.Clip = new RectangleGeometry(
            new Rect(0, 0, InfoSidebarImage.ActualWidth, InfoSidebarImage.ActualHeight), radius, radius);
    }

    public void Initialize()
    {
        InfoVersionTextBlock.Text = LocalizationText.Format("Settings.Info.Version", GetDisplayVersion());
    }

    private void DeveloperName_Click(object sender, RoutedEventArgs e) =>
        DeveloperToolsCommands.AccessState.RegisterDiscoveryClick();

    private void Website_RequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        OpenUrl(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    private void OpenGitHubProject_Click(object sender, RoutedEventArgs e) => OpenUrl(GitHubProjectUrl);

    private void OpenThirdPartyNotices_Click(object sender, RoutedEventArgs e) => OpenUrl(ThirdPartyNoticesUrl);

    private void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            AppDialogService.Show(
                Window.GetWindow(this),
                LocalizationText.Get("Settings.Info.OpenLink.Title"),
                LocalizationText.Get("Settings.Info.OpenLink.Heading"),
                LocalizationText.Format("Settings.Info.OpenLink.Message", url, exception.Message),
                AppDialogKind.Error,
                null,
                [new AppDialogButton(LocalizationText.Get("Common.OK"), AppDialogResult.Ok, IsPrimary: true, IsDefault: true)]);
        }
    }

    private static string GetDisplayVersion()
    {
        var assembly = typeof(SettingsInfoControl).Assembly;
        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;

        return !string.IsNullOrWhiteSpace(informationalVersion)
            ? informationalVersion.Split('+')[0]
            : assembly.GetName().Version?.ToString() ?? LocalizationText.Get("Settings.Info.UnknownVersion");
    }
}

using BookStitch.Dialog;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace BookStitch.Services;

/// <summary>One warning and persisted preference for every managed project-folder entry point.</summary>
public static class ProjectFolderOpenService
{
    public static bool IsManagedFolder(string folder)
    {
        for(var current=Path.GetFullPath(folder);!string.IsNullOrEmpty(current);current=Path.GetDirectoryName(current))
            if(File.Exists(ProjectFolderLayout.GetFolderManifestPath(current)) ||
               File.Exists(ProjectFolderLayout.GetMp3DiscManifestPath(current)) ||
               File.Exists(ProjectFolderLayout.GetAudioDiscManifestPath(current)))return true;
        return false;
    }

    public static void Open(Window owner, string projectFolder, GlobalSettingsManager? settingsManager)
        => Open(owner, projectFolder, settingsManager,
            path => Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true }));

    internal static void Open(Window owner, string projectFolder, GlobalSettingsManager? settingsManager, Action<string> launch)
    {
        if (settingsManager is null || !WarningSuppressionService.IsSuppressed(settingsManager.Current, WarningSuppressionService.ProjectFolder))
        {
            var dialog = new AppMessageDialog(LocalizationText.Get("Resume.Project.OpenFolder"),
                LocalizationText.Get("ProjectFolder.Warning.Title"), LocalizationText.Get("ProjectFolder.Warning.Message"),
                AppDialogKind.Warning, null,
                [new(LocalizationText.Get("Resume.Project.OpenFolder"), AppDialogResult.Yes, IsPrimary: true),
                 new(LocalizationText.Get("Common.Cancel"), AppDialogResult.Cancel, IsDefault: true, IsCancel: true)]);
            var toggle = new CheckBox
            {
                Content = LocalizationText.Get("Warnings.AlwaysWarn"), IsChecked = true,
                Style = (Style)dialog.FindResource("WarningToggleStyle"), Margin = new Thickness(0, 8, 0, 0)
            };
            AutomationProperties.SetName(toggle, LocalizationText.Get("Warnings.AlwaysWarn"));
            dialog.AdditionalContent.Content = toggle;
            dialog.AdditionalContent.Visibility = Visibility.Visible;
            ModalDialogService.ShowDialog(dialog, owner);
            if (dialog.Result != AppDialogResult.Yes) return;
            if (settingsManager is not null)
            {
                WarningSuppressionService.SetSuppressed(settingsManager.Current, WarningSuppressionService.ProjectFolder, toggle.IsChecked != true);
                settingsManager.SaveImmediate();
            }
        }
        try { launch(projectFolder); }
        catch (Exception ex) { AppDialogService.Error(owner, LocalizationText.Get("Resume.Project.OpenFolderFailed"), LocalizationText.Get("Common.OperationFailed"), [projectFolder, ex.Message]); }
    }
}

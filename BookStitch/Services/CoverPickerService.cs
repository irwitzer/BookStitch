using BookStitch.Dialog;
using BookStitch.Models;
using Microsoft.Win32;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;

namespace BookStitch.Services;

public sealed class CoverPickerService : INotifyPropertyChanged
{
    public static string ImageFileFilter => LocalizationText.Get("CoverPicker.ImageFilter");

    private readonly AppSettings _settings;
    private readonly Action _saveSettings;
    private readonly Func<string?> _workingRootProvider;

    public CoverPickerService(
        AppSettings settings,
        Action saveSettings,
        Func<string?> workingRootProvider)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _saveSettings = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));
        _workingRootProvider = workingRootProvider ?? throw new ArgumentNullException(nameof(workingRootProvider));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool RememberLastCoverPickerFolder
    {
        get => _settings.RememberLastCoverPickerFolder;
        set
        {
            if (_settings.RememberLastCoverPickerFolder == value)
                return;

            _settings.RememberLastCoverPickerFolder = value;
            _saveSettings();
            OnPropertyChanged();
        }
    }

    public string? LastUserSelectedCoverFolder => _settings.LastUserSelectedCoverFolder;

    public string? ChooseCoverFile(Window owner, CoverPickerContext context)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var dialog = new OpenFileDialog
        {
            Title = LocalizationText.Get("CoverPicker.Title"),
            Filter = ImageFileFilter,
            Multiselect = false,
            InitialDirectory = ResolveInitialDirectory(context)
        };

        if (dialog.ShowDialog(owner) != true)
            return null;

        RecordSuccessfulPickerSelection(dialog.FileName);
        return dialog.FileName;
    }

    public string ResolveInitialDirectory(CoverPickerContext context)
    {
        if (RememberLastCoverPickerFolder && IsValidExternalFolder(LastUserSelectedCoverFolder))
            return LastUserSelectedCoverFolder!;

        if (context.SourceKind == ProjectSetupSourceKind.Folder && IsValidExternalFolder(context.SourceFolder))
            return context.SourceFolder!;

        if (IsValidExternalFolder(context.ExistingCoverSourcePath is { Length: > 0 }
                ? Path.GetDirectoryName(context.ExistingCoverSourcePath)
                : null))
        {
            return Path.GetDirectoryName(context.ExistingCoverSourcePath)!;
        }

        if (IsValidExternalFolder(context.LegacyFallbackFolder))
            return context.LegacyFallbackFolder!;

        return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
    }

    public void RecordSuccessfulPickerSelection(string selectedFilePath)
    {
        if (!RememberLastCoverPickerFolder || string.IsNullOrWhiteSpace(selectedFilePath))
            return;

        var folder = Path.GetDirectoryName(selectedFilePath);
        if (!IsValidExternalFolder(folder) || string.Equals(folder, _settings.LastUserSelectedCoverFolder, StringComparison.OrdinalIgnoreCase))
            return;

        _settings.LastUserSelectedCoverFolder = folder;
        _saveSettings();
        OnPropertyChanged(nameof(LastUserSelectedCoverFolder));
    }

    public bool IsValidExternalFolder(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath) || IsOpticalDrivePath(folderPath))
            return false;

        var workingRoot = _workingRootProvider();
        if (string.IsNullOrWhiteSpace(workingRoot))
            return true;

        try
        {
            var root = Path.GetFullPath(workingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(folderPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var prefix = root + Path.DirectorySeparatorChar;
            return !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase) &&
                   !candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsOpticalDrivePath(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrWhiteSpace(root))
                return false;

            var drive = DriveInfo.GetDrives().FirstOrDefault(candidate =>
                string.Equals(candidate.Name, root, StringComparison.OrdinalIgnoreCase));
            return drive?.DriveType == DriveType.CDRom;
        }
        catch
        {
            return false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

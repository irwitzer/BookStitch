using System;
using System.IO;
using System.Linq;
using Xunit;

namespace BookStitch.Tests.Ui;

public sealed class GenreSettingsStructureTests
{
    [Fact]
    public void Settings_private_genre_toggle_resets_default_genre_and_main_window_selection()
    {
        var source = File.ReadAllText(FindRepositoryFile("BookStitch", "Windows", "SettingsWindow.Genres.cs"));

        Assert.Contains("_settings.DefaultGenre = GenreListService.GetDefaultGenre(_settings.UsePrivateGenreList);", source, StringComparison.Ordinal);
        Assert.Contains("mainWindow.RefreshGenreOptions(resetToDefault: true);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Main_window_genre_refresh_supports_explicit_default_reset()
    {
        var source = File.ReadAllText(FindRepositoryFile("BookStitch", "MainWindow.Genres.cs"));

        Assert.Contains("RefreshGenreOptions(bool resetToDefault = false)", source, StringComparison.Ordinal);
        Assert.Contains("ApplyGenreOptionsToComboBox(resetToDefault)", source, StringComparison.Ordinal);
        Assert.Contains("if (resetToDefault || !GenreListService.IsSelectableGenre(selectedGenre))", source, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(params string[] relativePathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(relativePathParts).ToArray());
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException("Repository file not found.", Path.Combine(relativePathParts));
    }
}

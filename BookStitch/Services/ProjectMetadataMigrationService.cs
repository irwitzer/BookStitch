using BookStitch.Models;
using System.IO;

namespace BookStitch.Services;

public sealed class ProjectMetadataMigrationService
{
    private readonly ProjectMetadataStore _store;

    public ProjectMetadataMigrationService()
        : this(new ProjectMetadataStore())
    {
    }

    public ProjectMetadataMigrationService(ProjectMetadataStore store)
    {
        _store = store;
    }

    public ProjectMetadata EnsureMigrated(
        string projectFolder,
        Mp3DiscProjectManifest? mp3DiscManifest,
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        if (File.Exists(ProjectFolderLayout.GetProjectMetadataPath(projectFolder)))
            return _store.LoadOrCreate(projectFolder);

        var metadata = BuildLegacyMetadata(mp3DiscManifest, exportManifest, audioDiscManifest);
        _store.Save(projectFolder, metadata);
        return metadata;
    }

    public ProjectMetadata LoadAuthoritativeOrLegacy(string projectFolder,
        Mp3DiscProjectManifest? mp3DiscManifest, ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest) =>
        File.Exists(ProjectFolderLayout.GetProjectMetadataPath(projectFolder))
            ? _store.LoadOrCreate(projectFolder)
            : BuildLegacyMetadata(mp3DiscManifest, exportManifest, audioDiscManifest);

    public static ProjectMetadataProjection CreateProjection(ProjectMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        return new ProjectMetadataProjection(
            StringValue(metadata, ProjectMetadataField.Title),
            FirstStringValue(metadata, ProjectMetadataField.Authors),
            StringValue(metadata, ProjectMetadataField.Album),
            FirstStringValue(metadata, ProjectMetadataField.Narrators),
            FirstStringValue(metadata, ProjectMetadataField.Genres));
    }

    public ProjectMetadataProjection SaveCurrentProjectMetadata(
        string projectFolder,
        string title,
        string author,
        string album,
        string narrator,
        string genre)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        var path = ProjectFolderLayout.GetProjectMetadataPath(projectFolder);
        var storeExists = File.Exists(path);
        var metadata = _store.LoadOrCreate(projectFolder);
        var changed = false;

        changed |= SetCurrentString(metadata, ProjectMetadataField.Title, title);
        changed |= SetCurrentString(metadata, ProjectMetadataField.Album, album);
        changed |= SetCurrentSingleValueList(metadata, ProjectMetadataField.Authors, author);
        changed |= SetCurrentSingleValueList(metadata, ProjectMetadataField.Narrators, narrator);
        changed |= SetCurrentSingleValueList(metadata, ProjectMetadataField.Genres, genre);

        if (!storeExists || changed)
            _store.Save(projectFolder, metadata);

        return CreateProjection(metadata);
    }

    public ProjectMetadataProjection LoadAuthoritativeProjection(string projectFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        return CreateProjection(_store.LoadOrCreate(projectFolder));
    }

    public ProjectMetadataProjection LoadAuthoritativeProjectionOrLegacy(
        string projectFolder,
        Mp3DiscProjectManifest? mp3DiscManifest = null,
        ExportWorkManifest? exportManifest = null,
        AudioDiscProjectManifest? audioDiscManifest = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);

        if (File.Exists(ProjectFolderLayout.GetProjectMetadataPath(projectFolder)))
            return LoadAuthoritativeProjection(projectFolder);

        return CreateProjection(BuildLegacyMetadata(
            mp3DiscManifest,
            exportManifest,
            audioDiscManifest));
    }

    private static ProjectMetadata BuildLegacyMetadata(
        Mp3DiscProjectManifest? mp3DiscManifest,
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest)
    {
        var metadata = new ProjectMetadata();
        SetString(metadata, ProjectMetadataField.Title, SelectTitle(mp3DiscManifest, exportManifest, audioDiscManifest));
        SetString(metadata, ProjectMetadataField.Album, SelectAlbum(mp3DiscManifest, exportManifest, audioDiscManifest));
        SetStringList(metadata, ProjectMetadataField.Authors, SelectAuthor(mp3DiscManifest, exportManifest, audioDiscManifest));
        SetStringList(metadata, ProjectMetadataField.Narrators, SelectNarrator(mp3DiscManifest, exportManifest, audioDiscManifest));
        SetStringList(metadata, ProjectMetadataField.Genres, SelectGenre(mp3DiscManifest, exportManifest, audioDiscManifest));
        var coverSource = SelectField(exportManifest?.Metadata.CoverSourcePath,
            audioDiscManifest?.CoverSourcePath, mp3DiscManifest?.CoverSourcePath);
        var processedCover = SelectField(exportManifest?.Metadata.ProcessedCoverPath,
            audioDiscManifest?.ProcessedCoverPath, mp3DiscManifest?.ProcessedCoverPath);
        if (!string.IsNullOrWhiteSpace(coverSource) || !string.IsNullOrWhiteSpace(processedCover))
            metadata.SetCover(coverSource, processedCover, MetadataOrigin.LegacyMigrated);
        return metadata;
    }

    private static string SelectTitle(
        Mp3DiscProjectManifest? mp3DiscManifest,
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest) =>
        SelectField(audioDiscManifest?.Title, mp3DiscManifest?.Title, exportManifest?.Metadata.Title);

    private static string SelectAuthor(
        Mp3DiscProjectManifest? mp3DiscManifest,
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest) =>
        SelectField(audioDiscManifest?.Author, mp3DiscManifest?.Author, exportManifest?.Metadata.Author);

    private static string SelectAlbum(
        Mp3DiscProjectManifest? mp3DiscManifest,
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest) =>
        SelectField(audioDiscManifest?.Album, mp3DiscManifest?.Album, exportManifest?.Metadata.Album);

    private static string SelectNarrator(
        Mp3DiscProjectManifest? mp3DiscManifest,
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest) =>
        SelectField(audioDiscManifest?.Narrator, mp3DiscManifest?.Narrator, exportManifest?.Metadata.Narrator);

    private static string SelectGenre(
        Mp3DiscProjectManifest? mp3DiscManifest,
        ExportWorkManifest? exportManifest,
        AudioDiscProjectManifest? audioDiscManifest) =>
        SelectField(audioDiscManifest?.Genre, mp3DiscManifest?.Genre, exportManifest?.Metadata.Genre);

    private static string SelectField(string? audioDiscValue, string? mp3DiscValue, string? exportValue)
    {
        foreach (var value in new[] { audioDiscValue, mp3DiscValue, exportValue })
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();

        return string.Empty;
    }

    private static void SetString(ProjectMetadata metadata, ProjectMetadataField field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata.SetValue(field, value, MetadataOrigin.LegacyMigrated);
    }

    private static void SetStringList(ProjectMetadata metadata, ProjectMetadataField field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            metadata.SetValue<IReadOnlyList<string>>(field, [value.Trim()], MetadataOrigin.LegacyMigrated);
    }

    private static bool SetCurrentString(ProjectMetadata metadata, ProjectMetadataField field, string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var current = metadata.GetValue<string>(field);
        if (string.Equals(current.Value ?? string.Empty, normalized, StringComparison.Ordinal))
            return false;

        return metadata.SetValue(field, normalized, MetadataOrigin.User);
    }

    private static bool SetCurrentSingleValueList(ProjectMetadata metadata, ProjectMetadataField field, string? value)
    {
        var normalized = value?.Trim() ?? string.Empty;
        var current = metadata.GetValue<IReadOnlyList<string>>(field).Value ?? [];
        if (!string.IsNullOrWhiteSpace(normalized) &&
            current.Count > 0 &&
            string.Equals(current[0], normalized, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(normalized) && current.Count == 0)
            return false;

        return metadata.SetValue<IReadOnlyList<string>>(
            field,
            string.IsNullOrWhiteSpace(normalized) ? [] : [normalized],
            MetadataOrigin.User);
    }

    private static string StringValue(ProjectMetadata metadata, ProjectMetadataField field) =>
        metadata.GetValue<string>(field).Value ?? string.Empty;

    private static string FirstStringValue(ProjectMetadata metadata, ProjectMetadataField field) =>
        metadata.GetValue<IReadOnlyList<string>>(field).Value?.FirstOrDefault() ?? string.Empty;

}

public sealed record ProjectMetadataProjection(
    string Title,
    string Author,
    string Album,
    string Narrator,
    string Genre);

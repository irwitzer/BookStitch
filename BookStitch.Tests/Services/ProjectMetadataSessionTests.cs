using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class ProjectMetadataSessionTests
{
    [Fact]
    public void Failed_final_flush_retains_dirty_session_until_a_successful_retry()
    {
        using var folder = new TemporaryFolder();
        var session = new ProjectMetadataSession();
        session.BeginNew();
        session.AttachNewProject(folder.Path);
        session.Mutate(metadata => metadata.SetValue(ProjectMetadataField.Title, "unsaved", MetadataOrigin.User));
        var path = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);
        var persisted = System.IO.File.ReadAllBytes(path);
        System.IO.File.WriteAllText(path, "{ damaged");
        Assert.ThrowsAny<Exception>(() => session.Flush());
        Assert.True(session.HasUnpersistedChanges);
        Assert.Throws<InvalidOperationException>(() => session.Detach());
        System.IO.File.WriteAllBytes(path, persisted);
        Assert.True(session.Flush());
        Assert.False(session.HasUnpersistedChanges);
        Assert.Equal("unsaved", new ProjectMetadataStore().LoadOrCreate(folder.Path).GetValue<string>(ProjectMetadataField.Title).Value);
    }

    [Fact]
    public void BeginNew_OwnsCanonicalMetadataBeforeAProjectFolderExists()
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Entwurf", MetadataOrigin.User);
        var session = new ProjectMetadataSession();

        var current = session.BeginNew(metadata);

        Assert.True(session.HasCurrent);
        Assert.False(session.IsAttached);
        Assert.Same(metadata, current);
        Assert.Same(metadata, session.Current);
        Assert.Null(session.ProjectFolder);
        Assert.Null(session.PersistedRevision);
        Assert.True(session.HasUnpersistedChanges);
    }

    [Fact]
    public void BeginNew_WhenSessionAlreadyOwnsMetadata_ThrowsWithoutReplacingIt()
    {
        var first = new ProjectMetadata();
        var second = new ProjectMetadata();
        var session = new ProjectMetadataSession();
        session.BeginNew(first);

        Assert.Throws<InvalidOperationException>(() => session.BeginNew(second));
        Assert.Same(first, session.Current);
    }

    [Fact]
    public void AttachNewProject_PersistsTheExistingLiveInstanceWithoutReplacingIt()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Titel", MetadataOrigin.User);
        var session = new ProjectMetadataSession();
        session.BeginNew(metadata);

        var current = session.AttachNewProject(folder.Path);

        Assert.True(session.IsAttached);
        Assert.Same(metadata, current);
        Assert.Same(metadata, session.Current);
        Assert.Equal(Path.GetFullPath(folder.Path), session.ProjectFolder);
        Assert.Equal(metadata.Revision, session.PersistedRevision);
        Assert.False(session.HasUnpersistedChanges);

        var stored = new ProjectMetadataStore().LoadOrCreate(folder.Path);
        Assert.Equal("Titel", stored.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(metadata.Revision, stored.Revision);
    }

    [Fact]
    public void AttachNewProject_WhenAlreadyAttached_StillThrowsAndKeepsTheOriginalSession()
    {
        using var firstFolder = new TemporaryFolder();
        using var secondFolder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        var session = new ProjectMetadataSession();
        session.BeginNew(metadata);
        session.AttachNewProject(firstFolder.Path);

        Assert.Throws<InvalidOperationException>(() => session.AttachNewProject(secondFolder.Path));
        Assert.True(session.IsAttached);
        Assert.Same(metadata, session.Current);
        Assert.Equal(Path.GetFullPath(firstFolder.Path), session.ProjectFolder);
    }

    [Fact]
    public void AttachNewProject_WhenCanonicalStoreAlreadyExists_ThrowsWithoutOverwritingOrReplacingLiveMetadata()
    {
        using var folder = new TemporaryFolder();
        var existing = new ProjectMetadata();
        existing.SetValue(ProjectMetadataField.Title, "Bestehend", MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, existing);

        var live = new ProjectMetadata();
        live.SetValue(ProjectMetadataField.Title, "Neu", MetadataOrigin.User);
        var session = new ProjectMetadataSession();
        session.BeginNew(live);

        Assert.Throws<InvalidOperationException>(() => session.AttachNewProject(folder.Path));
        Assert.Same(live, session.Current);
        Assert.False(session.IsAttached);
        Assert.True(session.HasUnpersistedChanges);

        var stored = new ProjectMetadataStore().LoadOrCreate(folder.Path);
        Assert.Equal("Bestehend", stored.GetValue<string>(ProjectMetadataField.Title).Value);
    }

    [Fact]
    public void OpenExisting_LoadsPersistedMetadataAndTracksItsRevision()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Persistiert", MetadataOrigin.SourceAggregation);
        new ProjectMetadataStore().Save(folder.Path, metadata);
        var session = new ProjectMetadataSession();

        var current = session.OpenExisting(folder.Path);

        Assert.True(session.HasCurrent);
        Assert.True(session.IsAttached);
        Assert.Same(current, session.Current);
        Assert.Equal("Persistiert", current.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal(metadata.Revision, session.PersistedRevision);
        Assert.False(session.HasUnpersistedChanges);
    }

    [Fact]
    public void OpenExisting_WhenCanonicalStoreIsMissing_ThrowsWithoutCreatingASecondTruth()
    {
        using var folder = new TemporaryFolder();
        var session = new ProjectMetadataSession();
        var metadataPath = ProjectFolderLayout.GetProjectMetadataPath(folder.Path);

        Assert.Throws<FileNotFoundException>(() => session.OpenExisting(folder.Path));
        Assert.False(File.Exists(metadataPath));
        Assert.False(session.HasCurrent);
        Assert.False(session.IsAttached);
    }

    [Fact]
    public void OpenExisting_WhenSessionAlreadyOwnsMetadata_ThrowsWithoutDiscardingTheLiveInstance()
    {
        using var folder = new TemporaryFolder();
        var stored = new ProjectMetadata();
        stored.SetValue(ProjectMetadataField.Title, "Persistiert", MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, stored);

        var live = new ProjectMetadata();
        live.SetValue(ProjectMetadataField.Title, "Live", MetadataOrigin.User);
        var session = new ProjectMetadataSession();
        session.BeginNew(live);

        Assert.Throws<InvalidOperationException>(() => session.OpenExisting(folder.Path));
        Assert.Same(live, session.Current);
        Assert.Null(session.ProjectFolder);
    }

    [Fact]
    public void Mutate_ChangesOnlyTheLiveInstanceUntilFlush()
    {
        using var folder = new TemporaryFolder();
        var session = new ProjectMetadataSession();
        session.BeginNew();
        session.AttachNewProject(folder.Path);

        var changed = session.Mutate(metadata =>
            metadata.SetValue(ProjectMetadataField.Album, "Nur im RAM", MetadataOrigin.User));

        Assert.True(changed);
        Assert.True(session.HasUnpersistedChanges);
        Assert.Null(new ProjectMetadataStore().LoadOrCreate(folder.Path).GetValue<string>(ProjectMetadataField.Album).Value);

        Assert.True(session.Flush());
        Assert.False(session.HasUnpersistedChanges);
        Assert.Equal("Nur im RAM", new ProjectMetadataStore().LoadOrCreate(folder.Path).GetValue<string>(ProjectMetadataField.Album).Value);
    }

    [Fact]
    public void Mutate_WhenMutationIsRevisionNeutral_ReturnsFalseAndDoesNotRequireAFlush()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Album, "Album", MetadataOrigin.User);
        var session = new ProjectMetadataSession();
        session.BeginNew(metadata);
        session.AttachNewProject(folder.Path);

        var changed = session.Mutate(item =>
            item.SetValue(ProjectMetadataField.Album, "Album", MetadataOrigin.User));

        Assert.False(changed);
        Assert.False(session.HasUnpersistedChanges);
        Assert.False(session.Flush());
    }

    [Fact]
    public void Flush_BeforeProjectFolderAttachment_ThrowsWithoutLosingTheLiveMetadata()
    {
        var metadata = new ProjectMetadata();
        var session = new ProjectMetadataSession();
        session.BeginNew(metadata);

        Assert.Throws<InvalidOperationException>(() => session.Flush());
        Assert.Same(metadata, session.Current);
        Assert.True(session.HasUnpersistedChanges);
    }

    [Fact]
    public void ReloadFromStore_ReplacesAnUnpersistedLiveMutationWithPersistedCanonicalState()
    {
        using var folder = new TemporaryFolder();
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, "Gespeichert", MetadataOrigin.User);
        var session = new ProjectMetadataSession();
        session.BeginNew(metadata);
        session.AttachNewProject(folder.Path);
        var original = session.Current!;

        session.Mutate(item =>
            item.SetValue(ProjectMetadataField.Title, "Nur im RAM", MetadataOrigin.User));
        Assert.True(session.HasUnpersistedChanges);

        var reloaded = session.ReloadFromStore();

        Assert.NotSame(original, reloaded);
        Assert.Same(reloaded, session.Current);
        Assert.Equal("Gespeichert", reloaded.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.False(session.HasUnpersistedChanges);
        Assert.Equal(reloaded.Revision, session.PersistedRevision);
    }

    [Fact]
    public void Detach_RejectsUnpersistedChangesUntilTheyAreFlushed()
    {
        using var folder = new TemporaryFolder();
        var session = new ProjectMetadataSession();
        session.BeginNew();
        session.AttachNewProject(folder.Path);
        session.Mutate(metadata =>
            metadata.SetValue(ProjectMetadataField.Title, "Geändert", MetadataOrigin.User));

        Assert.Throws<InvalidOperationException>(() => session.Detach());
        Assert.True(session.IsAttached);
        Assert.True(session.HasUnpersistedChanges);

        Assert.True(session.Flush());
        session.Detach();

        Assert.False(session.HasCurrent);
        Assert.False(session.IsAttached);
        Assert.Null(session.ProjectFolder);
        Assert.Null(session.PersistedRevision);
    }

    [Fact]
    public void Discard_ExplicitlyClearsAnUnpersistedNewSession()
    {
        var session = new ProjectMetadataSession();
        session.BeginNew();
        session.Mutate(metadata =>
            metadata.SetValue(ProjectMetadataField.Title, "Verwerfen", MetadataOrigin.User));

        session.Discard();

        Assert.False(session.HasCurrent);
        Assert.False(session.IsAttached);
        Assert.Null(session.Current);
        Assert.Null(session.ProjectFolder);
        Assert.Null(session.PersistedRevision);
        Assert.False(session.HasUnpersistedChanges);
    }

    [Fact]
    public void Flush_WhenPersistedRevisionChangedOutsideTheSession_RejectsStaleOverwrite()
    {
        using var folder = new TemporaryFolder();
        var session = new ProjectMetadataSession();
        var initial = new ProjectMetadata();
        initial.SetValue(ProjectMetadataField.Title, "Original", MetadataOrigin.User);
        session.BeginNew(initial);
        session.AttachNewProject(folder.Path);
        var baselineRevision = session.PersistedRevision;

        session.Mutate(metadata =>
            metadata.SetValue(ProjectMetadataField.Description, "Nur Session", MetadataOrigin.User));

        var external = new ProjectMetadataStore().LoadOrCreate(folder.Path);
        external.SetValue(ProjectMetadataField.Album, "Extern neuer", MetadataOrigin.User);
        new ProjectMetadataStore().Save(folder.Path, external);

        Assert.Throws<InvalidOperationException>(() => session.Flush());
        Assert.True(session.HasUnpersistedChanges);
        Assert.Equal(baselineRevision, session.PersistedRevision);

        var persisted = new ProjectMetadataStore().LoadOrCreate(folder.Path);
        Assert.Equal("Extern neuer", persisted.GetValue<string>(ProjectMetadataField.Album).Value);
        Assert.Null(persisted.GetValue<string>(ProjectMetadataField.Description).Value);
    }


}

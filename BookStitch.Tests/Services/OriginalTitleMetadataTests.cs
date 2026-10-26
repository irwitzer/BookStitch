using BookStitch.Models;
using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class OriginalTitleMetadataTests
{
    [Theory]
    [InlineData(MetadataTagFormat.Id3v2)]
    [InlineData(MetadataTagFormat.Ape)]
    [InlineData(MetadataTagFormat.Mp4)]
    [InlineData(MetadataTagFormat.Xiph)]
    [InlineData(MetadataTagFormat.Asf)]
    [InlineData(MetadataTagFormat.Riff)]
    public void Named_original_title_is_independent_of_title_subtitle_and_original_album(MetadataTagFormat format)
    {
        var tags = TagMetadataReader.CreateFromNamedTagsForTests(format,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["TITLE"] = ["Translated title"],
                ["SUBTITLE"] = ["Subtitle"],
                ["ORIGINALTITLE"] = ["  Original work title  "],
                ["ORIGTITLE"] = ["Alias"],
                ["TOAL"] = ["Original album"]
            });
        Assert.Equal("Translated title", tags.Title);
        Assert.Equal("Subtitle", tags.Subtitle);
        Assert.Equal("Original work title", tags.OriginalTitle);
        Assert.Contains(tags.RawTagEntries, tag => tag.OriginalKey == "ORIGINALTITLE" || tag.OriginalKey.EndsWith(":ORIGINALTITLE", StringComparison.Ordinal));
    }

    [Fact]
    public void Original_title_survives_source_and_project_reload_and_output_projection()
    {
        using var folder = new TemporaryFolder();
        var snapshot = new SourceMetadataSnapshot
        {
            SourceId = Guid.NewGuid(),
            CanonicalFields = new SourceMetadataCanonicalFields
            { Title = "Translated title", OriginalTitle = "Original work title" }
        };
        var sources = new SourceMetadataSnapshotStore();
        sources.Save(folder.Path, snapshot);
        var restoredSource = Assert.IsType<SourceMetadataSnapshot>(sources.TryLoad(folder.Path, snapshot.SourceId));
        Assert.Equal("Original work title", restoredSource.CanonicalFields.OriginalTitle);
        var metadata = new ProjectMetadata();
        new ProjectMetadataInitializationService().Apply(metadata, [restoredSource], ProjectMetadataInitializationState.InitialSetComplete);
        var store = new ProjectMetadataStore();
        store.Save(folder.Path, metadata);
        var restored = store.LoadOrCreate(folder.Path);
        Assert.Equal("Translated title", restored.GetValue<string>(ProjectMetadataField.Title).Value);
        Assert.Equal("Original work title", restored.GetValue<string>(ProjectMetadataField.OriginalTitle).Value);
        var output = new ProjectMetadataOutputProjectionService().Create(restored,
            new ProjectMetadataOutputProjectionOptions("", false, "en-US"),
            new Dictionary<ProjectMetadataField, OutputFieldPolicy>(), new Dictionary<string, bool>());
        Assert.Equal("Original work title", output.OriginalTitle);
        Assert.Equal("Original work title", output.WithFinalTechnicalData(TimeSpan.FromMinutes(1), 2).OriginalTitle);
    }

    [Fact]
    public void Original_title_uses_existing_manual_and_suppression_protection()
    {
        var metadata = new ProjectMetadata();
        var source = new SourceMetadataSnapshot
        { SourceId = Guid.NewGuid(), CanonicalFields = new() { OriginalTitle = "Original work title" } };
        var service = new ProjectMetadataInitializationService();
        metadata.SuppressValue(ProjectMetadataField.OriginalTitle, "Original work title");
        service.Apply(metadata, [source], ProjectMetadataInitializationState.InitialSetComplete);
        Assert.Null(metadata.GetValue<string>(ProjectMetadataField.OriginalTitle).Value);
        metadata.UnsuppressValue(ProjectMetadataField.OriginalTitle, "Original work title");
        metadata.SetValue(ProjectMetadataField.OriginalTitle, "Manual original title", MetadataOrigin.User);
        service.Apply(metadata, [source], ProjectMetadataInitializationState.SubsequentSource);
        Assert.Equal("Manual original title", metadata.GetValue<string>(ProjectMetadataField.OriginalTitle).Value);
    }

    [Fact]
    public void Existing_metadata_defaults_to_empty_original_title_and_retains_field_ids()
    {
        Assert.Equal(0, (int)ProjectMetadataField.Title);
        Assert.Equal(48, (int)ProjectMetadataField.Composers);
        Assert.Equal(50, (int)ProjectMetadataField.OriginalTitle);
        Assert.Equal(ProjectMetadataFieldValueKind.String, ProjectMetadataFields.GetValueKind(ProjectMetadataField.OriginalTitle));
        Assert.Empty(new ProjectMetadataState().OriginalTitle);
        Assert.Empty(new SourceMetadataCanonicalFields().OriginalTitle);
    }
}

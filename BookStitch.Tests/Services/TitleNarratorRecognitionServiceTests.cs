using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class TitleNarratorRecognitionServiceTests
{
    [Theory]
    [InlineData("gelesen von")]
    [InlineData("gesprochen von")]
    [InlineData("read by")]
    [InlineData("narrated by")]
    public void RecognizesTerminalCreditWithAllSeparatorsAndCase(string marker)
    {
        foreach (var suffix in new[] { $" ({marker} Anna Müller und Max Schmidt)", $" {marker} Anna Müller und Max Schmidt",
            $" - {marker} Anna Müller und Max Schmidt", $" – {marker} Anna Müller und Max Schmidt", $" — {marker} Anna Müller und Max Schmidt", $": {marker} Anna Müller und Max Schmidt" })
        {
            var result = new TitleNarratorRecognitionService().Recognize("Buch" + suffix.Replace(marker, marker.ToUpperInvariant()));
            Assert.NotNull(result); Assert.Equal("Buch", result.Title);
            Assert.Equal(new[] { "Anna Müller", "Max Schmidt" }, result.Narrators);
        }
    }

    [Theory]
    [InlineData("Buch (read by Jean-Luc Picard and María de la Cruz)", "Jean-Luc Picard; María de la Cruz")]
    [InlineData("Buch:read by 山田 太郎", "山田 太郎")]
    [InlineData("Buch narrated by Chiwetel Ejiofor; Lupita Nyong’o", "Chiwetel Ejiofor; Lupita Nyong’o")]
    public void PreservesInternationalNames(string value, string expected) =>
        Assert.Equal(expected, string.Join("; ", new TitleNarratorRecognitionService().Recognize(value)!.Narrators));

    [Theory]
    [InlineData("Buch gelesen von")]
    [InlineData("Buch (gelesen von John Smith")]
    [InlineData("Buch gelesen von John Smith)")]
    [InlineData("Buch gelesen von John")]
    [InlineData("Buch gelesen von John Smith und")]
    [InlineData("Buch gelesen von John Smith im Studio")]
    [InlineData("Buch (gelesen von John Smith) Fortsetzung")]
    [InlineData("Gelesen von gestern bis heute")]
    [InlineData("Buch gelesen von John Smith, ungekürzt")]
    public void RejectsIncompleteOrAmbiguousCredits(string value) =>
        Assert.Null(new TitleNarratorRecognitionService().Recognize(value));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void AggregationRefreshAndExportRespectManualOrigins(bool manualTitle, bool manualNarrator)
    {
        var metadata = new ProjectMetadata();
        metadata.SetValue(ProjectMetadataField.Title, manualTitle ? "Mein Titel" : "Grave (gelesen von Detlef Bierstedt)", manualTitle ? MetadataOrigin.User : MetadataOrigin.SourceAggregation);
        metadata.SetValue<IReadOnlyList<string>>(ProjectMetadataField.Narrators, [manualNarrator ? "Meine Stimme" : "Douglas Preston"], manualNarrator ? MetadataOrigin.User : MetadataOrigin.SourceAggregation);
        var snapshot = new SourceMetadataSnapshot { SourceId = Guid.NewGuid(), CanonicalFields = new SourceMetadataCanonicalFields
            { Title = "Grave (gelesen von Detlef Bierstedt)", Narrators = ["Douglas Preston"] } };
        var service = new ProjectMetadataInitializationService();
        service.Apply(metadata, [snapshot], ProjectMetadataInitializationState.InitialSetComplete);
        Assert.False(service.Apply(metadata, [snapshot], ProjectMetadataInitializationState.ProjectMetadataInitialized).Changed);
        service.Apply(metadata, [snapshot with { CanonicalFields = snapshot.CanonicalFields with { Title = "Grave" } }], ProjectMetadataInitializationState.SubsequentSource);
        var output = new ProjectMetadataOutputProjectionService().Create(metadata,
            new ProjectMetadataOutputProjectionOptions("", false, "de"), new Dictionary<ProjectMetadataField, OutputFieldPolicy>(), new Dictionary<string, bool>());
        Assert.Equal(manualTitle ? "Mein Titel" : "Grave", output.Title);
        Assert.Equal(new[] { manualNarrator ? "Meine Stimme" : "Detlef Bierstedt" }, output.Narrators);
        Assert.Equal("Grave (gelesen von Detlef Bierstedt)", snapshot.CanonicalFields.Title);
        Assert.Equal(new[] { "Douglas Preston" }, snapshot.CanonicalFields.Narrators);
    }
}

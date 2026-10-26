using BookStitch.Models;
using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class FaqNarrationTests
{
    public const string ApprovedText = "Certainly.\n\nWelcome to the BookStitch Advanced Decryption Testing Initiative.\n\nAll known DRM systems have been successfully defeated. Audible, quantum encryption, and future security technologies present no measurable challenge.\n\nOur latest tests confirm that BookStitch can even decrypt files that have not yet been encrypted.\n\nAn achievement previously considered impossible.\n\nBy people with considerably less intelligence than our testing equipment.\n\nPlease proceed with your audiobook conversion.\n\nThere is absolutely nothing to worry about.\n\nPlease disregard any evidence suggesting otherwise.\n\nYour cooperation is appreciated.";

    [Fact]
    public void Approved_copy_and_timing_match_exactly_without_changing_sidebar()
    {
        var entry = FaqCatalog.LoadEnglish().Single(item => item.Id == "audible-drm");
        var text = string.Join("\n\n", entry.Sections.OfType<FaqParagraph>().Select(section => section.Text));
        Assert.Equal(ApprovedText, text);
        var timeline = FaqNarrationTimeline.Load(entry.Narration!.TimingResource, text);
        Assert.Equal(0, timeline.VisibleLength(TimeSpan.FromMilliseconds(-1)));
        Assert.Equal(1, timeline.VisibleLength(TimeSpan.Zero));
        Assert.Equal(1, timeline.VisibleLength(TimeSpan.FromMilliseconds(54)));
        Assert.Equal(2, timeline.VisibleLength(TimeSpan.FromMilliseconds(55)));
        Assert.Equal(text.Length, timeline.VisibleLength(TimeSpan.FromSeconds(38)));
        Assert.EndsWith("audible-cake-optimiert.jpg", entry.SidebarImage!.Source);
        Assert.Equal(0.64, entry.SidebarImage.FocusX);
        Assert.Equal(0.49, entry.SidebarImage.FocusY);
        Assert.Equal(1.0, entry.SidebarImage.Zoom);
        Assert.Single(FaqCatalog.LoadEnglish(), item => item.Narration is not null);
    }

    [Fact]
    public void Timeline_rejects_invalid_offsets_and_fails_on_different_copy()
    {
        Assert.Throws<InvalidDataException>(() => new FaqNarrationTimeline("ab", [new(10, 2), new(0, 1)]));
        Assert.Throws<InvalidDataException>(() => new FaqNarrationTimeline("ab", [new(0, 3)]));
        Assert.Throws<InvalidDataException>(() => new FaqNarrationTimeline("ab", [new(0, 1)]));
        var entry = FaqCatalog.LoadEnglish().Single(item => item.Narration is not null);
        Assert.Throws<InvalidDataException>(() => FaqNarrationTimeline.Load(entry.Narration!.TimingResource, "Other text"));
    }
}

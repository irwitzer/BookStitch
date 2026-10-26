using BookStitch.Dialog;
using BookStitch.Models;
using Xunit;

namespace BookStitch.Tests.Dialog;

public sealed class ConceptEnumCandidateOrderTests
{
    [Fact]
    public void ConcreteOptionsPrecedeUnknownFallbacks()
    {
        Assert.Equal(
            [AudioWorkType.Audiobook, AudioWorkType.AudioDrama, AudioWorkType.Unknown],
            ConceptEnumCandidateOrder.AudioWorkTypes);
        Assert.Equal(
            [AbridgedStatus.Unabridged, AbridgedStatus.Abridged, AbridgedStatus.Unknown],
            ConceptEnumCandidateOrder.AbridgedStatuses);
    }
}

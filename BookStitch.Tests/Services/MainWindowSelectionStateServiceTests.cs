using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class MainWindowSelectionStateServiceTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void CreateBooleanChange_DetectsChanges(
        bool currentValue,
        bool incomingValue,
        bool expectedHasChanged)
    {
        var result = MainWindowSelectionStateService.CreateBooleanChange(
            currentValue,
            incomingValue);

        Assert.Equal(incomingValue, result.Value);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }

    [Fact]
    public void CreateReferenceChange_RejectsSameReference()
    {
        var instance = new object();

        var result = MainWindowSelectionStateService.CreateReferenceChange(
            instance,
            instance);

        Assert.Same(instance, result.Value);
        Assert.False(result.HasChanged);
    }

    [Fact]
    public void CreateReferenceChange_AcceptsDifferentReference()
    {
        var currentValue = new object();
        var incomingValue = new object();

        var result = MainWindowSelectionStateService.CreateReferenceChange(
            currentValue,
            incomingValue);

        Assert.Same(incomingValue, result.Value);
        Assert.True(result.HasChanged);
    }

    [Theory]
    [InlineData("", "", "", false)]
    [InlineData("Bereit", "Bereit", "Bereit", false)]
    [InlineData("Bereit", "bereit", "bereit", true)]
    [InlineData("Alt", "Neu", "Neu", true)]
    public void CreateOrdinalStringChange_DetectsOrdinalChanges(
        string currentValue,
        string normalizedIncomingValue,
        string expectedValue,
        bool expectedHasChanged)
    {
        var result = MainWindowSelectionStateService.CreateOrdinalStringChange(
            currentValue,
            normalizedIncomingValue);

        Assert.Equal(expectedValue, result.Value);
        Assert.Equal(expectedHasChanged, result.HasChanged);
    }

}
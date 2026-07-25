using BookStitch.Services;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class DeveloperDiscTestServiceTests
{
    private readonly DeveloperDiscTestService _service = new();

    [Theory]
    [InlineData(DeveloperDiscSimulationScenario.EmptyDrive)]
    [InlineData(DeveloperDiscSimulationScenario.UnsupportedDisc)]
    [InlineData(DeveloperDiscSimulationScenario.DuplicateEjected)]
    [InlineData(DeveloperDiscSimulationScenario.DuplicateManualEject)]
    public void CreateSimulationResult_WaitingScenariosNeverComplete(DeveloperDiscSimulationScenario scenario)
    {
        var result = _service.CreateSimulationResult(scenario, checkNumber: 1);

        Assert.False(result.CanImport);
        Assert.NotEmpty(result.DialogText);
        Assert.NotEmpty(result.StatusText);
        Assert.NotEmpty(result.ProgressText);

        if (scenario is DeveloperDiscSimulationScenario.DuplicateEjected or DeveloperDiscSimulationScenario.DuplicateManualEject)
            Assert.Equal(DiscPollingDisplayState.Duplicate, result.DisplayState);
    }

    [Fact]
    public void CreateSimulationResult_SlowScenarioCompletesOnFourthCheck()
    {
        Assert.False(_service.CreateSimulationResult(DeveloperDiscSimulationScenario.SlowThenReady, 1).CanImport);
        Assert.False(_service.CreateSimulationResult(DeveloperDiscSimulationScenario.SlowThenReady, 3).CanImport);
        Assert.True(_service.CreateSimulationResult(DeveloperDiscSimulationScenario.SlowThenReady, 4).CanImport);
    }

    [Fact]
    public void CreateSimulationResult_ReadyScenarioCompletesImmediatelyWithoutImportText()
    {
        var result = _service.CreateSimulationResult(DeveloperDiscSimulationScenario.Ready, checkNumber: 1);

        Assert.True(result.CanImport);
        Assert.Contains("kein Import", result.ProgressText, StringComparison.OrdinalIgnoreCase);
    }
}

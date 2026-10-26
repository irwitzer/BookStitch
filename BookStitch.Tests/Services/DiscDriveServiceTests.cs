using BookStitch.Services;
using BookStitch.Tests.TestHelpers;
using Xunit;

namespace BookStitch.Tests.Services;

public class DiscDriveServiceTests
{
    private readonly DiscDriveService _service = new();

    [Fact]
    public async Task A_canceled_queued_operation_never_runs_its_hardware_action()
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var running = _service.MeasureOperationAsync(@"S:\", "test.running", () =>
        {
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return 1;
        });
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var queued = _service.MeasureOperationAsync(@"s:\folder", "test.queued-eject", () => ++calls, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => queued);
            Assert.Equal(0, calls);
            Assert.False(running.IsCompleted);
        }
        finally { release.Set(); await running; }
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Failed_native_operation_releases_the_drive_gate()
    {
        await Assert.ThrowsAsync<IOException>(() => _service.MeasureOperationAsync<int>(@"T:\", "test.failure",
            () => throw new IOException("native read failure")));
        Assert.Equal(7, await new DiscDriveService().MeasureOperationAsync(@"T:\", "test.recovery", () => 7)
            .WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(301, 0, 0)]
    [InlineData(0, 25, 0)]
    [InlineData(0, 0, 305)]
    public void Bounded_classification_never_rejects_audio_beyond_its_search_budget(int files, int branches, int depth)
    {
        using var root = new TemporaryFolder();
        for (var index = 0; index < files; index++)
            File.WriteAllText(Path.Combine(root.Path, $"{index:0000}.txt"), "data");
        for (var index = 0; index < branches; index++)
            Directory.CreateDirectory(Path.Combine(root.Path, $"{index:0000}"));
        var audioFolder = branches > 0 ? Path.Combine(root.Path, "0024") : root.Path;
        for (var index = 0; index < depth; index++) audioFolder = Path.Combine(audioFolder, "d");
        Directory.CreateDirectory(audioFolder);
        File.WriteAllBytes(Path.Combine(audioFolder, "track.mp3"), [1, 2, 3]);

        var kind = DiscDriveService.ClassifyMedia(root.Path, true);

        Assert.Contains(kind, new[] { DiscMediaKind.Unknown, DiscMediaKind.Mp3Disc });
        Assert.True(DiscDriveCandidateProbeService.CanAnalyzeMediaKind(kind, DiscMediaKind.Mp3Disc));
        Assert.True(new Mp3DiscImportService().AnalyzeSource(root.Path).IsSupportedDataDisc);
    }

    [Fact]
    public void Complete_small_data_scan_still_rejects_a_non_audio_medium()
    {
        using var root = new TemporaryFolder();
        File.WriteAllText(Path.Combine(root.Path, "data.txt"), "data");
        Assert.Equal(DiscMediaKind.DataDisc, DiscDriveService.ClassifyMedia(root.Path, true));
        Assert.False(DiscDriveCandidateProbeService.CanAnalyzeMediaKind(DiscMediaKind.DataDisc, DiscMediaKind.Mp3Disc));
        Assert.False(DiscDriveCandidateProbeService.CanAnalyzeMediaKind(DiscMediaKind.Unknown, DiscMediaKind.AudioCd));
    }

    [Fact]
    public async Task Cancellation_waits_for_native_completion_and_blocks_followup_operations_on_the_same_drive()
    {
        using var release = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sideEffects = 0;
        var root = @"Q:\";
        var first = _service.MeasureOperationAsync(root, "test.native", () =>
        {
            started.SetResult();
            Assert.True(release.Wait(TimeSpan.FromSeconds(5)));
            return DiscDriveOperationService.Execute(root, () => ++sideEffects);
        }, cancellation.Token);
        try
        {
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            Assert.False(first.IsCompleted);
            var next = new DiscDriveService().MeasureOperationAsync(root, "test.followup", () => ++sideEffects);
            var other = new DiscDriveService().MeasureOperationAsync(@"R:\", "test.other", () => 7);
            Assert.Equal(7, await other.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(next.IsCompleted);
            release.Set();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
            Assert.Equal(1, await next.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(1, sideEffects);
            Assert.True(Assert.Single(_service.MeasurementStore.Snapshot()).Cancelled);
        }
        finally { release.Set(); }
    }

    [Theory]
    [InlineData("ABC-123", true)]
    [InlineData("00000000", false)]
    [InlineData("unknown", false)]
    [InlineData("123456789", false)]
    public void StorageSerialValidationRejectsPlaceholderIdentities(string serial, bool expected)
    {
        Assert.Equal(expected, DiscDriveIdentityService.IsUsableSerialNumber(serial));
    }

    [Fact]
    public void HardwareFingerprintIsStableAndIndependentOfRuntimeAddress()
    {
        var first = DiscDriveIdentityService.BuildHardwareFingerprint("ACME", "DVD", "1.0");
        var second = DiscDriveIdentityService.BuildHardwareFingerprint(" acme ", "dvd", "1.0");

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
        Assert.DoesNotContain("G:", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveResumeDiscSource_ReturnsFirstExistingCandidateOutsideProjectFolder()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "Project");
        var firstSource = Path.Combine(folder.Path, "DiscSource1");
        var secondSource = Path.Combine(folder.Path, "DiscSource2");
        Directory.CreateDirectory(projectFolder);
        Directory.CreateDirectory(firstSource);
        Directory.CreateDirectory(secondSource);

        var result = _service.ResolveResumeDiscSource(
            projectFolder,
            "  ",
            firstSource + "  ",
            secondSource);

        Assert.Equal(firstSource, result);
    }

    [Fact]
    public void ResolveResumeDiscSource_SkipsProjectFolderAndItsChildren()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "Project");
        var projectChild = Path.Combine(projectFolder, "CD 01");
        var validSource = Path.Combine(folder.Path, "DiscSource");
        Directory.CreateDirectory(projectChild);
        Directory.CreateDirectory(validSource);

        var result = _service.ResolveResumeDiscSource(
            projectFolder,
            projectFolder,
            projectChild,
            validSource);

        Assert.Equal(validSource, result);
    }

    [Fact]
    public void ResolveResumeDiscSource_WhenNoCandidateIsValid_ReturnsEmptyString()
    {
        using var folder = new TemporaryFolder();
        var projectFolder = Path.Combine(folder.Path, "Project");
        Directory.CreateDirectory(projectFolder);

        var result = _service.ResolveResumeDiscSource(
            projectFolder,
            null,
            "",
            Path.Combine(folder.Path, "Missing"));

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void DiscDriveInfo_WithVolumeLabel_UsesLabelAsDisplayName()
    {
        var drive = new DiscDriveInfo(@"Y:\", "Y:", true, "Kummer aller Art");

        Assert.Equal("Kummer aller Art", drive.DisplayName);
        Assert.Equal("Bereit", drive.StatusText);
    }

    [Fact]
    public void DiscDriveInfo_WhenEmpty_ReportsEmptyDrive()
    {
        var drive = new DiscDriveInfo(@"G:\", "G:", false, string.Empty);

        Assert.Equal("Kein Datenträger eingelegt", drive.DisplayName);
        Assert.Equal("Laufwerk ist leer", drive.StatusText);
    }
}

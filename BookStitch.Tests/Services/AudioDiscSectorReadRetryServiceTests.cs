using BookStitch.Services;
using System.ComponentModel;
using System.IO;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class AudioDiscSectorReadRetryServiceTests
{
    [Fact]
    public async Task ReadAsync_ReturnsFirstSuccessfulReadWithoutRetry()
    {
        var service = CreateWithoutDelay();
        var expected = new byte[] { 1, 2, 3 };
        var calls = 0;

        var result = await service.ReadAsync(
            400,
            16,
            (_, _) =>
            {
                calls++;
                return expected;
            },
            CancellationToken.None);

        Assert.Same(expected, result);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task ReadAsync_RetriesTransientReadErrorsAndReturnsSuccessfulResult()
    {
        var service = CreateWithoutDelay();
        var calls = 0;
        var retries = 0;

        var result = await service.ReadAsync(
            800,
            8,
            (_, _) =>
            {
                calls++;
                if (calls < 3)
                    throw new Win32Exception(21, "Laufwerk nicht bereit");

                return new byte[] { 7 };
            },
            CancellationToken.None,
            () => retries++);

        Assert.Equal(new byte[] { 7 }, result);
        Assert.Equal(3, calls);
        Assert.Equal(2, retries);
    }

    [Fact]
    public async Task ReadAsync_ReportsSectorRangeAndAttemptCountAfterFinalFailure()
    {
        var service = CreateWithoutDelay();
        var calls = 0;

        var exception = await Assert.ThrowsAsync<AudioDiscSectorReadException>(() =>
            service.ReadAsync(
                1_250,
                16,
                (_, _) =>
                {
                    calls++;
                    throw new IOException("Unvollständige Sektordaten");
                },
                CancellationToken.None));

        Assert.Equal(3, calls);
        Assert.Equal(1_250, exception.StartSector);
        Assert.Equal(16, exception.SectorCount);
        Assert.Equal(3, exception.Attempts);
        Assert.Contains("nach 3 Versuchen", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Unvollständige Sektordaten", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ReadAsync_RetryObserverFailureDoesNotAlterSuccessfulRetry()
    {
        var service = CreateWithoutDelay();
        var calls = 0;

        var result = await service.ReadAsync(
            900,
            8,
            (_, _) =>
            {
                calls++;
                if (calls == 1)
                    throw new IOException("Transient");

                return new byte[] { 9 };
            },
            CancellationToken.None,
            () => throw new InvalidOperationException("diagnostic observer failure"));

        Assert.Equal(new byte[] { 9 }, result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task ReadAsync_DoesNotRetryNonReadFailures()
    {
        var service = CreateWithoutDelay();
        var calls = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ReadAsync(
                100,
                4,
                (_, _) =>
                {
                    calls++;
                    throw new InvalidOperationException("Programmierfehler");
                },
                CancellationToken.None));

        Assert.Equal(1, calls);
    }

    private static AudioDiscSectorReadRetryService CreateWithoutDelay() =>
        new([TimeSpan.Zero, TimeSpan.Zero]);
}

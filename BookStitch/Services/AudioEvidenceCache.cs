using BookStitch.Models;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.Security.Cryptography;
using System.Text;

namespace BookStitch.Services;

public sealed record AudioEvidenceFileIdentity(string Path, long Length, long ModifiedUtc, long CreatedUtc,
    long ChangeTime = 0, string? PhysicalId = null, long UpdateSequenceNumber = 0)
{
    public string CacheKey => PhysicalId ?? Path;
    public bool Matches(AudioEvidenceFileIdentity other) => Length == other.Length && ModifiedUtc == other.ModifiedUtc &&
        CreatedUtc == other.CreatedUtc && ChangeTime == other.ChangeTime && UpdateSequenceNumber == other.UpdateSequenceNumber &&
        (PhysicalId is not null && other.PhysicalId is not null ? PhysicalId == other.PhysicalId : Path == other.Path);

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInfo { public long CreationTime, LastAccessTime, LastWriteTime, ChangeTime; public uint Attributes; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInfo
    {
        public uint Attributes; public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime, LastAccessTime, LastWriteTime;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, FileIndexHigh, FileIndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int kind, out BasicInfo info, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint control, nint input, uint inputSize,
        [Out] byte[] output, uint outputSize, out uint returned, nint overlapped);

    public static AudioEvidenceFileIdentity Read(string path)
    {
        var file = new FileInfo(System.IO.Path.GetFullPath(path));
        if (!file.Exists) throw new FileNotFoundException(LocalizationText.Get("Probe.SourceMissing"));
        var resolved = file.ResolveLinkTarget(true)?.FullName ?? file.FullName;
        file = new FileInfo(resolved);
        long change = 0, sequence = 0;
        string? physical = null;
        if (OperatingSystem.IsWindows())
        {
            using var handle = File.OpenHandle(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (GetFileInformationByHandleEx(handle, 0, out var basic, (uint)Marshal.SizeOf<BasicInfo>())) change = basic.ChangeTime;
            // Per-file USN detects close-together writes even when timestamps are restored or collide.
            // FSCTL_READ_FILE_USN_DATA defaults to USN_RECORD_V2 with no input buffer (USN at byte 24).
            var journal = new byte[1024];
            if (DeviceIoControl(handle, 0x000900eb, 0, 0, journal, (uint)journal.Length, out var returned, 0) &&
                returned >= 60 && BitConverter.ToUInt16(journal, 4) == 2)
                sequence = BitConverter.ToInt64(journal, 24);
            if (GetFileInformationByHandle(handle, out var info) && (info.FileIndexHigh != 0 || info.FileIndexLow != 0)) physical = $"{info.VolumeSerial:X8}:{info.FileIndexHigh:X8}{info.FileIndexLow:X8}";
        }
        return new(file.FullName.ToUpperInvariant(), file.Length, file.LastWriteTimeUtc.Ticks, file.CreationTimeUtc.Ticks, change, physical, sequence);
    }
}

public sealed record AudioEvidenceEntry
{
    public int SchemaVersion { get; init; } = 1;
    public bool CompleteProbeRequest { get; init; }
    public string Integrity { get; init; } = "";
    public required AudioEvidenceFileIdentity Source { get; init; }
    public required string ProbeIdentity { get; init; }
    public required AudioProbeInfo Probe { get; init; }
    public AudioProbeInfo? PreparedAacProbe { get; init; }
    public string? PreparedToolchainIdentity { get; init; }
    public List<Guid> SourceIds { get; init; } = [];
}

/// <summary>One project owns this evidence. Failed, cancelled or changed-source probes are never published.</summary>
public sealed class AudioEvidenceCache
{
    public const string ProbeSchemaIdentity = "audio-probe-v4/";
    private readonly ConcurrentDictionary<string, AudioEvidenceEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };
    public bool HasEntries => !_entries.IsEmpty;

    private static AudioProbeInfo Clone(AudioProbeInfo value) => JsonSerializer.Deserialize<AudioProbeInfo>(JsonSerializer.Serialize(value))!;
    private static AudioEvidenceEntry Seal(AudioEvidenceEntry value) => value with
    {
        Integrity = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value with { Integrity = "" }, Json))))
    };

    public async Task<IDisposable> EnterSupplementAsync(string path, CancellationToken token)
    {
        var gate = _gates.GetOrAdd(AudioEvidenceFileIdentity.Read(path).CacheKey + ":supplement", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        return new EvidenceLease(gate);
    }
    private sealed class EvidenceLease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;
        public void Dispose() { if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release(); }
    }

    public bool TryGet(string path, string probeIdentity, out AudioEvidenceEntry? entry)
    {
        entry = null;
        var identity = AudioEvidenceFileIdentity.Read(path);
        if (!_entries.TryGetValue(identity.CacheKey, out var saved) || saved.SchemaVersion != 1 || !saved.CompleteProbeRequest ||
            !saved.Source.Matches(identity) || saved.ProbeIdentity != probeIdentity || !saved.Probe.Success || saved.Probe.HasProbeErrors ||
            Seal(saved).Integrity != saved.Integrity) return false;
        entry = saved with { Probe = Clone(saved.Probe), PreparedAacProbe = saved.PreparedAacProbe is { } prepared ? Clone(prepared) : null,
            SourceIds = saved.SourceIds.ToList() };
        entry.Probe.FilePath = path;
        return true;
    }

    public async Task<AudioProbeInfo> GetOrProbeAsync(string path, string probeIdentity,
        Func<Task<AudioProbeInfo>> probe, CancellationToken token, Action<bool>? onCacheAccess = null)
    {
        var identity = AudioEvidenceFileIdentity.Read(path);
        var gate = _gates.GetOrAdd(identity.CacheKey, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            using var measurement = PerformanceMeasurement.Start("audio.evidence");
            if (TryGet(path, probeIdentity, out var entry))
            {
                measurement?.Add("CacheHits");
                onCacheAccess?.Invoke(true);
                return entry!.Probe;
            }
            measurement?.Add("CacheMisses");
            onCacheAccess?.Invoke(false);
            var result = await probe().ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (!identity.Matches(AudioEvidenceFileIdentity.Read(path))) throw new IOException(LocalizationText.Get("Aac.Plan.SourceError"));
            if (result.Success && !result.HasProbeErrors)
                _entries[identity.CacheKey] = Seal(new AudioEvidenceEntry { Source = identity, ProbeIdentity = probeIdentity, Probe = Clone(result), CompleteProbeRequest = true });
            return result;
        }
        finally { gate.Release(); }
    }

    public void Update(string path, string probeIdentity, AudioProbeInfo original, AudioProbeInfo? prepared, Guid sourceId,
        string? preparedToolchainIdentity = null)
    {
        var identity = AudioEvidenceFileIdentity.Read(path);
        _entries.AddOrUpdate(identity.CacheKey,
            _ => Seal(new AudioEvidenceEntry { Source = identity, ProbeIdentity = probeIdentity, Probe = Clone(original), CompleteProbeRequest = true,
                PreparedAacProbe = prepared is null ? null : Clone(prepared), PreparedToolchainIdentity = preparedToolchainIdentity, SourceIds = [sourceId] }),
            (_, existing) => existing.Source.Matches(identity) && existing.ProbeIdentity == probeIdentity
                ? Seal(existing with { Probe = Clone(original), PreparedAacProbe = prepared is null ? existing.PreparedAacProbe : Clone(prepared),
                    PreparedToolchainIdentity = prepared is null ? existing.PreparedToolchainIdentity : preparedToolchainIdentity,
                    SourceIds = existing.SourceIds.Append(sourceId).Distinct().ToList() })
                : throw new IOException(LocalizationText.Get("Aac.Plan.SourceError")));
    }

    // The caller has completed the existing verified-copy boundary; new project SourceIds may differ.
    public void RebindVerifiedCopy(string sourcePath, string targetPath, Guid sourceId)
    {
        if (!File.Exists(sourcePath)) return; // No trustworthy source identity: keep the normal cache-miss path.
        var sourceIdentity = AudioEvidenceFileIdentity.Read(sourcePath);
        if (!_entries.TryGetValue(sourceIdentity.CacheKey, out var source) || !source.Source.Matches(sourceIdentity) ||
            source.Integrity != Seal(source).Integrity) return;
        var targetIdentity = AudioEvidenceFileIdentity.Read(targetPath);
        if (targetIdentity.Length != source.Source.Length) throw new IOException(LocalizationText.Get("Aac.Plan.SourceError"));
        var probe = Clone(source.Probe); probe.FilePath = targetPath;
        _entries[targetIdentity.CacheKey] = Seal(source with { Source = targetIdentity, Probe = probe,
            SourceIds = source.SourceIds.Append(sourceId).Distinct().ToList() });
    }

    // Called only by the successful source-acquisition owner, after its verified copy boundary.
    public void RebindVerifiedCopies(IEnumerable<TrackInfo> tracks, string root)
    {
        foreach (var track in tracks)
        {
            var source = _entries.Values.FirstOrDefault(entry => entry.SourceIds.Contains(track.SourceId));
            if (source is null) continue;
            var target = TrackPathService.GetTrackPath(root, track);
            if (track.IsExcluded && !File.Exists(target)) continue;
            var identity = AudioEvidenceFileIdentity.Read(target);
            if (_entries.TryGetValue(identity.CacheKey, out var current) && current.Source.Matches(identity) &&
                current.SourceIds.Contains(track.SourceId)) continue;
            if (identity.Length != source.Source.Length) throw new IOException(LocalizationText.Get("Aac.Plan.SourceError"));
            var probe = Clone(source.Probe);
            probe.FilePath = target;
            _entries[identity.CacheKey] = Seal(source with { Source = identity, Probe = probe });
        }
    }

    public void Save(string projectFolder, string fileName = "audio-evidence.json")
    {
        var path = System.IO.Path.Combine(ProjectFolderLayout.GetSettingsFolder(projectFolder), fileName);
        new AtomicJsonStore().Save(path, _entries.Values.ToArray(), Json);
    }

    public static AudioEvidenceCache Load(string projectFolder, string fileName = "audio-evidence.json")
    {
        var cache = new AudioEvidenceCache();
        var path = System.IO.Path.Combine(ProjectFolderLayout.GetSettingsFolder(projectFolder), fileName);
        if (!File.Exists(path)) return cache;
        try
        {
            foreach (var entry in JsonSerializer.Deserialize<AudioEvidenceEntry[]>(File.ReadAllText(path), Json) ?? [])
                if (entry.SchemaVersion == 1 && entry.CompleteProbeRequest && entry.Probe?.Success == true && entry.Source is not null)
                    cache._entries[entry.Source.CacheKey] = entry;
        }
        catch (JsonException) { }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        return cache;
    }
}

using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;

namespace BookStitch.Services;

public static class AacToolchainIdentityCache
{
    private static readonly ConcurrentDictionary<string, (AudioEvidenceFileIdentity Identity, string Hash)> Hashes = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Sync = new();

    public static string Identity(string path)
    {
        var identity = AudioEvidenceFileIdentity.Read(path);
        lock (Sync)
        {
            if (identity.UpdateSequenceNumber != 0 && Hashes.TryGetValue(identity.Path, out var cached) && cached.Identity == identity) return cached.Hash;
            using var measurement = PerformanceMeasurement.Start("aac.toolchain.hash");
            using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var hash = Convert.ToHexString(SHA256.HashData(input));
            if (identity != AudioEvidenceFileIdentity.Read(path)) throw new IOException(LocalizationText.Get("Aac.Plan.SourceError"));
            Hashes[identity.Path] = (identity, hash);
            measurement?.Add("Bytes", input.Length);
            return hash;
        }
    }
}

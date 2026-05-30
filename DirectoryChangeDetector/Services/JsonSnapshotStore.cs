using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DirectoryChangeDetector.Interfaces;
using DirectoryChangeDetector.Models;

namespace DirectoryChangeDetector.Services;

// Stores snapshots as JSON, one file per analyzed directory. The file name is a hash of the
// root path, so directories are tracked independently and the snapshot never lands inside the
// analyzed directory (where it would be detected as a new file next run).
public sealed class JsonSnapshotStore(string snapshotsDirectory, ILogger<JsonSnapshotStore> logger)
    : ISnapshotStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<DirectorySnapshot?> LoadAsync(string normalizedRootPath, CancellationToken cancellationToken = default)
    {
        var file = GetSnapshotFilePath(normalizedRootPath);
        if (!File.Exists(file))
        {
            return null;
        }

        try
        {
            await using var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            return await JsonSerializer.DeserializeAsync<DirectorySnapshot>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException ex)
        {
            // A corrupt snapshot must not break analysis; treat it as "no baseline".
            logger.LogWarning(ex, "Snapshot file {File} is corrupt; treating as first run.", file);
            return null;
        }
    }

    public async Task SaveAsync(DirectorySnapshot snapshot, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(snapshotsDirectory);

        var file = GetSnapshotFilePath(snapshot.RootPath);
        await using var stream = new FileStream(
            path: file, 
            mode: FileMode.Create, 
            access: FileAccess.Write,
            share: FileShare.None, 
            bufferSize: 4096,
            options: FileOptions.Asynchronous);
        await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions, cancellationToken);
    }

    private string GetSnapshotFilePath(string normalizedRootPath)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRootPath.ToLowerInvariant()));
        var fileName = Convert.ToHexString(hash).ToLowerInvariant() + ".json";
        return Path.Combine(snapshotsDirectory, fileName);
    }
}

using System.Security.Cryptography;
using DirectoryChangeDetector.Interfaces;

namespace DirectoryChangeDetector.Services;

// Streams the file from disk and returns its hex SHA-256, so large files aren't buffered.
public sealed class Sha256FileHasher : IFileHasher
{
    private const int BufferSize = 64 * 1024;

    public async Task<string> ComputeHashAsync(string absolutePath, CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            absolutePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }
}

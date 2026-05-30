using DirectoryChangeDetector.Models;

namespace DirectoryChangeDetector.Interfaces;

public interface ISnapshotStore
{
    /// <returns>The most recent snapshot for the path, or <c>null</c> if it was never analyzed.</returns>
    Task<DirectorySnapshot?> LoadAsync(string normalizedRootPath, CancellationToken cancellationToken = default);

    Task SaveAsync(DirectorySnapshot snapshot, CancellationToken cancellationToken = default);
}

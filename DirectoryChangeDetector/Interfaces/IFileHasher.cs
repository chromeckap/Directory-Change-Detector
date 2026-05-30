namespace DirectoryChangeDetector.Interfaces;

public interface IFileHasher
{
    Task<string> ComputeHashAsync(string absolutePath, CancellationToken cancellationToken = default);
}

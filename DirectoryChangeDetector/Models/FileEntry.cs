namespace DirectoryChangeDetector.Models;

public sealed record FileEntry
{
    public required string RelativePath { get; init; }   // snapshot key: relative to the root, '/'-separated
    public required string Hash { get; init; }           // hex SHA-256 of the content
    public int Version { get; init; }                    // 1 when new, +1 on each content change
}

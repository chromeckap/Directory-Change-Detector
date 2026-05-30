namespace DirectoryChangeDetector.Models;

public sealed class DirectorySnapshot
{
    public required string RootPath { get; init; }
    public DateTimeOffset ScannedAt { get; init; }
    public List<FileEntry> Files { get; init; } = [];

    // Tracked explicitly so a removed (even empty) subdirectory can be reported, not just inferred.
    public List<string> Directories { get; init; } = [];
}

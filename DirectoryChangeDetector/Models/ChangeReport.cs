namespace DirectoryChangeDetector.Models;

public sealed class ChangeReport
{
    // True on the very first scan: only a baseline is recorded, so all change lists stay empty.
    public bool IsFirstRun { get; init; }
    public int IndexedFileCount { get; init; }
    public IReadOnlyList<FileChange> NewFiles { get; init; } = [];
    public IReadOnlyList<FileChange> ChangedFiles { get; init; } = [];
    public IReadOnlyList<string> DeletedFiles { get; init; } = [];
    public IReadOnlyList<string> DeletedDirectories { get; init; } = [];
    public DateTimeOffset ScannedAt { get; init; }
}

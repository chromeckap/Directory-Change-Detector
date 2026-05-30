namespace DirectoryChangeDetector.Models;

public sealed record FileChange
(
    string Path, 
    int Version
);
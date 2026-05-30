namespace DirectoryChangeDetector.Services;

public static class PathUtilities
{
    // Case-insensitive on every OS for stable, portable snapshots (Windows is the primary
    // target). Trade-off: on a case-sensitive FS "a.txt" and "A.txt" collide
    public static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;

    public static string NormalizeRoot(string fullPath) =>
        fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    // Snapshot key: path relative to the root, '/'-separated (stable across OS).
    public static string ToRelativeKey(string root, string fullPath) =>
        Path.GetRelativePath(root, fullPath).Replace('\\', '/');
}

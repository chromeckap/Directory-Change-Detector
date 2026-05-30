using DirectoryChangeDetector.Exceptions;
using DirectoryChangeDetector.Interfaces;
using DirectoryChangeDetector.Models;

namespace DirectoryChangeDetector.Services;

public sealed class DirectoryAnalyzer(
    IFileHasher hasher,
    ISnapshotStore store,
    ILogger<DirectoryAnalyzer> logger)
    : IDirectoryAnalyzer
{
    public async Task<ChangeReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default)
    {
        var root = ValidateAndNormalize(path);

        var current = await ScanAsync(root, cancellationToken);
        var previous = await store.LoadAsync(root, cancellationToken);
        var scannedAt = DateTimeOffset.UtcNow;

        return previous is null
            ? await CreateBaselineAsync(root, current, scannedAt, cancellationToken)
            : await CompareAsync(root, previous, current, scannedAt, cancellationToken);
    }

    private static string ValidateAndNormalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new DirectoryAnalysisException("Path must not be empty.");
        }

        if (!Path.IsPathRooted(path))
        {
            throw new DirectoryAnalysisException($"Path must be absolute: '{path}'.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new DirectoryAnalysisException($"Path is not valid: '{path}'.", ex);
        }

        if (!Directory.Exists(fullPath))
        {
            throw new DirectoryAnalysisException($"Directory does not exist: '{fullPath}'.");
        }

        return PathUtilities.NormalizeRoot(fullPath);
    }

    private async Task<ScanResult> ScanAsync(string root, CancellationToken cancellationToken)
    {
        var files = new Dictionary<string, string>(PathUtilities.Comparer);
        var directories = new List<string>();
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();

            string[] subDirectories;
            string[] directoryFiles;
            try
            {
                subDirectories = Directory.GetDirectories(directory);
                directoryFiles = Directory.GetFiles(directory);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                // Skip an unreadable sub-directory rather than failing the whole scan.
                logger.LogWarning(ex, "Skipping inaccessible directory: {Directory}", directory);
                continue;
            }

            foreach (var subDirectory in subDirectories)
            {
                directories.Add(PathUtilities.ToRelativeKey(root, subDirectory));
                pending.Push(subDirectory);
            }

            foreach (var file in directoryFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await HashFileInto(root, file, files, cancellationToken);
            }
        }

        return new ScanResult(files, directories);
    }

    private async Task HashFileInto(
        string root,
        string file,
        Dictionary<string, string> files,
        CancellationToken cancellationToken)
    {
        try
        {
            files[PathUtilities.ToRelativeKey(root, file)] = await hasher.ComputeHashAsync(file, cancellationToken);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            // Locked/unreadable file: skip it so the whole analysis doesn't fail.
            logger.LogWarning(ex, "Skipping unreadable file: {File}", file);
        }
    }

    // First run: record everything at version 1 but report no changes (baseline only).
    private async Task<ChangeReport> CreateBaselineAsync(
        string root, ScanResult current, DateTimeOffset scannedAt, CancellationToken cancellationToken)
    {
        var entries = current.Files
            .Select(file => new FileEntry { RelativePath = file.Key, Hash = file.Value, Version = 1 })
            .ToList();

        await SaveSnapshotAsync(root, entries, current.Directories, scannedAt, cancellationToken);

        return new ChangeReport
        {
            IsFirstRun = true,
            IndexedFileCount = entries.Count,
            ScannedAt = scannedAt,
        };
    }

    private async Task<ChangeReport> CompareAsync(
        string root,
        DirectorySnapshot previous,
        ScanResult current,
        DateTimeOffset scannedAt,
        CancellationToken cancellationToken)
    {
        var previousFiles = previous.Files.ToDictionary(file => file.RelativePath, PathUtilities.Comparer);

        var newFiles = new List<FileChange>();
        var changedFiles = new List<FileChange>();
        var entries = new List<FileEntry>(current.Files.Count);

        foreach (var (relativePath, hash) in current.Files)
        {
            if (!previousFiles.TryGetValue(relativePath, out var existing))
            {
                // New (a deleted-then-recreated file restarts here too, at version 1).
                entries.Add(new FileEntry { RelativePath = relativePath, Hash = hash, Version = 1 });
                newFiles.Add(new FileChange(relativePath, 1));
            }
            else if (!string.Equals(existing.Hash, hash, StringComparison.OrdinalIgnoreCase))
            {
                var version = existing.Version + 1;
                entries.Add(new FileEntry { RelativePath = relativePath, Hash = hash, Version = version });
                changedFiles.Add(new FileChange(relativePath, version));
            }
            else
            {
                entries.Add(existing); // unchanged: keep the existing entry and version
            }
        }

        var deletedFiles = previousFiles.Keys
            .Where(key => !current.Files.ContainsKey(key))
            .OrderBy(key => key, PathUtilities.Comparer)
            .ToList();

        var currentDirectories = new HashSet<string>(current.Directories, PathUtilities.Comparer);
        var deletedDirectories = previous.Directories
            .Where(directory => !currentDirectories.Contains(directory))
            .OrderBy(directory => directory, PathUtilities.Comparer)
            .ToList();

        await SaveSnapshotAsync(root, entries, current.Directories, scannedAt, cancellationToken);

        return new ChangeReport
        {
            IsFirstRun = false,
            IndexedFileCount = entries.Count,
            NewFiles = newFiles.OrderBy(file => file.Path, PathUtilities.Comparer).ToList(),
            ChangedFiles = changedFiles.OrderBy(file => file.Path, PathUtilities.Comparer).ToList(),
            DeletedFiles = deletedFiles,
            DeletedDirectories = deletedDirectories,
            ScannedAt = scannedAt,
        };
    }

    private Task SaveSnapshotAsync(
        string root,
        List<FileEntry> entries,
        List<string> directories,
        DateTimeOffset scannedAt,
        CancellationToken cancellationToken)
    {
        var snapshot = new DirectorySnapshot
        {
            RootPath = root,
            ScannedAt = scannedAt,
            Files = entries,
            Directories = directories,
        };

        return store.SaveAsync(snapshot, cancellationToken);
    }

    private readonly record struct ScanResult(
        Dictionary<string, string> Files,
        List<string> Directories);
}

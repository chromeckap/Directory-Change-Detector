using DirectoryChangeDetector.Exceptions;
using DirectoryChangeDetector.Interfaces;
using DirectoryChangeDetector.Models;
using DirectoryChangeDetector.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace DirectoryChangeDetector.Tests;

public sealed class DirectoryAnalyzerTests
{
    private readonly Mock<IFileHasher> _hasher = new();
    private readonly Mock<ISnapshotStore> _store = new();
    private DirectorySnapshot? _saved;

    public DirectoryAnalyzerTests()
    {
        // The "hash" of a file is simply its text content, so writing different content
        // produces a different hash and tests can control hashes precisely.
        _hasher
            .Setup(h => h.ComputeHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((path, _) => Task.FromResult(File.ReadAllText(path)));

        // Capture whatever snapshot the analyzer persists.
        _store
            .Setup(s => s.SaveAsync(It.IsAny<DirectorySnapshot>(), It.IsAny<CancellationToken>()))
            .Callback<DirectorySnapshot, CancellationToken>((snapshot, _) => _saved = snapshot)
            .Returns(Task.CompletedTask);
    }

    private DirectoryAnalyzer CreateAnalyzer() =>
        new(_hasher.Object, _store.Object, NullLogger<DirectoryAnalyzer>.Instance);

    private void SetupPreviousSnapshot(string root, IEnumerable<FileEntry> files, IEnumerable<string>? directories = null)
    {
        var snapshot = new DirectorySnapshot
        {
            RootPath = root,
            ScannedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            Files = files.ToList(),
            Directories = (directories ?? []).ToList(),
        };

        _store
            .Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(snapshot);
    }

    private void SetupNoPreviousSnapshot() =>
        _store
            .Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DirectorySnapshot?)null);

    [Fact]
    public async Task FirstRun_WithNoPreviousSnapshot_CreatesBaselineAndReportsNoChanges()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", "alpha");
        dir.WriteFile("sub/b.txt", "beta");
        SetupNoPreviousSnapshot();

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        Assert.True(report.IsFirstRun);
        Assert.Equal(2, report.IndexedFileCount);
        Assert.Empty(report.NewFiles);
        Assert.Empty(report.ChangedFiles);
        Assert.Empty(report.DeletedFiles);
        Assert.Empty(report.DeletedDirectories);

        Assert.NotNull(_saved);
        Assert.All(_saved!.Files, f => Assert.Equal(1, f.Version));
    }

    [Fact]
    public async Task NewFileAddedAfterBaseline_IsReportedAsNewWithVersionOne()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", "alpha");
        dir.WriteFile("b.txt", "beta");
        SetupPreviousSnapshot(dir.Path, [Entry("a.txt", "alpha", version: 1)]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        Assert.False(report.IsFirstRun);
        var added = Assert.Single(report.NewFiles);
        Assert.Equal("b.txt", added.Path);
        Assert.Equal(1, added.Version);
        Assert.Empty(report.ChangedFiles);
        Assert.Empty(report.DeletedFiles);
    }

    [Fact]
    public async Task UnchangedFile_IsNotReportedAndKeepsItsVersion()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", "alpha");
        SetupPreviousSnapshot(dir.Path, [Entry("a.txt", "alpha", version: 3)]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        Assert.Empty(report.NewFiles);
        Assert.Empty(report.ChangedFiles);
        Assert.Empty(report.DeletedFiles);

        var persisted = Assert.Single(_saved!.Files);
        Assert.Equal("a.txt", persisted.RelativePath);
        Assert.Equal(3, persisted.Version);
    }

    [Fact]
    public async Task ChangedContent_IsReportedAsChangedWithIncrementedVersion()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", "new-content");
        SetupPreviousSnapshot(dir.Path, [Entry("a.txt", "old-content", version: 2)]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        var changed = Assert.Single(report.ChangedFiles);
        Assert.Equal("a.txt", changed.Path);
        Assert.Equal(3, changed.Version);
        Assert.Empty(report.NewFiles);
        Assert.Empty(report.DeletedFiles);
    }

    [Fact]
    public async Task DeletedFile_IsReportedAsDeleted()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", "alpha");
        SetupPreviousSnapshot(dir.Path,
        [
            Entry("a.txt", "alpha", version: 1),
            Entry("gone.txt", "whatever", version: 1),
        ]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        var deleted = Assert.Single(report.DeletedFiles);
        Assert.Equal("gone.txt", deleted);
        Assert.Empty(report.NewFiles);
        Assert.Empty(report.ChangedFiles);
    }

    [Fact]
    public async Task DeletedSubDirectory_IsReportedAsDeletedDirectory()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", "alpha");
        SetupPreviousSnapshot(
            dir.Path,
            [Entry("a.txt", "alpha", version: 1)],
            directories: ["sub/removed"]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        var deletedDir = Assert.Single(report.DeletedDirectories);
        Assert.Equal("sub/removed", deletedDir);
        Assert.Empty(report.DeletedFiles);
    }

    [Fact]
    public async Task RecreatedFile_AbsentFromPreviousSnapshot_IsTreatedAsNewWithVersionOne()
    {
        // A file deleted in an earlier run is no longer in the previous snapshot. When it
        // reappears with the same name it must restart at version 1, not resume its old version.
        using var dir = new TempDirectory();
        dir.WriteFile("recreated.txt", "fresh");
        SetupPreviousSnapshot(dir.Path, [Entry("other.txt", "x", version: 7)]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        var added = Assert.Single(report.NewFiles);
        Assert.Equal("recreated.txt", added.Path);
        Assert.Equal(1, added.Version);
    }

    [Fact]
    public async Task EmptyPath_ThrowsDirectoryAnalysisException()
    {
        await Assert.ThrowsAsync<DirectoryAnalysisException>(
            () => CreateAnalyzer().AnalyzeAsync("   "));
    }

    [Fact]
    public async Task NonExistentDirectory_ThrowsDirectoryAnalysisException()
    {
        var missing = Path.Combine(Path.GetTempPath(), "dcd-missing-" + Guid.NewGuid().ToString("N"));

        await Assert.ThrowsAsync<DirectoryAnalysisException>(
            () => CreateAnalyzer().AnalyzeAsync(missing));
    }

    [Fact]
    public async Task RelativePath_ThrowsDirectoryAnalysisException()
    {
        await Assert.ThrowsAsync<DirectoryAnalysisException>(
            () => CreateAnalyzer().AnalyzeAsync("some/relative/dir"));
    }

    [Fact]
    public async Task UnreadableFile_IsSkipped_WithoutFailingTheAnalysis()
    {
        // A locked/unreadable file is skipped (logged), not crashing the scan. Because it
        // can't be hashed this run it drops out of the current set and is reported as deleted.
        using var dir = new TempDirectory();
        dir.WriteFile("locked.txt", "content");
        SetupPreviousSnapshot(dir.Path, [Entry("locked.txt", "content", version: 4)]);

        _hasher
            .Setup(h => h.ComputeHashAsync(
                It.Is<string>(p => p.EndsWith("locked.txt")), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new IOException("file is locked"));

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        Assert.Equal(new[] { "locked.txt" }, report.DeletedFiles);
        Assert.Empty(report.NewFiles);
        Assert.Empty(report.ChangedFiles);
    }

    [Fact]
    public async Task SameFileNameInDifferentDirectories_AreTrackedIndependently()
    {
        // The snapshot key is the full relative path, so identical file names under different
        // sub-directories must not collide: changing one must not affect the other.
        using var dir = new TempDirectory();
        dir.WriteFile("sub-a/c.txt", "edited");   // differs from previous -> changed
        dir.WriteFile("sub-b/c.txt", "same");     // unchanged

        SetupPreviousSnapshot(dir.Path,
        [
            Entry("sub-a/c.txt", "original", version: 1),
            Entry("sub-b/c.txt", "same", version: 5),
        ]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        var changed = Assert.Single(report.ChangedFiles);
        Assert.Equal("sub-a/c.txt", changed.Path);
        Assert.Equal(2, changed.Version);
        Assert.Empty(report.NewFiles);
        Assert.Empty(report.DeletedFiles);

        // The same-named file in the other directory is untouched and keeps its version.
        var keptB = Assert.Single(_saved!.Files, f => f.RelativePath == "sub-b/c.txt");
        Assert.Equal(5, keptB.Version);
    }

    [Fact]
    public async Task DeletingOneOfTwoSameNamedFiles_OnlyReportsThatOne()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("sub-b/c.txt", "same");     // sub-a/c.txt removed from disk

        SetupPreviousSnapshot(dir.Path,
        [
            Entry("sub-a/c.txt", "x", version: 1),
            Entry("sub-b/c.txt", "same", version: 1),
        ]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        var deleted = Assert.Single(report.DeletedFiles);
        Assert.Equal("sub-a/c.txt", deleted);
        Assert.Empty(report.ChangedFiles);
        Assert.Empty(report.NewFiles);
    }

    [Fact]
    public async Task MixedChanges_AreAllReportedInASingleRun()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("keep.txt", "same");        // unchanged
        dir.WriteFile("edit.txt", "new-body");    // changed
        dir.WriteFile("add.txt", "brand-new");    // new
        // gone.txt and directory "old" exist only in the previous snapshot

        SetupPreviousSnapshot(dir.Path,
        [
            Entry("keep.txt", "same", version: 2),
            Entry("edit.txt", "old-body", version: 2),
            Entry("gone.txt", "x", version: 1),
        ],
        directories: ["old"]);

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        Assert.Equal(new[] { "add.txt" }, report.NewFiles.Select(f => f.Path));
        var edited = Assert.Single(report.ChangedFiles);
        Assert.Equal("edit.txt", edited.Path);
        Assert.Equal(3, edited.Version);
        Assert.Equal(new[] { "gone.txt" }, report.DeletedFiles);
        Assert.Equal(new[] { "old" }, report.DeletedDirectories);
    }

    [Fact]
    public async Task Versioning_IsCumulativeAcrossMultipleRuns()
    {
        using var dir = new TempDirectory();
        dir.WriteFile("a.txt", "v1");

        // Round-trip: feed the snapshot we just saved back in as the previous one.
        _store
            .Setup(s => s.LoadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _saved);

        var analyzer = CreateAnalyzer();

        await analyzer.AnalyzeAsync(dir.Path);                 // baseline -> v1
        dir.WriteFile("a.txt", "v2");
        await analyzer.AnalyzeAsync(dir.Path);                 // change -> v2
        dir.WriteFile("a.txt", "v3");
        var report = await analyzer.AnalyzeAsync(dir.Path);    // change -> v3

        var changed = Assert.Single(report.ChangedFiles);
        Assert.Equal(3, changed.Version);
    }

    [Fact]
    public async Task EmptyDirectory_FirstRun_IndexesNoFiles()
    {
        using var dir = new TempDirectory();
        SetupNoPreviousSnapshot();

        var report = await CreateAnalyzer().AnalyzeAsync(dir.Path);

        Assert.True(report.IsFirstRun);
        Assert.Equal(0, report.IndexedFileCount);
        Assert.Empty(_saved!.Files);
    }

    private static FileEntry Entry(string relativePath, string hash, int version) =>
        new() { RelativePath = relativePath, Hash = hash, Version = version };
}

using DirectoryChangeDetector.Models;
using DirectoryChangeDetector.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace DirectoryChangeDetector.Tests;

public sealed class JsonSnapshotStoreTests
{
    private static JsonSnapshotStore CreateStore(string directory) =>
        new(directory, NullLogger<JsonSnapshotStore>.Instance);

    private static DirectorySnapshot SampleSnapshot(string rootPath) => new()
    {
        RootPath = rootPath,
        ScannedAt = DateTimeOffset.UtcNow,
        Files =
        [
            new FileEntry { RelativePath = "a.txt", Hash = "AAA", Version = 1 },
            new FileEntry { RelativePath = "sub/b.txt", Hash = "BBB", Version = 3 },
        ],
        Directories = ["sub"],
    };

    [Fact]
    public async Task SaveThenLoad_RoundTripsSnapshot()
    {
        using var storeDir = new TempDirectory();
        var store = CreateStore(storeDir.Path);
        var snapshot = SampleSnapshot(@"C:\analyzed\root");

        await store.SaveAsync(snapshot);
        var loaded = await store.LoadAsync(snapshot.RootPath);

        Assert.NotNull(loaded);
        Assert.Equal(snapshot.RootPath, loaded!.RootPath);
        Assert.Equal(2, loaded.Files.Count);
        Assert.Equal(3, loaded.Files.Single(f => f.RelativePath == "sub/b.txt").Version);
        Assert.Equal(new[] { "sub" }, loaded.Directories);
    }

    [Fact]
    public async Task Load_WhenNothingSaved_ReturnsNull()
    {
        using var storeDir = new TempDirectory();
        var store = CreateStore(storeDir.Path);

        var loaded = await store.LoadAsync(@"C:\never\analyzed");

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Load_WhenSnapshotFileIsCorrupt_ReturnsNull()
    {
        using var storeDir = new TempDirectory();
        var store = CreateStore(storeDir.Path);
        var snapshot = SampleSnapshot(@"C:\analyzed\root");
        await store.SaveAsync(snapshot);

        // Corrupt the persisted file; the store must treat this as "no baseline", not throw.
        var file = Directory.GetFiles(storeDir.Path, "*.json").Single();
        await File.WriteAllTextAsync(file, "{ this is not valid json");

        var loaded = await store.LoadAsync(snapshot.RootPath);

        Assert.Null(loaded);
    }

    [Fact]
    public async Task Save_WritesOutsideTheAnalyzedDirectory()
    {
        using var analyzedDir = new TempDirectory();   // stands in for the scanned root
        using var storeDir = new TempDirectory();       // separate snapshot location
        var store = CreateStore(storeDir.Path);

        await store.SaveAsync(SampleSnapshot(analyzedDir.Path));

        Assert.Empty(Directory.GetFiles(analyzedDir.Path));          // nothing written into the scanned dir
        Assert.Single(Directory.GetFiles(storeDir.Path, "*.json"));  // snapshot lives in the store dir
    }

    [Fact]
    public async Task DifferentRoots_AreStoredAndLoadedIndependently()
    {
        using var storeDir = new TempDirectory();
        var store = CreateStore(storeDir.Path);

        await store.SaveAsync(SampleSnapshot(@"C:\root\one"));
        var other = SampleSnapshot(@"C:\root\two");
        other.Files.Clear();
        await store.SaveAsync(other);

        var one = await store.LoadAsync(@"C:\root\one");
        var two = await store.LoadAsync(@"C:\root\two");

        Assert.Equal(2, one!.Files.Count);
        Assert.Empty(two!.Files);
        Assert.Equal(2, Directory.GetFiles(storeDir.Path, "*.json").Length);
    }
}

using DirectoryChangeDetector.Services;

namespace DirectoryChangeDetector.Tests;

public sealed class Sha256FileHasherTests
{
    [Fact]
    public async Task ComputeHash_MatchesKnownSha256Vector()
    {
        using var dir = new TempDirectory();
        var file = dir.WriteFile("abc.txt", "abc");

        var hash = await new Sha256FileHasher().ComputeHashAsync(file);

        // Well-known digest of the UTF-8 bytes "abc".
        Assert.Equal(
            "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
            hash,
            ignoreCase: true);
    }

    [Fact]
    public async Task ComputeHash_DiffersWhenContentDiffers()
    {
        using var dir = new TempDirectory();
        var first = dir.WriteFile("one.txt", "hello");
        var second = dir.WriteFile("two.txt", "world");
        var hasher = new Sha256FileHasher();

        var firstHash = await hasher.ComputeHashAsync(first);
        var secondHash = await hasher.ComputeHashAsync(second);

        Assert.NotEqual(firstHash, secondHash);
    }
}

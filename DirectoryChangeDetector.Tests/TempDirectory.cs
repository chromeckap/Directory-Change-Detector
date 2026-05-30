namespace DirectoryChangeDetector.Tests;

/// <summary>
/// A throwaway directory under the system temp folder, deleted on dispose. Used so the
/// analyzer can run its real file-system enumeration against controlled content while the
/// snapshot store and hasher are mocked.
/// </summary>
internal sealed class TempDirectory : IDisposable
{
    public string Path { get; }

    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dcd-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string WriteFile(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    public void CreateSubDirectory(string relativePath) =>
        Directory.CreateDirectory(System.IO.Path.Combine(Path, relativePath));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; a leaked temp dir must not fail the test run.
        }
    }
}

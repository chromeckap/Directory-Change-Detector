using DirectoryChangeDetector.Models;

namespace DirectoryChangeDetector.Interfaces;

public interface IDirectoryAnalyzer
{
    /// <exception cref="DirectoryChangeDetector.Exceptions.DirectoryAnalysisException">
    /// The path is empty, relative, malformed, missing, or inaccessible.</exception>
    Task<ChangeReport> AnalyzeAsync(string path, CancellationToken cancellationToken = default);
}

namespace DirectoryChangeDetector.Exceptions;

// Caller-facing input problem (bad/relative/missing path, no access); mapped to HTTP 400.
public sealed class DirectoryAnalysisException : Exception
{
    public DirectoryAnalysisException(string message) : base(message) { }

    public DirectoryAnalysisException(string message, Exception innerException)
        : base(message, innerException) { }
}

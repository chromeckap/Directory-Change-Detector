# Directory Change Detector

ASP.NET Core app (REST API + static frontend) that detects changes in a local directory between manually triggered analyses: **new** files, **changed** files (by content), **deleted** files and subdirectories, and a per-file **version** number.
- **Content change = SHA-256 hash**, not timestamp or size.
- **Comparison structure = `Dictionary<relative path, FileEntry>`**. The key is the full path relative to the root, normalized to `/` - so same-named files in different subdirectories (`sub-a/c.txt` vs `sub-b/c.txt`) don't clash. No tree structure (unnecessary at this scope).
- **The snapshot is stored outside the analyzed directory** - under `data/snapshots/` below the content root (configurable via `SnapshotStore:Directory` in `appsettings.json`). The file name is a hash of the normalized path, so multiple directories can be tracked independently and the snapshot is never detected as a "new file" itself.
- **Subdirectories are tracked explicitly** in the snapshot, so a removed (even empty) subdirectory can be reported rather than only inferred from files.
- **Versioning**: new file -> `1`; hash changed -> `previous + 1`; unchanged -> keeps its version.
- **First-run semantics**: when no previous snapshot exists, the run only establishes a baseline - everything gets version 1, the change lists are empty, and the response has `isFirstRun: true`. This mirrors the assignment's distinction between "analyzes on the first run" and "reports changes on subsequent runs".


## Limitations

1. **Reads any path the caller gives** - path-traversal / info-disclosure risk; in production restrict to an allow-list.
2. **New empty subdirectories are not reported** - only new files are (a new folder shows up via its files).
3. **Only the diff since the last run is shown** - the first run just records a baseline.
4. **Renames are not detected** - a rename is a delete + a new file (version restarts at 1).
5. **Version goes up by 1 per run, not per change** - a consequence of manual, not continuous, scanning.
6. **Symlinks/junctions are followed like normal directories** - a self-referential junction could loop.
7. **Best-effort consistency** - the tree may change mid-scan; an unreadable file is skipped.
8. **No concurrency protection** - single-user tool; concurrent runs on the same path could corrupt the snapshot.

## How to run

Requires the .NET SDK 10.

```bash
# from the repo root
dotnet run --project DirectoryChangeDetector
```

Then open the URL from the console (<http://localhost:5281/>). Enter an **absolute** directory path, click **Analyze**, and click again after changing the directory's contents to see the diff.

Run the tests:

```bash
dotnet test
```

### REST API

`POST /api/v1/analyze` - `v1` is just a path convention (in case the contract changes); full versioning (`Asp.Versioning`) would be overkill for a single endpoint.

```jsonc
// request
// forward slashes work too: "C:/some/path"
{ "path": "C:\\some\\absolute\\path" }

// 200 - first run (baseline only, no changes reported)
{
    "isFirstRun": true,
    "indexedFileCount": 12,
    "newFiles": [],
    "changedFiles": [],
    "deletedFiles": [],
    "deletedDirectories": [],
    "scannedAt": "2026-05-30T12:00:00+00:00"
}

// 200 - subsequent run
{ 
    "isFirstRun": false,
    "newFiles": [{ "path": "sub/a.txt", "version": 1 }],
    "changedFiles": [{ "path": "b.txt", "version": 3 }],
    "deletedFiles": ["old.txt"],
    "deletedDirectories": ["sub/removed"],
    "indexedFileCount": 5,
    "scannedAt": "2026-05-30T12:00:00+00:00"
}
```

A missing, malformed, or inaccessible path returns **400** with a problem detail.

## Solution overview

A single ASP.NET Core web project split into folders, plus a unit-test project. Dependency injection and small interfaces keep the logic testable without extra ceremony.

```
DirectoryChangeDetector/             # ASP.NET Core Web API + static frontend
  Program.cs                         # DI registration, HTTP pipeline
  Controllers/AnalyzeController.cs   # thin: validate -> service -> map to HTTP
  Interfaces/    IFileHasher, ISnapshotStore, IDirectoryAnalyzer
  Models/        FileEntry, DirectorySnapshot, ChangeReport, FileChange
  Exceptions/    DirectoryAnalysisException
  Services/      DirectoryAnalyzer, JsonSnapshotStore, Sha256FileHasher, PathUtilities
  wwwroot/index.html                 # plain HTML + JS (fetch), no framework
DirectoryChangeDetector.Tests/       # xUnit + Moq
```

Key interfaces (registered in `Program.cs`, all mockable):

- **`IFileHasher` -> `Sha256FileHasher`** - streamed SHA-256 over a `FileStream`, so even a 50 MB file is never loaded into memory in full.
- **`ISnapshotStore` -> `JsonSnapshotStore`** - load/save the snapshot as JSON. The interface hides persistence, so JSON could be swapped for a DB without touching the logic.
- **`IDirectoryAnalyzer` -> `DirectoryAnalyzer`** - orchestrates: enumerate -> compare -> save.
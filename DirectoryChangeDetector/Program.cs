using DirectoryChangeDetector.Interfaces;
using DirectoryChangeDetector.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Resolve the snapshot folder from configuration; a relative path is taken relative to the
// content root. Defaults to data/snapshots.
var configuredDirectory = builder.Configuration["SnapshotStore:Directory"] ?? Path.Combine("data", "snapshots");
var snapshotsDirectory = Path.IsPathRooted(configuredDirectory)
    ? configuredDirectory
    : Path.Combine(builder.Environment.ContentRootPath, configuredDirectory);

builder.Services.AddSingleton<IFileHasher, Sha256FileHasher>();
builder.Services.AddSingleton<ISnapshotStore>(sp =>
    new JsonSnapshotStore(snapshotsDirectory, sp.GetRequiredService<ILogger<JsonSnapshotStore>>()));
builder.Services.AddSingleton<IDirectoryAnalyzer, DirectoryAnalyzer>();

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

app.Run();
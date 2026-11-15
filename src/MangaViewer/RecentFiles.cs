using System.IO;
using System.Text.Json;

namespace MangaViewer;

public sealed class RecentFiles
{
    private readonly string storagePath;
    public IReadOnlyList<RecentFile> Entries { get; private set; }

    public RecentFiles(string storagePath)
    {
        this.storagePath = storagePath;
        try
        {
            string[] paths = File.Exists(storagePath)
                ? JsonSerializer.Deserialize<string[]>(File.ReadAllText(storagePath)) ?? [] : [];
            Entries = paths.Where(path => !string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).Select(path => new RecentFile(path)).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            Entries = [];
        }
    }

    public void Record(string path)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var paths = new[] { fullPath }.Concat(Entries.Select(entry => entry.FullPath))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(storagePath))!);
        File.WriteAllText(storagePath + ".tmp", JsonSerializer.Serialize(paths));
        File.Move(storagePath + ".tmp", storagePath, true);
        Entries = paths.Select(path => new RecentFile(path)).ToArray();
    }
}

public sealed record RecentFile(string FullPath)
{
    public string Name => Path.GetFileName(FullPath) is { Length: > 0 } name ? name : FullPath;
}

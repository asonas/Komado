using System.IO;
using System.Text.Json;

namespace MangaViewer;

public sealed record WindowPreferences(double Width = 1200, double Height = 850, bool Maximized = false,
    bool Facing = true, bool LeftAdvances = true, bool AutomaticLayout = true)
{
    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MangaViewer", "window.json");

    public static WindowPreferences Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var saved = JsonSerializer.Deserialize<WindowPreferences>(File.ReadAllText(path));
            return saved is not null && double.IsFinite(saved.Width) && double.IsFinite(saved.Height)
                && saved.Width >= 720 && saved.Height >= 400 ? saved : new();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this));
        File.Move(temporary, path, true);
    }
}

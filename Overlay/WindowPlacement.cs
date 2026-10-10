using System.Text.Json;

namespace TerraWikiOverlay;

internal static class WindowPlacement
{
    public static Rectangle? Load(string directory)
    {
        try
        {
            string path = Path.Combine(directory, "window-v1.json");
            if (!File.Exists(path)) return null;
            Rectangle bounds = JsonSerializer.Deserialize<Rectangle>(File.ReadAllText(path));
            return bounds.Width >= 500 && bounds.Height >= 400 ? bounds : null;
        }
        catch { return null; }
    }

    public static void Save(string directory, Rectangle bounds)
    {
        try
        {
            string path = Path.Combine(directory, "window-v1.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(bounds));
            File.Move(path + ".tmp", path, true);
        }
        catch { }
    }
}

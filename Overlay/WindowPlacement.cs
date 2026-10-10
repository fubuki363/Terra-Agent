using System.Text.Json;

namespace TerraWikiOverlay;

internal static class WindowPlacement
{
    private static readonly object SaveLock = new();
    private static long _saveGeneration;
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
        SaveCore(directory, bounds, Interlocked.Increment(ref _saveGeneration));
    }

    public static void SaveInBackground(string directory, Rectangle bounds)
    {
        long generation = Interlocked.Increment(ref _saveGeneration);
        _ = Task.Run(() => SaveCore(directory, bounds, generation));
    }

    private static void SaveCore(string directory, Rectangle bounds, long generation)
    {
        try
        {
            lock (SaveLock)
            {
                if (generation != Volatile.Read(ref _saveGeneration)) return;
                string path = Path.Combine(directory, "window-v1.json");
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(bounds));
                File.Move(path + ".tmp", path, true);
            }
        }
        catch { }
    }
}

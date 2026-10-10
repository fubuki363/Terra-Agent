using System.Text.RegularExpressions;

namespace TerraWikiOverlay;

internal static class AppDiagnostics
{
    public static void Record(Exception exception)
    {
        try
        {
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TerraWikiOverlay");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "errors.log");
            string safe = Regex.Replace(exception.ToString(), @"sk-[A-Za-z0-9_-]+", "[API Key 已隐藏]");
            if (File.Exists(path) && new FileInfo(path).Length > 1_000_000) File.Move(path, path + ".previous", true);
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {safe}{Environment.NewLine}");
        }
        catch { }
    }
}

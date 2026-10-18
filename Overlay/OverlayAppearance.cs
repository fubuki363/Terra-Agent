using Microsoft.Win32;

namespace TerraWikiOverlay;

internal static class OverlayAppearance
{
    // Read the saved/system appearance before the WebView paints its first frame.
    internal static bool IsDark(string mode)
    {
        if (mode == "dark") return true;
        if (mode == "light") return false;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception) { return false; }
    }
}

using System.Runtime.InteropServices;

namespace TerraWikiOverlay;

internal static class OverlayClipboard
{
    public static bool TryCopy(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        try
        {
            // Rich text is deliberately not put on the clipboard: the selected
            // characters are the exact plain text users expect to paste.
            Clipboard.SetDataObject(text, true, 3, 40);
            return true;
        }
        catch (ExternalException) { return false; }
        catch (ThreadStateException) { return false; }
    }
}

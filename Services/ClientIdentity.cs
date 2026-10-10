using System.Reflection;

namespace TerraWikiAI.Services;

internal static class ClientIdentity
{
    public static string UserAgent => "TerraWikiOverlay/" + (typeof(ClientIdentity).Assembly.GetName().Version?.ToString(3) ?? "1.1.0")
        + " (standalone Terraria assistant)";
}

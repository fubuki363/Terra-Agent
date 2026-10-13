using System;

namespace TerraWikiOverlay;

internal static class WikiBrowserNavigation
{
    internal static string Home(bool english) => english ? "https://terraria.wiki.gg/" : "https://terraria.wiki.gg/zh/";
    internal static bool IsWikiPage(string address) => Uri.TryCreate(address, UriKind.Absolute, out Uri? uri)
        && uri.Scheme == Uri.UriSchemeHttps && uri.Host.Equals("terraria.wiki.gg", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && string.IsNullOrEmpty(uri.UserInfo);

    internal static string Page(string title, bool english) => Home(english) + "wiki/" + Uri.EscapeDataString(title.Replace(' ', '_'));

    internal static string Resolve(string text, bool english)
    {
        text = text.Trim();
        if (text.Length == 0) return Home(english);
        if (text.Contains("://", StringComparison.Ordinal) || text.StartsWith("terraria.wiki.gg/", StringComparison.OrdinalIgnoreCase))
        {
            string address = text.Contains("://", StringComparison.Ordinal) ? text : "https://" + text;
            if (!IsWikiPage(address)) throw new InvalidOperationException("图鉴仅打开 HTTPS 的 Terraria 官方 Wiki 页面。请输入关键词或官方 Wiki 地址。");
            return new Uri(address).AbsoluteUri;
        }
        return Home(english) + "wiki/Special:Search?search=" + Uri.EscapeDataString(text) + "&fulltext=1";
    }
}

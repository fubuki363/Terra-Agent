using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Diagnostics.Runtime;

namespace TerraWikiOverlay;

internal static class RuntimeItemCatalog
{
    internal static Dictionary<int, string> Load(ClrModule module, ClrAppDomain domain)
    {
        ClrObject cache = module.GetTypeByName("Terraria.Lang")?.GetStaticFieldByName("_itemNameCache")?.ReadObject(domain) ?? default;
        Dictionary<int, string> names = new();
        if (!cache.IsValid || !cache.IsArray) return names;
        ClrArray array = cache.AsArray();
        for (int id = 1; id < Math.Min(array.Length, 200000); id++)
        {
            ClrObject text = array.GetObjectValue(id);
            if (!text.IsValid) continue;
            foreach (string field in new[] { "_value", "<Value>k__BackingField", "<EnglishValue>k__BackingField" })
            {
                try
                {
                    string? value = text.ReadObjectField(field).AsString(180);
                    if (!string.IsNullOrWhiteSpace(value)) { names[id] = value; break; }
                }
                catch (Exception) { }
            }
        }
        return names;
    }
    internal static IReadOnlyList<KeyValuePair<int, string>> Find(Dictionary<int, string> names, string query, int maximum = 24)
    {
        string key = Normalize(query);
        if (key.Length == 0) return Array.Empty<KeyValuePair<int, string>>();
        if (int.TryParse(key, out int id) && names.TryGetValue(id, out string? name)) return new[] { new KeyValuePair<int, string>(id, name) };
        var exact = names.Where(p => Normalize(p.Value) == key).Take(maximum).ToArray();
        if (exact.Length > 0) return exact;
        return names.Where(p => Normalize(p.Value).Contains(key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => Math.Abs(p.Value.Length - query.Length)).Take(maximum).ToArray();
    }
    private static string Normalize(string text) => new(text.Where(c => !char.IsWhiteSpace(c) && c is not '_' and not '-' and not '(' and not ')').Select(char.ToLowerInvariant).ToArray());
}

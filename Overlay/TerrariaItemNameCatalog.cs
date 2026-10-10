using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;
using Mono.Cecil;

namespace TerraWikiOverlay;

internal static class TerrariaItemNameCatalog
{
    private const string ChineseResourceName = "Terraria.Localization.Content.zh-Hans.Items.json";
    private const string EnglishResourceName = "Terraria.Localization.Content.en-US.Items.json";
    private static readonly Lazy<Catalog> Current = new(Load, LazyThreadSafetyMode.ExecutionAndPublication);

    public static int Count => Current.Value.Names.Count;
    internal static string Diagnostic => Current.Value.Diagnostic;

    public static string? Resolve(int itemId) => Current.Value.Names.TryGetValue(itemId, out string? name)
        ? name
        : null;

    public static IReadOnlyList<KeyValuePair<int, string>> FindByName(string query, int maximum = 24)
    {
        string normalized = NormalizeName(query);
        if (normalized.Length == 0)
            return Array.Empty<KeyValuePair<int, string>>();
        if (int.TryParse(normalized, out int itemId) && Current.Value.Names.TryGetValue(itemId, out string? idName))
            return new[] { new KeyValuePair<int, string>(itemId, idName) };

        List<KeyValuePair<int, string>> exact = Current.Value.Names
            .Where(entry => NormalizeName(entry.Value).Equals(normalized, StringComparison.OrdinalIgnoreCase))
            .Take(maximum)
            .ToList();
        if (exact.Count > 0)
            return exact;

        return Current.Value.Names
            .Where(entry => NormalizeName(entry.Value).Contains(normalized, StringComparison.OrdinalIgnoreCase)
                || normalized.Contains(NormalizeName(entry.Value), StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => Math.Abs(NormalizeName(entry.Value).Length - normalized.Length))
            .ThenBy(entry => entry.Value, StringComparer.Ordinal)
            .Take(Math.Max(1, maximum))
            .ToList();
    }

    private static string NormalizeName(string value) => new(value
        .Where(character => !char.IsWhiteSpace(character) && character is not '·' and not '-' and not '_' and not '（' and not '）' and not '(' and not ')')
        .Select(char.ToLowerInvariant)
        .ToArray());

    private static Catalog Load()
    {
        string? executablePath = FindTerrariaExecutable();
        if (executablePath is null)
            return new Catalog(new Dictionary<int, string>(), "未找到 Terraria.exe");

        try
        {
            using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(executablePath, new ReaderParameters
            {
                ReadingMode = ReadingMode.Deferred,
                ReadSymbols = false
            });
            Dictionary<string, string> localizedNames = ReadLocalizedNames(assembly, ChineseResourceName);
            Dictionary<string, string> englishNames = ReadLocalizedNames(assembly, EnglishResourceName);
            TypeDefinition? itemIdType = assembly.MainModule.GetType("Terraria.ID.ItemID");
            if (itemIdType is null)
                return new Catalog(new Dictionary<int, string>(), "Terraria.ID.ItemID 不存在");

            Dictionary<int, string> names = new();
            foreach (FieldDefinition field in itemIdType.Fields)
            {
                if (!field.HasConstant || field.Constant is null)
                    continue;
                int itemId;
                try { itemId = Convert.ToInt32(field.Constant); }
                catch { continue; }
                if (itemId <= 0 || names.ContainsKey(itemId))
                    continue;
                if (localizedNames.TryGetValue(field.Name, out string? localized) && !string.IsNullOrWhiteSpace(localized))
                    names[itemId] = localized;
                else if (englishNames.TryGetValue(field.Name, out string? english) && !string.IsNullOrWhiteSpace(english))
                    names[itemId] = english;
            }
            return new Catalog(names, $"已从 Terraria.exe 载入 {names.Count} 个物品名称");
        }
        catch (Exception exception)
        {
            return new Catalog(new Dictionary<int, string>(), exception.GetBaseException().GetType().Name + ": " + exception.GetBaseException().Message);
        }
    }

    private static Dictionary<string, string> ReadLocalizedNames(AssemblyDefinition assembly, string resourceName)
    {
        EmbeddedResource? resource = assembly.MainModule.Resources
            .OfType<EmbeddedResource>()
            .FirstOrDefault(candidate => candidate.Name == resourceName);
        if (resource is null)
            return new Dictionary<string, string>(StringComparer.Ordinal);

        using Stream stream = resource.GetResourceStream();
        using JsonDocument document = JsonDocument.Parse(stream, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        if (!document.RootElement.TryGetProperty("ItemName", out JsonElement itemNames))
            return new Dictionary<string, string>(StringComparer.Ordinal);

        Dictionary<string, string> result = new(StringComparer.Ordinal);
        foreach (JsonProperty property in itemNames.EnumerateObject())
        {
            string? value = property.Value.GetString();
            if (!string.IsNullOrWhiteSpace(value))
                result[property.Name] = value;
        }
        return result;
    }

    internal static string? FindTerrariaExecutable()
    {
        foreach (Process process in Process.GetProcessesByName("Terraria"))
        {
            try
            {
                string? path = process.MainModule?.FileName;
                if (IsTerrariaExecutable(path))
                    return path;
            }
            catch { }
            finally { process.Dispose(); }
        }

        string? configured = Environment.GetEnvironmentVariable("TERRARIA_EXE");
        if (IsTerrariaExecutable(configured))
            return configured;
        string adjacent = Path.Combine(AppContext.BaseDirectory, "Terraria.exe");
        if (IsTerrariaExecutable(adjacent))
            return adjacent;

        foreach (string steamRoot in FindSteamRoots())
        {
            string candidate = Path.Combine(steamRoot, "steamapps", "common", "Terraria", "Terraria.exe");
            if (IsTerrariaExecutable(candidate))
                return candidate;
        }
        return null;
    }

    private static IEnumerable<string> FindSteamRoots()
    {
        HashSet<string> roots = new(StringComparer.OrdinalIgnoreCase);
        AddRegistrySteamRoot(roots, Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath");
        AddRegistrySteamRoot(roots, Registry.LocalMachine, @"Software\WOW6432Node\Valve\Steam", "InstallPath");
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFiles))
            roots.Add(Path.Combine(programFiles, "Steam"));

        foreach (string root in roots.ToList())
        {
            string librariesPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(librariesPath))
                continue;
            try
            {
                string content = File.ReadAllText(librariesPath);
                foreach (Match match in Regex.Matches(content, "\\\"path\\\"\\s+\\\"(?<path>[^\\\"]+)\\\"", RegexOptions.IgnoreCase))
                {
                    string value = match.Groups["path"].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(value))
                        roots.Add(value);
                }
            }
            catch { }
        }
        return roots;
    }

    private static void AddRegistrySteamRoot(HashSet<string> roots, RegistryKey hive, string subKey, string valueName)
    {
        try
        {
            using RegistryKey? key = hive.OpenSubKey(subKey, writable: false);
            if (key?.GetValue(valueName) is string value && Directory.Exists(value))
                roots.Add(value.Replace('/', Path.DirectorySeparatorChar));
        }
        catch { }
    }

    private static bool IsTerrariaExecutable(string? path) => !string.IsNullOrWhiteSpace(path)
        && File.Exists(path)
        && string.Equals(Path.GetFileName(path), "Terraria.exe", StringComparison.OrdinalIgnoreCase);

    private sealed record Catalog(IReadOnlyDictionary<int, string> Names, string Diagnostic);
}

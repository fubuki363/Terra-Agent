using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerraWikiOverlay;

internal sealed class OverlaySettings
{
    public int SchemaVersion { get; set; } = 8;
    public bool PersistHistory { get; set; } = true;
    // Kept so existing settings-v1.json files continue to deserialize cleanly.
    public bool IncludeSaveContext { get; set; } = true;
    public bool IncludeMemoryContext { get; set; } = true;
    public bool IncludeHealth { get; set; } = true;
    public bool IncludeMana { get; set; } = true;
    public bool IncludeInventory { get; set; } = true;
    public bool IncludeEquipment { get; set; } = true;
    public bool IncludeWorldInfo { get; set; } = true;
    public bool IncludeProgress { get; set; } = true;
    public bool IncludeAchievements { get; set; } = true;
    public bool EnableStorageLocator { get; set; } = true;
    public bool EnableBiomeLocator { get; set; } = true;
    public bool EnableObjectLocator { get; set; } = true;
    public int ContextTurns { get; set; } = 6;
    public int ContextCharacterBudget { get; set; } = 12000;
    public int HistoryRetentionDays { get; set; } = 30;
    // Legacy fields are retained for clean migration. Agent requests ignore the
    // old output cap and always keep DeepSeek thinking enabled.
    public int MaxOutputTokens { get; set; }
    public bool EnableThinking { get; set; } = true;

    [JsonIgnore]
    public GameContextSelection GameContextSelection => new(
        IncludeHealth,
        IncludeMana,
        IncludeInventory,
        IncludeEquipment,
        IncludeWorldInfo,
        IncludeProgress,
        IncludeAchievements);
}

internal sealed class OverlaySettingsStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public OverlaySettingsStore(string dataDirectory)
    {
        _path = Path.Combine(dataDirectory, "settings-v1.json");
        Current = Load();
        int loadedSchemaVersion = Current.SchemaVersion;
        Normalize(Current);
        if (loadedSchemaVersion < Current.SchemaVersion)
            Save();
    }

    public OverlaySettings Current { get; }

    public void Save()
    {
        Normalize(Current);
        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(Current, _jsonOptions));
        File.Move(temporaryPath, _path, true);
    }

    private OverlaySettings Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<OverlaySettings>(File.ReadAllText(_path), _jsonOptions) ?? new();
        }
        catch
        {
        }
        return new OverlaySettings();
    }

    private static void Normalize(OverlaySettings settings)
    {
        if (settings.SchemaVersion < 3)
            settings.IncludeMemoryContext = true;
        if (settings.SchemaVersion < 5)
        {
            settings.EnableStorageLocator = true;
            settings.EnableBiomeLocator = true;
        }
        if (settings.SchemaVersion < 8) settings.EnableObjectLocator = true;
        settings.SchemaVersion = 8;
        settings.ContextTurns = Math.Clamp(settings.ContextTurns, 0, 20);
        settings.ContextCharacterBudget = Math.Clamp(settings.ContextCharacterBudget, 1000, 50000);
        settings.HistoryRetentionDays = Math.Clamp(settings.HistoryRetentionDays, 1, 365);
        settings.MaxOutputTokens = 0;
        settings.EnableThinking = true;
    }
}

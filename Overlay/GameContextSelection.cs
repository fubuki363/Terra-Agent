namespace TerraWikiOverlay;

internal readonly record struct GameContextSelection(
    bool Health,
    bool Mana,
    bool Inventory,
    bool Equipment,
    bool WorldInfo,
    bool Progress,
    bool Achievements)
{
    public bool Vitals => Health || Mana;
    public bool HasAny => Vitals || Inventory || Equipment || WorldInfo || Progress || Achievements;

    public static GameContextSelection All { get; } = new(true, true, true, true, true, true, true);
}

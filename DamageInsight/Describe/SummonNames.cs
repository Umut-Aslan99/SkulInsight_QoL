using System.Collections.Generic;
using System.Text.RegularExpressions;
using DamageInsight.Lang;

namespace DamageInsight.Describe;

/// <summary>
/// Names for summons the game gives no name (character key "Unspecified"): the descriptions showed their prefab names
/// ("Recruit 4 Swap BuddyArcher", "DavyJones ReadyToShoot Crew Rifle Kneel"; QA 2026-10-11, finding T1). The prefab
/// name minus its skull, level, Swap/Buddy/Variant parts gives a core that this table names. Plain logic, no Unity.
/// </summary>
public static class SummonNames
{
    private static readonly Regex Owner = new(@"^(Recruit|GraveDigger|DavyJones|Prisoner|StoneMonkey)(\s+\d+)?(\s+Swap)?(\s+(PrepareForBattle|ReadyToShoot))?\s+");
    private static readonly Regex Noise = new(@"\bBuddy|\s+Variant\b|\(Parent\)|\s+(Stand|Kneel|Creeping|Low|High)\b|(?<=Bomb)\d+$");

    private static readonly Dictionary<string, string> Table = new()
    {
        ["Recruit"] = Loc.N("Recruit"),
        ["Archer"] = Loc.N("Archer"),
        ["Assassin"] = Loc.N("Assassin"),
        ["ManAtArms"] = Loc.N("Man-at-Arms"),
        ["Hamlet"] = Loc.N("Hamlet"),
        ["KingLear"] = Loc.N("King Lear"),
        ["Macbeth"] = Loc.N("Macbeth"),
        ["Othello"] = Loc.N("Othello"),
        ["Crew"] = Loc.N("Crew"),
        ["Crew Rifle"] = Loc.N("Rifle crew"),
        ["Crew Cannon"] = Loc.N("Cannon crew"),
        ["Crew Bomb"] = Loc.N("Bomb crew"),
        ["DoubleCrossbomatic"] = Loc.N("Double Crossbomatic"),
        ["Bunshin"] = Loc.N("Clone"),
        ["DwarfTurret"] = Loc.N("Turret"),
        ["DwarfTurret Promoted"] = Loc.N("Upgraded turret"),
        ["Turret"] = Loc.N("Turret"),
        ["Bomb"] = Loc.N("Bomb"),
        ["Ice"] = Loc.N("Ice"),
        ["Poision"] = Loc.N("Poison"), // the game's own spelling
        ["Restriction"] = Loc.N("Restriction"),
    };

    /// <summary>The summon's name in the current language, or "" if the table doesn't know it.</summary>
    public static string For(string prefabName)
    {
        if (string.IsNullOrEmpty(prefabName))
            return "";
        string core = prefabName.Replace("(Clone)", "").Replace('_', ' ').Trim();
        core = Owner.Replace(core, "");
        core = Regex.Replace(Noise.Replace(core, ""), @"\s+", " ").Trim();
        return Table.TryGetValue(core, out var english) ? Loc.T(english) : "";
    }
}

using System;
using System.Collections.Generic;
using Platforms;

namespace DamageInsight.Daily;

/// <summary>What the game holds that an evened-out profile sets (read from the game at run time; plain data).</summary>
public sealed class DailyProfileFacts
{
    /// <summary>"gear/unlocked/&lt;Type&gt;/&lt;name&gt;" of every skull, item, quintessence and dark ability that needs unlocking.</summary>
    public readonly List<string> GearUnlockKeys = new();
    /// <summary>"Progress/WitchMastery/&lt;tree&gt;/&lt;n&gt;" → the mastery's top level.</summary>
    public readonly Dictionary<string, int> WitchTop = new();
    /// <summary>The Darktech machines (DarktechData.Type names).</summary>
    public readonly List<string> Darktech = new();
    public int TopDarkMirrorLevel = 10;
}

/// <summary>
/// The daily run's profile (Umut 2026-10-16: the daily is played in its own slot so everyone plays the same run): a copy
/// of the player's own save, evened out so the gear pool and the stats are the same for everybody. Every gear that
/// needs unlocking is unlocked (the pool depends on it: GearManager), the Witch's masteries are at their top, every
/// Darktech machine is unlocked and on, every Dark Mirror level is open, the castle NPCs are rescued, the currencies
/// are at 0 and no run is in progress. Plain logic on the game's save model.
/// </summary>
public static class DailyProfile
{
    public static readonly string[] Rescued =
        { "Progress/foxRescued", "Progress/ogreRescued", "Progress/druidRescued", "Progress/deathknightRescued" };

    public static readonly string[] Currencies = { "gold", "darkQuartz", "bone", "heartQuartz" };

    /// <summary>Dark quartz to start a daily run with, for the Dark Mirror castle's machines (Umut 2026-10-18).</summary>
    public const int StartDarkQuartz = 15_000;

    public static void Even(SaveFileModel save, DailyProfileFacts facts)
    {
        foreach (var key in facts.GearUnlockKeys)
            save.SetBool(key, true);
        foreach (var pair in facts.WitchTop)
            save.SetInt(pair.Key, pair.Value);
        foreach (var machine in facts.Darktech)
        {
            save.SetBool($"HardmodeProgress/_unlocked/{machine}", true);
            save.SetBool($"HardmodeProgress/_activated/{machine}", true);
        }
        save.SetInt("HardmodeProgress/clearedLevel", Math.Max(save.GetInt("HardmodeProgress/clearedLevel", -1), facts.TopDarkMirrorLevel));
        save.SetInt("HardmodeProgress/clearedCount", Math.Max(1, save.GetInt("HardmodeProgress/clearedCount")));
        foreach (var key in Rescued)
            save.SetBool(key, true);
        foreach (var currency in Currencies)
            save.SetInt($"Currency/{currency}/balance", currency == "darkQuartz" ? StartDarkQuartz : 0);
        save.SetBool("Save/hasSave", false);
    }
}

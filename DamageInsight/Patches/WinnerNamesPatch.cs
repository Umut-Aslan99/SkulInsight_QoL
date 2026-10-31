using System.Linq;
using DamageInsight.Daily;
using HarmonyLib;
using UI.Adventurer;

namespace DamageInsight.Patches;

/// <summary>
/// The adventurers carry the names of yesterday's daily run winners (0.16; Umut 2026-10-30: "If Tythos wins 1st place,
/// each other players name for the adventurers should be Tythos. I think it sounds cool and rewarding"). The game
/// picks a name from its list when an adventurer's name tag shows (AdventurerNamePool.OnEnable); a winner's name takes
/// its place. In a daily run the winner of that board comes first. Setting Daily/WinnerNames; needs daily runs on.
/// </summary>
[HarmonyPatch(typeof(AdventurerNamePool), "OnEnable")]
public static class WinnerNamesPatch
{
    private static void Postfix(AdventurerNamePool __instance) => Guard.Run("Winners as adventurers", () =>
    {
        string name = Pick();
        if (name == null || __instance._text == null)
            return;
        Lang.FontFallback.For(ScriptOf(name)); // a Chinese winner's name in an English game
        __instance._text.text = name;
    });

    /// <summary>A winner's name for an adventurer, or null (the game's own name stays).</summary>
    internal static string Pick()
    {
        if (Plugin.DailyWinnerNames?.Value == false || !DailyService.Enabled)
            return null;
        var champions = DailyService.Champions;
        if (champions == null || champions.Count == 0)
            return null;
        if (DailyRun.InDailySlot)
        {
            var playing = DailyService.State.Tries.Values.FirstOrDefault(t => t.State is "armed" or "started");
            var own = champions.FirstOrDefault(c => playing != null && c.board == playing.Board);
            if (own.name is { Length: > 0 })
                return own.name;
        }
        var names = champions.Select(c => c.name).Where(n => n.Length > 0).Distinct().ToList();
        return names.Count == 0 ? null : names[UnityEngine.Random.Range(0, names.Count)];
    }

    /// <summary>The language whose system font draws a name the game's fonts may lack ("" for Latin and the like).</summary>
    internal static string ScriptOf(string name)
    {
        if (name.Any(c => c >= '가' && c <= '힯'))
            return "ko";
        if (name.Any(c => c >= '぀' && c <= 'ヿ'))
            return "ja";
        return name.Any(c => c >= '⺀') ? "zh-Hans" : "";
    }
}

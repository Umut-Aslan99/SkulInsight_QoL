using System;
using System.Collections.Generic;
using System.Linq;

namespace DamageInsight.Daily;

/// <summary>
/// The other mods loaded next to SkulInsight (Umut 2026-10-17: "disqualify points if people use other (imbalanced)
/// mods, such as the community items mod; only vanilla items and skulls should count"). A daily run with other mods
/// is sent with their ids and doesn't count on the boards (the server's DAILY_ALLOWED_MODS can allow harmless ones).
/// </summary>
public static class DailyMods
{
    /// <summary>The other BepInEx plugins: (GUID, name).</summary>
    public static List<(string guid, string name)> Others()
    {
        var list = new List<(string guid, string name)>();
        try
        {
            foreach (var info in BepInEx.Bootstrap.Chainloader.PluginInfos.Values)
            {
                string guid = info.Metadata?.GUID ?? "";
                if (guid.Length == 0 || guid == MyPluginInfo.PLUGIN_GUID)
                    continue;
                list.Add((guid, info.Metadata?.Name ?? guid));
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Daily run: the other mods could not be listed: {e.Message}");
        }
        return list.OrderBy(m => m.guid, StringComparer.Ordinal).ToList();
    }
}

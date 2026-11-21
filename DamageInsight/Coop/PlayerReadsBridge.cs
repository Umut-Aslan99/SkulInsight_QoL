using System;
using System.Reflection;

namespace DamageInsight.Coop;

/// <summary>
/// The plugin's side of the preloader (docs/coop/packages/E.md): finds `SkulInsightCoop.PlayerReads`, which the
/// preloader injects into Assembly-CSharp when `[Coop] Enabled = true`, and checks it is the version and the list of
/// player reads that was reviewed, then sets the Owner and Target resolvers (CoopTargeting); World stays the original
/// read (this game's player). Without a co-op session the resolvers give this game's player too.
/// </summary>
public static class PlayerReadsBridge
{
    public const int ApiVersion = 1;
    /// <summary>The site list reviewed in package E (390 reads, PLAYER_READS.md). A game update that changes it turns
    /// co-op's resolvers off until the list is reviewed again (UPDATE_PLAYER_READS=1, see E.md).</summary>
    public const string ReviewedSitesHash = "2fd2200b15a1d55d";

    /// <summary>The injected type when it is there and matches the review; null otherwise.</summary>
    public static Type Reads { get; private set; }
    public static string Status { get; private set; } = "not checked";

    /// <summary>Whether the injected API can be used (plain logic, tested): null = yes, else why not.</summary>
    public static string Problem(int apiVersion, int unclassifiedSites, string sitesHash)
    {
        if (apiVersion != ApiVersion)
            return $"its API version is {apiVersion}, this SkulInsight needs {ApiVersion}";
        if (unclassifiedSites != 0)
            return $"{unclassifiedSites} player read(s) of this game version are not reviewed yet";
        if (!string.Equals(sitesHash, ReviewedSitesHash, StringComparison.OrdinalIgnoreCase))
            return $"its player reads ({sitesHash}) differ from the reviewed list ({ReviewedSitesHash})";
        return null;
    }

    /// <summary>Looks for the injected type once at startup and logs what it found.</summary>
    public static void Check(bool coopEnabled)
    {
        Type type = null;
        try
        {
            type = typeof(Level.LevelManager).Assembly.GetType("SkulInsightCoop.PlayerReads", false);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("[Coop] preloader check failed: " + e.Message);
        }
        if (type == null)
        {
            Status = coopEnabled ? "missing: the preloader didn't run (patchers/SkulInsight)" : "off";
            if (coopEnabled)
                Plugin.Log.LogWarning("[Coop] [Coop] Enabled is on but the preloader didn't run: " + Status);
            return;
        }
        int api = Constant<int>(type, "ApiVersion", -1);
        int sites = Constant<int>(type, "SiteCount", -1);
        int unclassified = Constant<int>(type, "UnclassifiedSites", -1);
        string hash = Constant(type, "SitesHash", "");
        string problem = Problem(api, unclassified, hash);
        if (problem != null)
        {
            Status = "refused: " + problem;
            Plugin.Log.LogWarning("[Coop] the preloader's player reads can't be used: " + problem + ". Co-op stays off.");
            return;
        }
        Reads = type;
        try
        {
            type.GetField("Owner")?.SetValue(null, (Func<Level.LevelManager, object, int, Characters.Character>)CoopTargeting.ResolveOwner);
            type.GetField("Target")?.SetValue(null, (Func<Level.LevelManager, object, int, Characters.Character>)CoopTargeting.ResolveTarget);
        }
        catch (Exception e)
        {
            Reads = null;
            Status = "refused: the resolvers could not be set (" + e.Message + ")";
            Plugin.Log.LogWarning("[Coop] " + Status);
            return;
        }
        Status = $"ready: {sites} player reads, list {hash}";
        Plugin.Log.LogInfo("[Coop] preloader " + Status + " (Owner and Target resolvers set)");
    }

    private static T Constant<T>(Type type, string name, T fallback)
    {
        try
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Static);
            return field?.GetValue(null) is T value ? value : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }
}

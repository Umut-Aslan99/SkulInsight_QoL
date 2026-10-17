using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Recording;

namespace DamageInsight.Codex;

/// <summary>
/// The damage table's fallback for hits whose origin is only an object's code name: the Codex's gear (skulls, items,
/// quintessences, inscriptions, dark abilities) by its key (OriginMatch). Set as GearTable.ResolveOrigin at start.
/// </summary>
public static class CodexOrigins
{
    private static OriginMatch<CodexEntry> _match;
    private static readonly Dictionary<string, (GearGroup group, string name)?> Cache = new();

    public static void Attach()
    {
        GearTable.ResolveOrigin = Resolve;
        DamageInsight.Lang.Loc.Changed += Cache.Clear; // the names follow the language
    }

    public static (GearGroup group, string name)? Resolve(string origin)
    {
        if (string.IsNullOrEmpty(origin))
            return null;
        if (Cache.TryGetValue(origin, out var known))
            return known;
        (GearGroup group, string name)? result = null;
        try
        {
            _match ??= new OriginMatch<CodexEntry>(CodexCatalog.Entries
                .Where(e => GroupOf(e.Category) != null)
                .Select(e => (e.Key, e)));
            if (_match.Find(origin) is { } entry)
                result = (GroupOf(entry.Category).Value, entry.Name);
        }
        catch (Exception)
        {
            // no catalog yet (title screen): try again next time
            return null;
        }
        Cache[origin] = result;
        return result;
    }

    private static GearGroup? GroupOf(CodexCategory category) => category switch
    {
        CodexCategory.Skulls => GearGroup.Skulls,
        CodexCategory.Items => GearGroup.Items,
        CodexCategory.Essences => GearGroup.Quintessences,
        CodexCategory.Inscriptions => GearGroup.Inscriptions,
        CodexCategory.DarkAbilities => GearGroup.DarkAbilities,
        _ => null,
    };
}

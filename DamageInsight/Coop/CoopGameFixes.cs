using HarmonyLib;

namespace DamageInsight.Coop;

/// <summary>
/// The Grave Digger skull keeps the graves it raised in a list that is emptied when the next map has loaded
/// (<c>GraveDiggerGraveContainer.Clear</c> on <c>LevelManager.onMapLoaded</c>). The graves go with the old map before
/// that, so every frame in between reads a destroyed grave (NullReferenceException in its Update). Alone that is a
/// few frames; in co-op the loading barrier keeps the game running while it waits for the others, and a run had
/// 30-69 errors per game (COOP_LOG 57c, 57i). In co-op, destroyed graves leave the list before it is read.
/// </summary>
// REVIEW(fable): a guard against a symptom (trigger 6); the game's own clear-on-load stays. COOP_LOG 57i.
[HarmonyPatch(typeof(Characters.Gear.Weapons.GraveDiggerGraveContainer), "Update")]
internal static class CoopGraveDiggerGraves
{
    private static void Prefix(Characters.Gear.Weapons.GraveDiggerGraveContainer __instance)
    {
        if (CoopWorld.Current?.Active == true)
            __instance._graves?.RemoveAll(grave => grave == null);
    }
}


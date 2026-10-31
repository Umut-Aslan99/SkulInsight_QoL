using DamageInsight.SaveSlots;
using HarmonyLib;
using Level;

namespace DamageInsight.Patches;

/// <summary>Restart room (0.16): the game has just saved the room being entered (Chapter writes its save file right before).</summary>
[HarmonyPatch(typeof(LevelManager), nameof(LevelManager.InvokeOnMapChanged))]
public static class RoomSavedPatch
{
    private static void Postfix() => Guard.Run("Restart room (room saved)", RoomRestart.OnRoomSaved);
}

/// <summary>
/// Restart room passes the title screen and continues at once: the title's own start (logo, "press any key", which
/// would continue the run a second time on a key press) doesn't begin then.
/// </summary>
[HarmonyPatch(typeof(Scenes.Main), nameof(Scenes.Main.LogoAnimationsCompleted))]
public static class RoomRestartSkipsTitlePatch
{
    private static bool Prefix()
    {
        bool skip = false;
        Guard.Run("Restart room (title)", () => skip = RoomRestart.Continuing);
        return !skip;
    }
}

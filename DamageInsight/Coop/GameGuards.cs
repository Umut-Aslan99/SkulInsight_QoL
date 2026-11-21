using System;
using Characters.Operations.SetPosition;
using HarmonyLib;

namespace DamageInsight.Coop;

/// <summary>
/// Guards around game code that breaks in co-op situations the game never meets alone.
/// </summary>
internal static class GameGuards
{
    /// <summary>
    /// `ToPlayerBased.Clamp` (an enemy move that places something next to the player, e.g. a boss's teleport) asks for
    /// the ground under this game's player and uses it without a check: with no ground within 100 units (the player in
    /// the air over a pit, outside the arena, or a downed body) it stopped with a NullReferenceException, and the
    /// enemy's whole operation list stopped with it. The boss check of 2026-11-16 (COOP_LOG 60n) showed it in n4 and
    /// dm5 on both games. Now the spot stays the player's own (not clamped to a platform) and the move goes on.
    /// </summary>
    [HarmonyPatch(typeof(ToPlayerBased), nameof(ToPlayerBased.Clamp))]
    public static class PlayerBasedClamp
    {
        private static int _seen;

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception is not NullReferenceException)
                return __exception;
            if (_seen++ < 3)
                CoopTrace.Write("guard-clamp", CoopWorld.CurrentRoom(), what: "no ground under the player");
            return null;
        }
    }
}

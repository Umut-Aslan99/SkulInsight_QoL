using Characters;
using Utils;
using HarmonyLib;

namespace DamageInsight.Coop;

/// <summary>
/// The chapter 4 cannon ("CannonJump": a <c>Level.Traps.PlatformInvoker</c> that runs the cannon character's TakeOn
/// action) grabs whoever stands in its Range onto a <see cref="GrabBoard"/> and runs Jump, SetCharacterVisible(false
/// for 0.3 s) and SetPositionTo on everything grabbed. Two game details meet in co-op (Norman test 2026-11-21, COOP_LOG
/// 64, the run-ending "trampoline" lock):
/// <list type="bullet">
/// <item><see cref="GrabBoard.Add"/> checks <c>targets.Count &lt;= _maxTargetCount</c>, so the cannon's board (max 1)
/// takes two characters; its Range finds every Player-layer character, and a teammate's copy is one.</item>
/// <item>SetCharacterVisible with <c>_findComponentInOwner</c> keeps the owner's collider, renderer and attach object
/// in fields of the shared operation: run on two characters in the same frame, both restores after 0.3 s go to the
/// second one, and the first stays without collider (no interaction, can't step on the cannon again) and renderer
/// (invisible; a swap shows the other skull's renderer).</item>
/// </list>
/// Alone, only one player exists and neither shows. Here a trap's board never takes a teammate's copy: the copy's
/// own game runs its own cannon and the copy follows over the network. Enemies' grabs are left as they are.
/// </summary>
// REVIEW(fable): host/client authority of world things (trigger 2) + a Harmony patch on a shared game type
// (trigger 3); proven only by the game files (tools/unity/follow_refs.py), not yet by a game run. COOP_LOG 64.
[HarmonyPatch(typeof(GrabBoard), nameof(GrabBoard.Add))]
internal static class CoopTrapGrabs
{
    private static int _refused;

    private static bool Prefix(GrabBoard __instance, Target target)
    {
        if (target == null || CoopCopies.Count == 0 || !CoopCopies.IsCopy(target.character))
            return true;
        var holder = __instance.GetComponentInParent<Character>();
        if (holder != null && holder.type != Character.Type.Trap)
            return true; // an enemy's or a skull's grab: unchanged
        _refused++;
        if (_refused <= 20 || _refused % 100 == 0)
            CoopTrace.Write("grab-refused", CoopWorld.CurrentRoom(), name: CoopTrace.NameOf(target.character),
                what: holder != null ? CoopTrace.NameOf(holder) : __instance.name, amount: _refused);
        return false;
    }
}

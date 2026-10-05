#if DEV // a developer tool: release builds don't lead bosses
using System;
using System.Linq;
using UnityEngine;
using BD = BehaviorDesigner.Runtime;

namespace DamageInsight.Codex;

/// <summary>
/// One clean take per move: once a led (or swapped-in) move has started, the director waits until its take is saved
/// and its aftermath filmed before it asks for the next one. Without this, the forced moves came back to back and each
/// take was cut by the next move or filmed on into it (2026-10-05 run: Dark Skul 2's "Melee attack" showed the bone
/// rain before and the hard smash after it). A tree boss (BehaviorDesigner) is also held still meanwhile: its tree is
/// paused the moment the move ends (the move's own action plays out) and resumed when the take is done, at most
/// <see cref="HoldMax"/> later. Block and coroutine bosses keep their own pace meanwhile.
/// </summary>
public static partial class MoveDirector
{
    private const float HoldMax = 10f;      // a take still not saved by then: go on anyway
    private static bool _listening;

    /// <summary>Hooks the attack starts and ends once (the moment a move starts and ends counts, not the next tick).</summary>
    private static void Listen()
    {
        if (_listening)
            return;
        _listening = true;
        Patches.AttackTracker.Started += (run, _) => Patches.Guard.Run("Move director (start)", () => OnStarted(run));
        Patches.AttackTracker.Ended += (run, _) => Patches.Guard.Run("Move director (end)", () => OnEnded(run));
    }

    private static Led LedOf(AttackGraph graph) => Bosses.Values.FirstOrDefault(l => l.Graph == graph);

    /// <summary>The led move started: from now on its take is waited for.</summary>
    private static void OnStarted(Patches.AttackTracker.Run run)
    {
        if (!MovePicker.On || LedOf(run.Graph) is not { } led || led.Target == null || run.Graph.AttackOf(run.Unit)?.Label != led.Target)
            return;
        Wait(led, led.Target);
        led.Target = null;
    }

    /// <summary>The move waited for ended: a tree boss holds still until its take is saved.</summary>
    private static void OnEnded(Patches.AttackTracker.Run run)
    {
        if (!MovePicker.On || LedOf(run.Graph) is not { } led || led.Waiting == null || led.Held.Count > 0 ||
            run.Graph.AttackOf(run.Unit)?.Label != led.Waiting || led.Boss == null)
            return;
        foreach (var behavior in BossAttacks.TreesOf(led.Boss).Where(b => b != null && b.isActiveAndEnabled))
        {
            behavior.DisableBehavior(true);
            led.Held.Add(behavior);
        }
        led.HeldSince = Time.unscaledTime;
        if (led.Held.Count > 0)
            Plugin.Log.LogInfo($"Move director: {led.Key}: holding still until {led.Waiting} is on film.");
    }

    private static void Wait(Led led, string move)
    {
        led.Waiting = move;
        led.WaitUntil = Time.unscaledTime + HoldMax + 6f; // the move itself plays first
        led.Aside[move] = Time.unscaledTime + Started;
        led.Plan = null;
    }

    /// <summary>
    /// Refresh: whether the boss still waits for a take (no new move meanwhile). Done once the move is on film and no
    /// take of the boss is being filmed any more (its aftermath included), or the hold took too long.
    /// </summary>
    private static bool StillWaiting(Led led, FightRecorder.Directed film)
    {
        if (led.Waiting == null)
            return false;
        float now = Time.unscaledTime;
        // A held boss waits for its aftermath too; one that keeps acting would never let the takes close.
        bool saved = !film.Missing.Contains(led.Waiting) && (led.Held.Count == 0 || !film.Filming);
        bool tooLong = now > led.WaitUntil || led.Held.Count > 0 && now - led.HeldSince > HoldMax;
        if (!saved && !tooLong)
        {
            led.Plan = null;
            led.Text = $"Director: filming {led.Waiting}";
            return true;
        }
        Plugin.Log.LogInfo($"Move director: {led.Key}: {led.Waiting} " + (saved ? "on film" : "not on film after waiting") + ".");
        Release(led);
        led.Waiting = null;
        return false;
    }

    /// <summary>Resumes the trees held for a take.</summary>
    private static void Release(Led led)
    {
        foreach (var behavior in led.Held)
            try
            {
                if (behavior != null)
                    behavior.EnableBehavior();
            }
            catch (Exception e)
            {
                Patches.Guard.Report("Move director (resume)", e);
            }
        led.Held.Clear();
        led.HeldSince = 0f;
    }
}
#endif

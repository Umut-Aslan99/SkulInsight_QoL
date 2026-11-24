using System;
using Characters;
using Characters.Gear.Synergy;
using Characters.Gear.Synergy.Inscriptions;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DamageInsight.Coop;

/// <summary>
/// An inscription's effect object comes from its settings' AssetReference (`Inscription.LoadReference`). The game
/// asks that reference to load whenever its own handle is gone; `AssetReference.LoadAssetAsync` refuses ("Attempting
/// to load AssetReference that has already been loaded") while the reference's stored load is still alive, hands
/// back an empty handle, and the inscription's `Instantiate(null)` throws: that inscription has no effect for the rest
/// of the run (its later steps throw NullReferenceExceptions). Alone the stored load dies with the old player's
/// inscription; in co-op the teammate scans load the same address (TeammateProjectiles.LoadInscriptions,
/// TeammateFollowers' fairy tale) and keep it alive. Real players' logs 2026-11-24: 122 times in 12 of 20 co-op logs
/// (COOP_LOG 72b). Here an inscription without a handle takes the live load (one more holder, released by its own
/// UnloadReference) instead of asking again. Without a live load the game's own path runs as always.
/// </summary>
// REVIEW(fable): reference counting of a shared Addressables load (trigger 2); from the logs and the decompile.
[HarmonyPatch(typeof(Inscription), "LoadReference")]
internal static class InscriptionLiveLoad
{
    internal static int Reused { get; private set; }
    /// <summary>Off only inside the self-test (Testing/InscriptionLoadTest) to show the game's own failure.</summary>
    internal static bool Enabled = true;
    private static bool _failedLogged;

    private static bool Prefix(Inscription __instance)
    {
        try
        {
            if (!Enabled || __instance._handle.IsValid())
                return true; // it holds its load already: the game does nothing either
            var reference = __instance.settings?.reference;
            if (reference == null || !reference.IsValid())
                return true; // nobody holds this address: the game loads it as always
            var live = reference.OperationHandle;
            Addressables.ResourceManager.Acquire(live);
            __instance._handle = live.Convert<GameObject>();
            Reused++;
            if (Reused <= 20)
                Plugin.Log.LogInfo($"[Coop] inscription {__instance.key}: its effect reuses the load that is already alive ({Reused})");
            if (CoopTrace.On)
                CoopTrace.Write("inscription-reused", EnemySync.TraceRoom, name: __instance.key.ToString(), amount: Reused);
            return false;
        }
        catch (Exception e)
        {
            if (!_failedLogged)
            {
                _failedLogged = true;
                Plugin.Log.LogWarning("[Coop] inscription load guard failed (" + e.Message + "); the game's own load runs");
            }
            return true;
        }
    }
}

/// <summary>
/// A teammate copy is built from the player prefab and has its own Synergy (never active: a copy has no items). When
/// the copy goes (a teammate leaves, the copy is drawn again), the game's `Synergy.OnDestroy` runs `Destory()` on
/// every inscription, which writes `canBeSuper = false` for every key into THIS player's save
/// (`superInscriptions`). A copy's Synergy is left out of that.
/// </summary>
// REVIEW(fable): skips game code for copies (trigger 6); found while reading Synergy for COOP_LOG 73.
[HarmonyPatch(typeof(Synergy), "OnDestroy")]
internal static class CopySynergyKeepsSave
{
    private static bool Prefix(Synergy __instance)
    {
        try
        {
            var character = __instance.GetComponentInParent<Character>();
            if (character == null)
                return true;
            bool copy = CoopCopies.IsCopy(character) || character.name.StartsWith("SkulInsightCoop_", StringComparison.Ordinal);
            return !copy;
        }
        catch (Exception)
        {
            return true;
        }
    }
}

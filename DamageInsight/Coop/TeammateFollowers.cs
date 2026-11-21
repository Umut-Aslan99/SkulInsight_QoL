using System;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using Characters.Gear.Synergy.Inscriptions;
using Characters.Gear.Synergy.Inscriptions.FairyTaleSummon;
using HarmonyLib;
using Services;
using Singletons;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DamageInsight.Coop;

/// <summary>
/// A teammate's follower on this game (Umut 2026-11-21: "oberon doesnt show on the other players screen"). The Fairy
/// Tale inscription's full set spawns Oberon for its own player only (FairyTale.Spawn), and a copy carries no items or
/// inscriptions. Now the owner's game sends which Oberon it has (<see cref="TeammateEventKind.Follower"/>: the
/// inscription's reference field, or "" when gone; again with every loadout, so a newcomer gets it), and the copy's
/// game spawns the same Oberon for the copy. Its attacks act as the copy (CoopCopies: nothing here), its shots are the
/// owner's (TeammateProjectiles), its screen flash stays on the owner's screen (TeammateScreenFx).
/// </summary>
// REVIEW(fable): a new message kind; the copy's Oberon fights by itself (for the show) instead of in step.
internal static class TeammateFollowers
{
    private const string FairyTalePrefix = "FairyTale/";
    private static readonly string[] OberonFields =
        { "_oberonReference", "_darkOberonReference", "_superLightOberonReference", "_superDarkOberonReference" };

    /// <summary>This game's player's follower ("" none), sent with the loadout (CoopRuntime).</summary>
    internal static string Local = "";

    // ------------------------------------------------------------------ the owner's game

    [HarmonyPatch(typeof(FairyTale), "Spawn")]
    public static class Spawned
    {
        private static void Postfix(FairyTale __instance, AssetReference reference)
        {
            try
            {
                if (__instance.character == null || __instance.character != TeammateProjectiles.Local)
                    return;
                foreach (string field in OberonFields)
                    if (ReferenceEquals(AccessTools.Field(typeof(FairyTale), field)?.GetValue(__instance), reference))
                    {
                        Set(FairyTalePrefix + field);
                        return;
                    }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] Oberon not sent: " + e.Message);
            }
        }
    }

    [HarmonyPatch(typeof(FairyTale), "Release")]
    public static class Released
    {
        private static void Postfix(FairyTale __instance)
        {
            if (__instance.character != null && __instance.character == TeammateProjectiles.Local)
                Set("");
        }
    }

    private static void Set(string follower)
    {
        if (Local == follower)
            return;
        Local = follower;
        TeammateProjectiles.Send?.Invoke(TeammateEvent.ForFollower(follower));
        CoopTrace.Write("follower-sent", CoopWorld.CurrentRoom(), what: follower.Length > 0 ? follower : "none");
    }

    // ------------------------------------------------------------------ the copy's game

    private sealed class Held
    {
        public string Name = "";
        public GameObject Follower;
        public Transform Slot;
        public AsyncOperationHandle<GameObject> Handle;
        public bool Loading, Failed;
    }

    private static readonly Dictionary<Character, Held> Copies = new();
    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
    private static AsyncOperationHandle<GameObject> _fairyTale;
    private static bool _fairyTaleAsked;

    /// <summary>Every frame for a shown copy (CoopRuntime): the copy has the follower its owner has.</summary>
    internal static void Keep(Character copy, string name)
    {
        if (copy == null)
            return;
        name ??= "";
        Copies.TryGetValue(copy, out var held);
        if (held != null && held.Name == name && (name.Length == 0 || held.Follower != null || held.Loading || held.Failed))
        {
            if (held.Loading)
                Finish(copy, held);
            return;
        }
        if (held != null)
        {
            Drop(held);
            Copies.Remove(copy);
        }
        Prune();
        if (name.Length == 0)
            return;
        Copies[copy] = held = new Held { Name = name };
        Begin(held);
    }

    private static void Prune()
    {
        if (Copies.Count <= 4)
            return;
        var gone = new List<Character>();
        foreach (var pair in Copies)
            if (pair.Key == null)
            {
                Drop(pair.Value);
                gone.Add(pair.Key);
            }
        foreach (var key in gone)
            Copies.Remove(key);
    }

    private static void Drop(Held held)
    {
        if (held.Follower != null)
            UnityEngine.Object.Destroy(held.Follower);
        if (held.Slot != null)
            UnityEngine.Object.Destroy(held.Slot.gameObject);
        held.Follower = null;
        held.Slot = null;
    }

    /// <summary>The Fairy Tale inscription's prefab (its Oberon references), loaded once the way the game loads it.</summary>
    private static FairyTale FairyTalePrefab()
    {
        if (!_fairyTaleAsked)
        {
            _fairyTaleAsked = true;
            var settings = Singleton<Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.synergy?._synergySettings?.settings;
            var reference = settings?[Characters.Gear.Synergy.Inscriptions.Inscription.Key.FairyTale]?.reference;
            if (reference != null && reference.RuntimeKeyIsValid())
                _fairyTale = Addressables.LoadAssetAsync<GameObject>(reference.RuntimeKey);
            else
                _fairyTaleAsked = false; // no player yet: try again
        }
        return _fairyTale.IsValid() && _fairyTale.IsDone && _fairyTale.Status == AsyncOperationStatus.Succeeded && _fairyTale.Result != null
            ? _fairyTale.Result.GetComponent<FairyTale>()
            : null;
    }

    private static void Begin(Held held)
    {
        held.Loading = true;
    }

    /// <summary>Loads step by step (the inscription prefab, then the Oberon), then spawns it for the copy.</summary>
    private static void Finish(Character copy, Held held)
    {
        try
        {
            if (!held.Handle.IsValid())
            {
                var fairyTale = FairyTalePrefab();
                if (fairyTale == null)
                    return; // still loading
                if (!held.Name.StartsWith(FairyTalePrefix, StringComparison.Ordinal))
                {
                    Fail(held, "unknown follower " + held.Name);
                    return;
                }
                string field = held.Name.Substring(FairyTalePrefix.Length);
                var reference = AccessTools.Field(typeof(FairyTale), field)?.GetValue(fairyTale) as AssetReference;
                if (reference == null || !reference.RuntimeKeyIsValid())
                {
                    Fail(held, "no reference " + field);
                    return;
                }
                held.Handle = Addressables.LoadAssetAsync<GameObject>(reference.RuntimeKey);
                // where Oberon floats: the inscription's slot, relative to its player
                var slot = new GameObject("SkulInsight teammate Oberon slot").transform;
                slot.SetParent(copy.transform, false);
                var prefabSlot = fairyTale._oberonSlot;
                slot.localPosition = prefabSlot != null ? fairyTale.transform.InverseTransformPoint(prefabSlot.position) : new Vector3(-1f, 1.5f, 0f);
                held.Slot = slot;
                return;
            }
            if (!held.Handle.IsDone)
                return;
            held.Loading = false;
            if (held.Handle.Status != AsyncOperationStatus.Succeeded || held.Handle.Result == null)
            {
                Fail(held, "not loaded");
                return;
            }
            var follower = UnityEngine.Object.Instantiate(held.Handle.Result);
            var oberon = follower.GetComponent<Oberon>();
            if (oberon == null)
            {
                UnityEngine.Object.Destroy(follower);
                Fail(held, "no Oberon on the prefab");
                return;
            }
            oberon.Initialize(copy, held.Slot);
            held.Follower = follower;
            CoopTrace.Write("follower-shown", CoopWorld.CurrentRoom(), what: held.Name);
        }
        catch (Exception e)
        {
            Fail(held, e.Message);
        }
    }

    private static void Fail(Held held, string why)
    {
        held.Loading = false;
        held.Failed = true;
        if (Logged.Add(held.Name + why))
            Plugin.Log.LogWarning($"[Coop] teammate follower {held.Name} not shown: {why}");
        CoopTrace.Write("follower-missing", CoopWorld.CurrentRoom(), what: held.Name, name: why);
    }
}

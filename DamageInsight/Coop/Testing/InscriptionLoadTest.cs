using System.Collections;
using Characters;
using Characters.Gear.Synergy.Inscriptions;
using Services;
using Singletons;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// Self-test of <see cref="InscriptionLiveLoad"/> (`--skulinsight-repro=inscriptions` on a test copy, COOP_LOG 73):
/// in the castle it plays what a co-op session does to an inscription's AssetReference, once with the guard off and
/// once on: (1) the player's inscription loads it the game's way, (2) a teammate scan loads the same address by key
/// and keeps it (TeammateProjectiles.LoadInscriptions), (3) the old inscription releases its handle (the player object
/// of the last run goes), (4) a fresh inscription (the next run's player) loads again. Guard off must show the players'
/// error (an empty handle); guard on must give the effect prefab. "[InscriptionTest]" lines in LogOutput.log, then the
/// copy quits. Developer tool only: English.
/// </summary>
public sealed class InscriptionLoadTest : MonoBehaviour
{
    private float _since = -1f;
    private bool _started;

    private void Update()
    {
        var player = Singleton<Service>.Instance?.levelManager?.player;
        if (_started || player?.playerComponents?.inventory?.synergy == null)
            return;
        if (_since < 0f)
            _since = Time.unscaledTime;
        if (Time.unscaledTime - _since < 3f)
            return;
        _started = true;
        StartCoroutine(CRun(player));
    }

    private IEnumerator CRun(Character player)
    {
        var synergy = player.playerComponents.inventory.synergy;
        Inscription.Key key = Inscription.Key.None;
        foreach (var inscription in synergy.inscriptions)
            if (inscription != null && inscription.key != Inscription.Key.None && inscription.settings?.reference != null
                && inscription.settings.reference.RuntimeKeyIsValid() && !inscription.settings.reference.IsValid())
            {
                key = inscription.key;
                break;
            }
        if (key == Inscription.Key.None)
        {
            Plugin.Log.LogWarning("[InscriptionTest] no inscription with a free reference found");
            yield break;
        }
        int passed = 0;
        foreach (bool guard in new[] { false, true })
        {
            InscriptionLiveLoad.Enabled = guard;
            var settings = synergy.inscriptions[key].settings;
            var old = new Inscription();
            old.Initialize(key, settings, player);
            old.LoadReference();                       // (1) the game's way: AssetReference.LoadAssetAsync
            old._handle.WaitForCompletion();
            var ours = Addressables.LoadAssetAsync<GameObject>(settings.reference.RuntimeKey); // (2) a teammate scan
            ours.WaitForCompletion();
            old.UnloadReference();                     // (3) the old player's inscription goes
            yield return null;
            var fresh = new Inscription();
            fresh.Initialize(key, settings, player);
            fresh.LoadReference();                     // (4) the next run's player
            GameObject prefab = fresh._handle.IsValid() ? fresh._handle.WaitForCompletion() : null;
            bool ok = prefab != null;
            bool expected = ok == guard;               // off: the players' error; on: the prefab
            if (expected)
                passed++;
            Plugin.Log.LogInfo($"[InscriptionTest] {key}, guard {(guard ? "on" : "off")}: " +
                               (ok ? "effect prefab loaded" : "empty handle (the players' error)") +
                               (expected ? " - as expected" : " - NOT as expected"));
            if (fresh._handle.IsValid())
                fresh.UnloadReference();
            if (ours.IsValid())
                Addressables.Release(ours);
            yield return null;
            Plugin.Log.LogInfo($"[InscriptionTest] after cleanup the reference holds a load: {settings.reference.IsValid()}");
        }
        InscriptionLiveLoad.Enabled = true;
        Plugin.Log.LogInfo($"[InscriptionTest] RESULT {(passed == 2 ? "PASS" : "FAIL")} ({passed}/2)");
        yield return new WaitForSecondsRealtime(1f);
        Application.Quit();
    }
}

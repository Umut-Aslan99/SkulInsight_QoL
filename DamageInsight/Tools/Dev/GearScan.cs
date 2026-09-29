using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Characters;
using Characters.Gear.Synergy.Inscriptions;
using GameResources;
using Services;
using Singletons;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DamageInsight.Tools;

/// <summary>
/// Development tool: loads every skull, item and quintessence prefab (without spawning it), every
/// inscription's data object, and every character they summon, and saves their serialized data as JSON,
/// plus the global status settings. This gives us the real numbers to build the description texts from.
/// Output: BepInEx\DamageInsight\GearScan\{weapons,items,essences,inscriptions,characters}\&lt;name&gt;.json,
/// statuses.json, index.tsv
/// </summary>
public static class GearScan
{
    public static string OutputFolder => Path.Combine(Paths.BepInExRootPath, "DamageInsight", "GearScan");

    private static readonly HashSet<string> WrittenCharacters = new();
    private static readonly HashSet<string> SeenAssets = new();
    private static readonly Queue<(string guid, string referencedBy)> PendingAssets = new();
    private const int MaxLinkedAssets = 3000;
    private static StringBuilder _index;

    public static IEnumerator Run(Action<string> progress, Action<string> onDone)
    {
        var gear = GearResource.instance;
        if (gear == null)
        {
            onDone("GearResource not loaded");
            yield break;
        }

        Directory.CreateDirectory(OutputFolder);
        WrittenCharacters.Clear();
        SeenAssets.Clear();
        PendingAssets.Clear();
        _index = new StringBuilder("category\tname\tdisplayName\trarity\tobtainable\tbytes\ttruncated\terror\n");

        WriteStatuses();

        var sets = new List<(string folder, string prefix, IEnumerable<GearReference> refs)>
        {
            ("weapons", "weapon", gear.weapons),
            ("items", "item", gear.items),
            ("essences", "quintessence", gear.essences),
        };
        int total = sets.Sum(s => s.refs.Count()), done = 0, failed = 0;

        foreach (var (folder, prefix, refs) in sets)
        {
            foreach (var reference in refs)
            {
                done++;
                if (done % 25 == 0)
                    progress($"Gear scan {done}/{total}");

                GearRequest request = null;
                string error = "";
                try
                {
                    request = reference.LoadAsync();
                }
                catch (Exception e)
                {
                    error = e.GetBaseException().Message;
                }

                if (request != null)
                {
                    while (!request.isDone)
                        yield return null;
                    try
                    {
                        var go = request.handle.Result;
                        if (go == null)
                            throw new Exception("prefab did not load");
                        Save(folder, reference.name, go, Meta(reference, prefix, folder), reference.displayNameKey,
                            reference.rarity.ToString(), reference.obtainable.ToString());
                    }
                    catch (Exception e)
                    {
                        error = e.GetBaseException().Message;
                    }
                    finally
                    {
                        try { request.Release(); } catch { /* ignore */ }
                    }
                }

                if (error.Length > 0)
                {
                    failed++;
                    Plugin.Log.LogWarning($"Gear scan: {folder}/{reference.name} failed: {error}");
                    _index.Append($"{folder}\t{reference.name}\t\t\t\t0\tFalse\t{error}\n");
                }
                yield return null; // one prefab per frame keeps the game responsive
            }
        }

        progress("Gear scan: inscriptions");
        int inscriptions = 0;
        var synergy = Singleton<Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.synergy;
        if (synergy != null)
        {
            for (int i = 0; i < synergy.inscriptions.Count; i++)
            {
                var inscription = synergy.inscriptions.Array[i];
                var key = synergy.inscriptions.Keys[i];
                AssetReference reference = inscription?.settings?.reference;
                if (reference == null || !reference.RuntimeKeyIsValid())
                    continue;

                // Load through the runtime key (not the AssetReference itself): the game may already hold
                // this reference's own handle when the inscription is active.
                AsyncOperationHandle<GameObject> handle = default;
                bool started = false;
                try
                {
                    handle = Addressables.LoadAssetAsync<GameObject>(reference.RuntimeKey);
                    started = true;
                }
                catch (Exception e)
                {
                    failed++;
                    Plugin.Log.LogWarning($"Gear scan: inscription {key} not loadable: {e.GetBaseException().Message}");
                }
                if (!started)
                    continue;
                while (handle.IsValid() && !handle.IsDone)
                    yield return null;
                try
                {
                    if (handle.IsValid() && handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                    {
                        Save("inscriptions", key.ToString(), handle.Result, InscriptionMeta(key, inscription), "", "", "");
                        inscriptions++;
                    }
                }
                catch (Exception e)
                {
                    failed++;
                    Plugin.Log.LogWarning($"Gear scan: inscription {key} failed: {e.GetBaseException().Message}");
                }
                finally
                {
                    try { if (handle.IsValid()) Addressables.Release(handle); } catch { /* ignore */ }
                }
                yield return null;
            }
        }
        else
        {
            Plugin.Log.LogWarning("Gear scan: no player inventory, inscriptions skipped");
        }

        // Prefabs the gear only links to (Addressables), e.g. Fairy Tale's Oberons or projectiles.
        int linked = 0;
        while (PendingAssets.Count > 0 && linked < MaxLinkedAssets)
        {
            var (guid, referencedBy) = PendingAssets.Dequeue();
            if (linked % 25 == 0)
                progress($"Gear scan: linked prefabs {linked} (+{PendingAssets.Count} queued)");

            AsyncOperationHandle<UnityEngine.Object> handle;
            try
            {
                handle = Addressables.LoadAssetAsync<UnityEngine.Object>(guid);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Gear scan: link {guid} ({referencedBy}) not loadable: {e.GetBaseException().Message}");
                continue;
            }
            while (!handle.IsDone)
                yield return null;
            try
            {
                if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result is GameObject go)
                {
                    var meta = new Dictionary<string, string>
                    {
                        ["category"] = "linked",
                        ["name"] = go.name,
                        ["guid"] = guid,
                        ["referencedBy"] = referencedBy,
                    };
                    Save("linked", guid, go, meta, "", "", "");
                    linked++;
                }
            }
            catch (Exception e)
            {
                failed++;
                Plugin.Log.LogWarning($"Gear scan: link {guid} failed: {e.GetBaseException().Message}");
            }
            finally
            {
                try { if (handle.IsValid()) Addressables.Release(handle); } catch { /* ignore */ }
            }
            yield return null;
        }

        File.WriteAllText(Path.Combine(OutputFolder, "index.tsv"), _index.ToString(), Encoding.UTF8);
        onDone($"{done - failed}/{total} gear, {inscriptions} inscriptions, {WrittenCharacters.Count} characters, {linked} linked prefabs saved ({failed} failed) to {OutputFolder}");
    }

    /// <summary>Writes one object as JSON, then every character it references (and theirs, recursively).</summary>
    private static void Save(string folder, string name, GameObject go, Dictionary<string, string> meta,
        string displayNameKey, string rarity, string obtainable)
    {
        var writer = new ObjectGraphWriter();
        writer.WriteRoot(go, meta);
        string dir = Path.Combine(OutputFolder, folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, Safe(name) + ".json"), writer.Result, Encoding.UTF8);
        string displayName = meta.TryGetValue("displayName", out var d) ? d : Loc(displayNameKey);
        _index.Append($"{folder}\t{name}\t{displayName}\t{rarity}\t{obtainable}\t{writer.Result.Length}\t{writer.Truncated}\t\n");

        foreach (string guid in writer.ReferencedAssets)
            if (SeenAssets.Add(guid))
                PendingAssets.Enqueue((guid, $"{folder}/{name}"));

        foreach (var character in writer.ReferencedCharacters)
        {
            if (character == null)
                continue;
            string characterName = character.gameObject.name.Replace("(Clone)", "").Trim();
            if (!WrittenCharacters.Add(characterName))
                continue;
            var characterMeta = new Dictionary<string, string>
            {
                ["category"] = "characters",
                ["name"] = characterName,
                ["key"] = character.key.ToString(),
                ["type"] = character.type.ToString(),
                ["displayName"] = Loc($"enemy/name/{character.key}"),
                ["referencedBy"] = $"{folder}/{name}",
            };
            Save("characters", characterName, character.gameObject, characterMeta, "", "", "");
        }
    }

    private static Dictionary<string, string> Meta(GearReference r, string prefix, string category)
    {
        string key = $"{prefix}/{r.name}";
        return new Dictionary<string, string>
        {
            ["category"] = category,
            ["name"] = r.name,
            ["rarity"] = r.rarity.ToString(),
            ["gearTag"] = r.gearTag.ToString(),
            ["obtainable"] = r.obtainable.ToString(),
            ["displayName"] = Loc(key + "/name"),
            ["description"] = Loc(key + "/desc"),
            ["activeName"] = Loc(key + "/active/name"),
            ["activeDescription"] = Loc(key + "/active/desc"),
        };
    }

    private static Dictionary<string, string> InscriptionMeta(Inscription.Key key, Inscription inscription)
    {
        var steps = inscription.settings.steps ?? Array.Empty<int>();
        var meta = new Dictionary<string, string>
        {
            ["category"] = "inscriptions",
            ["name"] = key.ToString(),
            ["displayName"] = Loc($"synergy/key/{key}/name"),
            ["steps"] = string.Join(",", steps),
            ["omenItem"] = inscription.settings.omenItem?.AssetGUID ?? "",
            ["superDescription"] = Loc($"synergy/key/{key}/desc/tuning"),
        };
        for (int i = 0; i < steps.Length; i++)
            meta[$"desc{i}"] = Loc($"synergy/key/{key}/desc/{i}");
        return meta;
    }

    private static void WriteStatuses()
    {
        try
        {
            var writer = new ObjectGraphWriter();
            writer.WriteValue(CharacterStatusSetting.instance);
            File.WriteAllText(Path.Combine(OutputFolder, "statuses.json"), writer.Result, Encoding.UTF8);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Gear scan: status settings failed: {e.GetBaseException().Message}");
        }
    }

    private static string Loc(string key)
    {
        if (string.IsNullOrEmpty(key))
            return "";
        try
        {
            return Localization.TryGetLocalizedString(key, out var s) ? s : "";
        }
        catch
        {
            return "";
        }
    }

    private static string Safe(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }
}

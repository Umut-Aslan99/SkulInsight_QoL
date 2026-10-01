using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Characters;
using DamageInsight.Codex;
using GameResources;
using Level;
using Level.Waves;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace DamageInsight.Tools;

/// <summary>
/// Development tool for balancing the Codex progression: reads every chapter's stages (how many normal maps, which
/// map pool, special map chances, fixed maps) and every map prefab's enemy waves (without spawning anything), per
/// enemy group: normal, and Dark Mirror A (level 0-3), B (4-7), C (8-10). Output: Codex\Debug\levels.json.
/// </summary>
public static class LevelScan
{
    public static string OutputFile => Path.Combine(CodexTracker.Folder, "Debug", "levels.json");

    private static readonly string[] Groups = { "normal", "A", "B", "C" };

    public static IEnumerator Run(Action<string> progress, Action<string> done)
    {
        var stages = new StringBuilder();
        var maps = new Dictionary<string, string>(); // guid -> json
        var toScan = new Dictionary<string, MapReference>();
        int chapterCount = 0;
        foreach (Chapter.Type type in Enum.GetValues(typeof(Chapter.Type)))
        {
            if (type is Chapter.Type.Test or Chapter.Type.Castle or Chapter.Type.Tutorial or Chapter.Type.HardmodeCastle)
                continue;
            AssetReference chapterRef = null;
            try { chapterRef = LevelResource.instance.GetChapter((int)type); }
            catch (Exception) { /* no such chapter */ }
            if (chapterRef == null || !chapterRef.RuntimeKeyIsValid())
                continue;
            var chapterHandle = Addressables.LoadAssetAsync<Chapter>(chapterRef.RuntimeKey);
            while (!chapterHandle.IsDone)
                yield return null;
            var chapter = chapterHandle.Status == AsyncOperationStatus.Succeeded ? chapterHandle.Result : null;
            if (chapter == null)
                continue;
            chapterCount++;
            for (int s = 0; s < (chapter.stages?.Length ?? 0); s++)
            {
                var stageHandle = Addressables.LoadAssetAsync<IStageInfo>(chapter.stages[s].RuntimeKey);
                while (!stageHandle.IsDone)
                    yield return null;
                var stage = stageHandle.Status == AsyncOperationStatus.Succeeded ? stageHandle.Result : null;
                if (stage != null)
                {
                    if (stages.Length > 0)
                        stages.Append(",\n");
                    stages.Append(StageJson(type, s, stage, toScan));
                }
                Addressables.Release(stageHandle);
            }
            Addressables.Release(chapterHandle);
            progress($"Level scan: {type} read");
        }

        int n = 0;
        foreach (var pair in toScan)
        {
            n++;
            if (n % 10 == 0)
                progress($"Level scan: map {n}/{toScan.Count}");
            var handle = Addressables.LoadAssetAsync<GameObject>(pair.Value.reference.RuntimeKey);
            while (!handle.IsDone)
                yield return null;
            try
            {
                maps[pair.Key] = handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null
                    ? MapJson(handle.Result)
                    : "{\"error\":\"not loaded\"}";
            }
            catch (Exception e)
            {
                maps[pair.Key] = "{\"error\":" + Str(e.Message) + "}";
            }
            Addressables.Release(handle);
            yield return null;
        }

        var sb = new StringBuilder("{\"stages\":[\n").Append(stages).Append("\n],\"maps\":{\n");
        sb.Append(string.Join(",\n", maps.Select(p => Str(p.Key) + ":" + p.Value)));
        sb.Append("\n}}");
        Directory.CreateDirectory(Path.GetDirectoryName(OutputFile));
        File.WriteAllText(OutputFile, sb.ToString(), new UTF8Encoding(false));
        done($"{chapterCount} chapters, {toScan.Count} maps -> {OutputFile}");
    }

    private static string StageJson(Chapter.Type chapter, int index, IStageInfo stage, Dictionary<string, MapReference> toScan)
    {
        string Ref(MapReference m)
        {
            if (m == null || m.reference == null || string.IsNullOrWhiteSpace(m.reference.AssetGUID))
                return "null";
            toScan[m.reference.AssetGUID] = m;
            return "{\"guid\":" + Str(m.reference.AssetGUID) + ",\"type\":" + Str(m.type.ToString()) +
                   ",\"special\":" + Str(m.specialMapType.ToString()) + ",\"dark\":" + (m.darkEnemy ? "true" : "false") +
                   ",\"path\":" + Str(m.path) + "}";
        }
        string Range(Vector2Int v) => $"[{v.x},{v.y}]";
        var sb = new StringBuilder();
        sb.Append("{\"chapter\":").Append(Str(chapter.ToString())).Append(",\"stage\":").Append(index)
          .Append(",\"kind\":").Append(Str(stage.GetType().Name))
          .Append(",\"pool\":[").Append(string.Join(",", (stage.maps ?? Array.Empty<MapReference>()).Select(Ref))).Append(']');
        switch (stage)
        {
            case StageInfo info:
                sb.Append(",\"normalMaps\":").Append(Range(info._normalMaps))
                  .Append(",\"headRewards\":").Append(Range(info._headRewards))
                  .Append(",\"itemRewards\":").Append(Range(info._itemRewards))
                  .Append(",\"specialWeights\":[").Append(string.Join(",", (info._specialMapWeights ?? Array.Empty<float>())
                      .Select(w => w.ToString(System.Globalization.CultureInfo.InvariantCulture)))).Append(']')
                  .Append(",\"entry\":").Append(Ref(info._entry?.reference))
                  .Append(",\"terminal\":").Append(Ref(info._terminal?.reference))
                  .Append(",\"extra\":[").Append(string.Join(",", (info._extraMaps?.values ?? Array.Empty<StageInfo.ExtraMapInfo>())
                      .Where(e => e != null)
                      .Select(e => "{\"map\":" + Ref(e.reference) + ",\"possibility\":" +
                                   e.possibility.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                                   ",\"position\":" + Range(e.positionRange) + "}"))).Append(']')
                  .Append(",\"castleNpc\":").Append(Ref(info._castleNpc?.reference));
                break;
            case CustomStageInfo custom:
                sb.Append(",\"fixed\":[").Append(string.Join(",", (custom._maps?.values ?? Array.Empty<SerializablePathNode>())
                    .Where(m => m != null).Select(m => Ref(m.reference)))).Append(']');
                break;
        }
        return sb.Append('}').ToString();
    }

    /// <summary>A map's enemies per group: every wave's characters and pins (Pin → Enemy prefab → characters).</summary>
    private static string MapJson(GameObject prefab)
    {
        var counts = Groups.ToDictionary(g => g, _ => new Dictionary<string, int>());
        void Count(string group, Character c)
        {
            if (c == null || !(CodexTracker.EntryOf(c) is { } entry))
                return;
            var d = counts[group];
            d[entry.id] = d.TryGetValue(entry.id, out int n) ? n + 1 : 1;
        }
        void CountPin(string group, Pin pin)
        {
            var enemy = pin._enemy;
            if (enemy == null)
                return;
            foreach (var c in enemy.characters ?? Array.Empty<Character>())
                Count(group, c);
        }
        var waves = prefab.GetComponentsInChildren<EnemyWave>(true);
        foreach (var wave in waves)
            foreach (Transform child in wave.transform)
            {
                if (child.GetComponent<PinGroupSelector>() is { } selector)
                {
                    var byGroup = new[] { selector._groupNormal, selector._groupA, selector._groupB, selector._groupC };
                    for (int g = 0; g < Groups.Length; g++)
                        if (byGroup[g] != null)
                            foreach (var pin in byGroup[g].GetComponentsInChildren<Pin>(true))
                                CountPin(Groups[g], pin);
                }
                else if (child.GetComponent<Character>() is { } character)
                {
                    foreach (var g in Groups)
                        Count(g, character);
                }
                else if (child.GetComponent<Pin>() is { } pin)
                {
                    foreach (var g in Groups)
                        CountPin(g, pin);
                }
            }
        string Counts(Dictionary<string, int> d) => "{" + string.Join(",", d.OrderBy(p => p.Key).Select(p => Str(p.Key) + ":" + p.Value)) + "}";
        var map = prefab.GetComponent<Map>();
        return "{\"name\":" + Str(prefab.name) + ",\"mapType\":" + Str(map != null ? map._type.ToString() : "?") +
               ",\"waves\":" + waves.Length + "," + string.Join(",", Groups.Select(g => Str(g) + ":" + Counts(counts[g]))) + "}";
    }

    private static string Str(string s) =>
        s == null ? "null" : "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
}

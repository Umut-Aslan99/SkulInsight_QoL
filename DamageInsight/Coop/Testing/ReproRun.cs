using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Characters;
using Characters.Controllers;
using GameResources;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// Solo hand test of a reported scenario (`--skulinsight-repro=NAME`, play-issue4-repro.cmd; COOP_LOG 71). A test
/// copy (own save, no co-op session) leaves the castle by itself into the scenario's chapter at its Dark Mirror level
/// and seed, gets the reported build in the chapter's first room (skulls with their skills, quintessence, items) and
/// goes straight to the scenario's room; then Umut plays by hand. The player is healed to full below 30 % health so the
/// run doesn't end (it still takes hits and knockback). In every room it watches the player and writes "[Repro]" lines
/// to LogOutput.log: what changed (hidden parts, outside the map, controls held, the running skill), and once the
/// player is LOST (hidden, outside the map or controls held 3 s) the last moves before it, a picture in
/// &lt;data&gt;/Repro and a notice. Developer tool only: English, never in a normal game.
/// </summary>
public sealed class ReproRun : MonoBehaviour
{
    private sealed class Scenario
    {
        public string Name;
        public Chapter.Type Chapter;
        public int Stage, DmLevel, Seed;
        public (string skull, string[] skills)[] Skulls;
        public string Essence;
        public string[] Items;
    }

    // GitHub issue #4 (uiig6, 2026-10-29): daily 2026-10-29|dm10+dlc, the DM Chimera (chapter 3, stage 2, map '0');
    // the player vanished there with GraveDigger_4 (King Lear + Hamlet) and WereWolf_2 (Hunting) out.
    private static readonly Dictionary<string, Scenario> Scenarios = new()
    {
        ["issue4"] = new Scenario
        {
            Name = "issue #4: DM10 Chimera, Grave Digger 4 + Werewolf 2",
            Chapter = Level.Chapter.Type.HardmodeChapter3, Stage = 2, DmLevel = 10, Seed = -264543652,
            Skulls = new[] { ("GraveDigger_4", new[] { "KingLear_4", "Hamlet_4" }), ("WereWolf_2", new[] { "Hunting_2" }) },
            Essence = "Banshee",
            Items = new[] { "InvisibleKnife", "CrownOfThorns", "HatredStone", "OliveTreeCudgel", "GoldenIncenseBurner",
                            "AssassinationDagger", "TheRing", "VoodooDoll", "GoldenWhip" },
        },
    };

    private const float BlockedSeconds = 3f;
    private Scenario _scenario;
    private int _step; // 0 castle, 1 loading the chapter, 2 giving the build, 3 to the room, 4 playing
    private float _castleSince = -1f, _blockedSince = -1f;
    private string _lastState = "", _lastMotion = "";
    private bool _lost;
    private Map _room;
    private readonly Queue<string> _moves = new();

    /// <summary>Called once from Plugin.Awake: only with --skulinsight-repro on a test copy without co-op.</summary>
    internal static void Begin(GameObject host)
    {
        var launch = CoopLaunch.Current;
        if (launch.Repro == null)
            return;
        if (!launch.IsTestCopy)
        {
            Plugin.Log.LogWarning("[Repro] only on a test copy (--skulinsight-instance 2-4): its own save, never Umut's");
            return;
        }
        if (launch.Repro == "conflict")
        {
            host.AddComponent<ConflictNoticeTest>(); // the other-co-op-mod warning, pictured (COOP_LOG 73)
            Plugin.Log.LogInfo("[Repro] scenario: co-op conflict notice picture");
            return;
        }
        if (launch.Repro == "inscriptions")
        {
            host.AddComponent<InscriptionLoadTest>(); // self-test of InscriptionLiveLoad (COOP_LOG 73)
            Plugin.Log.LogInfo("[Repro] scenario: inscription load self-test");
            return;
        }
        if (!Scenarios.TryGetValue(launch.Repro, out var scenario))
        {
            Plugin.Log.LogWarning($"[Repro] no scenario named {launch.Repro} (known: {string.Join(", ", Scenarios.Keys)})");
            return;
        }
        host.AddComponent<ReproRun>()._scenario = scenario;
        Plugin.Log.LogInfo("[Repro] scenario: " + scenario.Name);
    }

    private void Update()
    {
        var levels = Singleton<Service>.Instance?.levelManager;
        var player = levels?.player;
        var chapter = levels?.currentChapter;
        if (player == null || chapter == null || Map.Instance == null)
            return;
        float now = Time.unscaledTime;
        if (_step == 0 && chapter.type == Level.Chapter.Type.Castle)
        {
            if (_castleSince < 0f)
                _castleSince = now;
            else if (now - _castleSince > 3f)
            {
                _step = 1;
                StartCoroutine(CLeaveCastle(levels));
            }
            return;
        }
        if (_step == 1 && chapter.type == _scenario.Chapter)
        {
            _step = 2;
            StartCoroutine(CGiveBuild(levels, player, chapter));
            return;
        }
        if (_step < 4 || chapter.type == Level.Chapter.Type.Castle)
            return;
        Watch(player, now);
    }

    private IEnumerator CLeaveCastle(LevelManager levels)
    {
        yield return Singleton<Service>.Instance.fadeInOut.CFadeOut();
        Data.GameData.HardmodeProgress.hardmodeLevel = _scenario.DmLevel;
        Data.GameData.Save.instance._randomSeed.value = _scenario.Seed;
        Plugin.Log.LogInfo($"[Repro] leaving the castle: {_scenario.Chapter}, Dark Mirror {_scenario.DmLevel}, seed {_scenario.Seed}");
        levels.Load(_scenario.Chapter);
    }

    private IEnumerator CGiveBuild(LevelManager levels, Character player, Chapter chapter)
    {
        yield return new WaitForSeconds(2f);
        var resource = GearResource.instance;
        var inventory = player.playerComponents.inventory;
        for (int slot = 0; slot < _scenario.Skulls.Length && slot < inventory.weapon.weapons.Length; slot++)
        {
            var (name, skills) = _scenario.Skulls[slot];
            var reference = resource.weapons.FirstOrDefault(r => r != null && r.name == name);
            if (reference == null)
            {
                Plugin.Log.LogWarning("[Repro] no skull named " + name);
                continue;
            }
            var request = reference.LoadAsync();
            while (!request.isDone)
                yield return null;
            var weapon = levels.DropWeapon(request, player.transform.position);
            inventory.weapon.ForceEquipAt(weapon, slot);
            weapon.SetSkills(skills.ToArray(), ignoreLevel: false);
            Plugin.Log.LogInfo($"[Repro] skull {slot}: {name} ({string.Join(", ", weapon.currentSkills.Select(s => s.key))})");
        }
        var essence = resource.essences.FirstOrDefault(r => r != null && r.name == _scenario.Essence);
        if (essence == null)
            Plugin.Log.LogWarning("[Repro] no quintessence named " + _scenario.Essence);
        else
        {
            var request = essence.LoadAsync();
            while (!request.isDone)
                yield return null;
            inventory.quintessence.EquipAt(levels.DropQuintessence(request, player.transform.position), 0);
        }
        foreach (string name in _scenario.Items)
        {
            var reference = resource.items.FirstOrDefault(r => r != null && string.Equals(r.name, name, System.StringComparison.OrdinalIgnoreCase));
            if (reference == null)
            {
                Plugin.Log.LogWarning("[Repro] no item named " + name);
                continue;
            }
            var request = reference.LoadAsync();
            while (!request.isDone)
                yield return null;
            var item = levels.DropItem(request, player.transform.position);
            item?.dropped?.InteractWith(player);
            yield return null;
        }
        Plugin.Log.LogInfo("[Repro] items held: " + string.Join(", ", inventory.item.items.Where(i => i != null).Select(i => i.name)));
        yield return new WaitForSeconds(1f);
        _step = 3;
        Plugin.Log.LogInfo($"[Repro] to stage {_scenario.Stage} (the scenario's room)");
        chapter.Enter(_scenario.Stage, 0, 0);
        _step = 4;
    }

    // ------------------------------------------------------------------ watching the player

    private void Watch(Character player, float now)
    {
        if (_room != Map.Instance)
        {
            _room = Map.Instance;
            _lost = false;
            _lastState = "";
            _blockedSince = -1f;
            Plugin.Log.LogInfo($"[Repro] room: {Singleton<Service>.Instance.levelManager.currentChapter.type}, map '{_room.name.Replace("(Clone)", "")}'");
        }
        var health = player.health;
        if (health != null && !health.dead && health.currentHealth < health.maximumHealth * 0.3)
        {
            health.ResetToMaximumHealth();
            Plugin.Log.LogInfo("[Repro] healed to full (below 30 %)");
        }

        var running = player.runningMotion;
        string motion = running == null ? "" : (running.transform.parent != null ? running.transform.parent.name + "/" : "") + running.name;
        if (motion != _lastMotion)
        {
            _lastMotion = motion;
            Remember($"{Clock()} motion {(motion.Length > 0 ? motion : "none")} at {Spot(player)}");
        }

        string hidden = DamageInsight.Coop.PlayerWatch.HiddenText(player);
        bool inMap = _room.IsInMap(player.transform.position);
        bool blocked = PlayerInput.blocked.value && !global::UI.Dialogue.anyDialogueOpened;
        string holders = blocked ? DamageInsight.Coop.PlayerWatch.HolderNames() : "";
        // a cutscene (the boss's intro: LetterBox, the chapter's own hold) is logged but is no lost player
        bool cutscene = holders.Contains("(UI.LetterBox)") || holders.Contains("(Level.Chapter)");
        if (blocked && !cutscene && _blockedSince < 0f)
            _blockedSince = now;
        else if (!blocked || cutscene)
            _blockedSince = -1f;
        string state = (hidden.Length > 0 ? "hidden: " + hidden + "; " : "") + (inMap ? "" : "OUTSIDE THE MAP; ")
                       + (blocked ? "controls held by " + holders + "; " : "") + (player.invulnerable.value ? "invulnerable; " : "");
        if (state != _lastState)
        {
            _lastState = state;
            Remember($"{Clock()} state {(state.Length > 0 ? state : "normal")} at {Spot(player)}");
            Plugin.Log.LogInfo($"[Repro] state: {(state.Length > 0 ? state : "normal")} at {Spot(player)}, motion {motion}");
        }

        bool lost = hidden.Length > 0 || !inMap || (_blockedSince >= 0f && now - _blockedSince > BlockedSeconds);
        if (lost && !_lost)
        {
            _lost = true;
            string picture = Picture();
            Plugin.Log.LogWarning($"[Repro] LOST: {state} at {Spot(player)}, map bounds {_room.bounds.min:F1}..{_room.bounds.max:F1}; " +
                                  $"picture {picture}; the moves before it:\n  " + string.Join("\n  ", _moves));
            UI.Notice.Show("repro", "Repro: the player is LOST (" + state.TrimEnd(' ', ';') + "). Written to LogOutput.log.", 8f);
        }
        else if (!lost && _lost)
        {
            _lost = false;
            Plugin.Log.LogInfo($"[Repro] back to normal at {Spot(player)}");
        }
    }

    private void Remember(string line)
    {
        _moves.Enqueue(line);
        while (_moves.Count > 25)
            _moves.Dequeue();
    }

    private static string Clock() => System.DateTime.Now.ToString("HH:mm:ss.f");

    private static string Spot(Character player)
    {
        var p = player.transform.position;
        return $"({p.x:F1}, {p.y:F1})";
    }

    private static string Picture()
    {
        try
        {
            string folder = Path.Combine(ModData.Root, "Repro");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "lost-" + System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".png");
            ScreenCapture.CaptureScreenshot(path);
            return path;
        }
        catch (System.Exception e)
        {
            return "none (" + e.Message + ")";
        }
    }
}

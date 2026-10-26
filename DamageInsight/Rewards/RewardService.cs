using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Characters;
using Characters.Gear;
using Characters.Gear.Synergy.Inscriptions;
using Characters.Gear.Weapons;
using Data;
using DamageInsight.Codex;
using DamageInsight.Lang;
using GameResources;
using Level;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Rewards;

/// <summary>
/// The bestiary rewards in the game (0.15, docs/V015_SPEC.md part B): coins for broken legendary gear and for
/// adventurers, Omen coins at a run's end, the endless kill quests, inscription activations, and using them
/// (Summon: the gear drops at your feet; Activate: +1 to an inscription you have, like the Dark Mirror's banners).
/// The numbers and switches are in the settings section Rewards; all of it is off in daily runs.
/// </summary>
public static class RewardService
{
    private static bool CoinsOn => RewardApplier.On && Plugin.RewardsCoins?.Value == true;
    private static bool QuestsOn => RewardApplier.On && Plugin.RewardsQuests?.Value == true;
    private static bool ActivationsOn => RewardApplier.On && Plugin.RewardsActivations?.Value == true;

    private static int SummonLimit => Plugin.RewardsSummonsPerRun?.Value ?? 1;
    /// <summary>Coins a summon of this kind takes (setting Coins per summon; an adventurer's coin alone).</summary>
    public static int CoinCost(CoinKind kind) => RewardRules.CoinCost(kind, Plugin.RewardsCoinsPerSummon?.Value ?? 3);
    private static int ActivationLimit => Plugin.RewardsActivationsPerRun?.Value ?? 2;

    private static bool Developer =>
#if DEV
        true;
#else
        false;
#endif

    private static float _nextTick;
    private static Characters.Gear.Synergy.Synergy _synergy;
    private static string _synergyRun = "";
    private static readonly Dictionary<Inscription.Key, int> Added = new();

    /// <summary>One more of an inscription on this inventory, remembered so it can be taken back.</summary>
    private static void Raise(Characters.Gear.Synergy.Synergy synergy, Inscription.Key key)
    {
        synergy.inscriptions[key].bonusCount += 1;
        if (ReferenceEquals(synergy, _synergy))
            Added[key] = (Added.TryGetValue(key, out int n) ? n : 0) + 1;
    }

    private static Character Player => Singleton<Service>.Instance?.levelManager?.player;

    /// <summary>The run in progress ("" in the castle): the save's random seed, which a continued run keeps.</summary>
    public static string RunNow
    {
        get
        {
            var chapter = Singleton<Service>.Instance?.levelManager?.currentChapter;
            if (chapter == null || Player == null || !IsRunChapter(chapter.type))
                return "";
            return "seed:" + GameData.Save.instance.randomSeed;
        }
    }

    private static bool IsRunChapter(Chapter.Type type) =>
        type is >= Chapter.Type.Chapter1 and <= Chapter.Type.Chapter5 or >= Chapter.Type.HardmodeChapter1 and <= Chapter.Type.HardmodeChapter6;

    public static void Tick(Character player)
    {
        if (player == null || Time.unscaledTime < _nextTick || !RewardApplier.On)
            return;
        _nextTick = Time.unscaledTime + 0.5f;
        string run = RunNow;
        if (run.Length == 0)
            return;
        RewardStore.BeginRun(run);
        var synergy = player.playerComponents?.inventory?.synergy;
        if (synergy == null)
            return;
        // The game never resets bonusCount (the Dark Mirror's banners add theirs to a fresh inventory at each start):
        // what we added is kept per inventory. A new inventory (a continued run) gets this run's activations again;
        // the same inventory in a new run gives the last run's back.
        if (!ReferenceEquals(synergy, _synergy) || _synergyRun != run)
        {
            bool changed = false;
            if (ReferenceEquals(synergy, _synergy))
                foreach (var pair in Added)
                {
                    synergy.inscriptions[pair.Key].bonusCount = Math.Max(0, synergy.inscriptions[pair.Key].bonusCount - pair.Value);
                    changed = true;
                }
            _synergy = synergy;
            _synergyRun = run;
            Added.Clear();
            foreach (string key in RewardStore.ActivatedThisRun)
                if (Enum.TryParse(key, out Inscription.Key k))
                {
                    Raise(synergy, k);
                    changed = true;
                }
            if (changed)
            {
                player.playerComponents.inventory.UpdateSynergy();
                Plugin.Log.LogInfo($"Rewards: inscription activations of this run on this inventory: {string.Join(", ", RewardStore.ActivatedThisRun)}");
            }
        }
        if (!ActivationsOn)
            return;
        // An inscription at its top step earns an activation, once per run (not one raised by an activation).
        for (int i = 0; i < synergy.inscriptions.Count; i++)
        {
            var inscription = synergy.inscriptions.Array[i];
            if (inscription == null || !inscription.isMaxStep || inscription.count <= 0)
                continue;
            string key = synergy.inscriptions.Keys[i].ToString();
            if (RewardStore.ActivatedThisRun.Contains(key))
                continue;
            if (RewardStore.NoteMaxed(key))
                Float(Loc.F("{0} completed: an inscription activation when this run ends!", InscriptionName(synergy.inscriptions.Keys[i])), "#C9A2FF");
        }
    }

    // ------------------------------------------------------------------ earning

    /// <summary>
    /// A legendary item or skull was destroyed while lying on the ground (dismantled, or left behind when the map was
    /// left; Umut 2026-10-25: both count): its coin. Shop wares never count; a skull gives its first tier's coin.
    /// </summary>
    public static void OnGearDestroyed(Gear gear)
    {
        if (!CoinsOn || gear == null || gear.state != Gear.State.Dropped || gear.rarity != Rarity.Legendary || !gear.destructible)
            return;
        if (gear.type is not (Gear.Type.Item or Gear.Type.Weapon))
            return;
        var player = Player;
        if (player == null || !player.liveAndActive || RunNow.Length == 0)
            return;
        if (gear.dropped != null && gear.dropped.price > 0)
            return;
        string name = gear.name.Replace("(Clone)", "").Trim();
        if (RewardStore.SummonedThisRun(CodexCatalog.GearId(gear.type, name)))
            return;
        string id;
        if (gear is Weapon)
        {
            string first = CodexCatalog.Entries.FirstOrDefault(e => e.Id == CodexCatalog.GearId(Gear.Type.Weapon, name)) is { } entry
                ? CodexPages.AwakeningPath(entry).FirstOrDefault()?.Key : null;
            id = CodexCatalog.GearId(Gear.Type.Weapon, RewardRules.BaseSkull(name, first));
        }
        else
            id = CodexCatalog.GearId(Gear.Type.Item, name);
        RewardStore.AddCoin(id);
        History.RunJournal.AddNumber(History.RunNumbers.CoinsEarned, 1);
        Plugin.Log.LogInfo($"Rewards: a coin for {id} (legendary {gear.type} {name} destroyed)");
        Float(Loc.F("Coin earned: {0}", GearName(id)), "#FFD86B");
    }

    /// <summary>
    /// A gear's discard event (Gear.onDiscard, fired by the game's own OnDestroy; subscribed in Gear.Awake by
    /// Patches/RewardPatches): the coin rules of <see cref="OnGearDestroyed"/>.
    /// </summary>
    public static void OnGearDiscarded(Gear gear)
    {
        if (Service.quitting)
            return;
        Patches.Guard.Run("Rewards: gear discarded", () => OnGearDestroyed(gear));
    }

    /// <summary>An enemy the player killed (from the Codex's kill count): its quest, an adventurer's coin.</summary>
    public static void OnKilled(Character target, string entryId, CodexCategory category)
    {
        if (!RewardApplier.On || target == null || RunNow.Length == 0)
            return;
        if (target.type == Character.Type.Adventurer)
        {
            string adventurer = RewardRules.AdventurerOf(entryId.Substring(entryId.IndexOf(':') + 1));
            if (adventurer.Length == 0)
                adventurer = RewardRules.AdventurerOf(CodexCatalog.CleanName(target.name));
            if (CoinsOn && adventurer.Length > 0 && RewardStore.CountAdventurerKill(adventurer, Plugin.RewardsAdventurerKills.Value))
            {
                string item = CodexCatalog.GearId(Gear.Type.Item, RewardRules.ChosenItem(adventurer));
                RewardStore.AddCoin(item);
                History.RunJournal.AddNumber(History.RunNumbers.CoinsEarned, 1);
                Float(Loc.F("Coin earned: {0}", GearName(item)), "#FFD86B");
            }
            return;
        }
        if (!QuestsOn || category != CodexCategory.Enemies || target.type is Character.Type.Boss or Character.Type.Named)
            return;
        int done = RewardStore.CountKill(entryId, QuestStepOf(entryId), IsDark(target) ? RewardRules.DarkEnemyKills : 1);
        if (done <= 0)
            return;
        int quartz = Plugin.RewardsQuestDarkQuartz.Value * done, gold = Plugin.RewardsQuestGold.Value * done;
        var levelManager = Singleton<Service>.Instance.levelManager;
        Vector3 at = target.transform.position + Vector3.up;
        if (gold > 0)
            levelManager.DropGold(gold, Math.Min(10, Math.Max(1, gold / 10)), at);
        if (quartz > 0)
            levelManager.DropDarkQuartz(quartz, Math.Min(10, Math.Max(1, quartz / 5)), at);
        RewardStore.AddPaid(quartz, gold);
        History.RunJournal.AddNumber(History.RunNumbers.QuestsDone, done);
        Float(Loc.F("Quest done: {0}", CodexCatalog.Entries.FirstOrDefault(e => e.Id == entryId)?.Name ?? entryId), "#9BE8FF");
    }

    private static bool IsDark(Character target)
    {
        var attacher = target.attach != null ? target.attach.GetComponentInChildren<Characters.Abilities.Darks.DarkAbilityAttacher>(true) : null;
        return attacher?._abilities != null && attacher._abilities.Any(a => a != null);
    }

    /// <summary>
    /// A run ended (death or ending): the activations for the inscriptions that reached their top step in it, and an
    /// Omen coin for each Omen item held. Before the run history closes the run.
    /// </summary>
    public static void OnRunEnded()
    {
        if (ActivationsOn && RunNow.Length > 0 && RewardStore.PayMaxed() is var paid && paid > 0)
            Plugin.Log.LogInfo($"Rewards: {paid} inscription activation(s) for this run ({string.Join(", ", RewardStore.MaxedThisRun)})");
        if (!CoinsOn || Player is not { } player)
            return;
        var items = player.playerComponents?.inventory?.item?.items;
        int omens = items?.Count(i => i != null && (i.gearTag & (Gear.Tag.Omen | Gear.Tag.UpgradedOmen)) != 0 &&
                                      !RewardStore.SummonedThisRun(CodexCatalog.GearId(Gear.Type.Item, i.name.Replace("(Clone)", "").Trim()))) ?? 0;
        if (omens <= 0)
            return;
        RewardStore.AddOmenCoins(omens);
        History.RunJournal.AddNumber(History.RunNumbers.CoinsEarned, omens);
        Plugin.Log.LogInfo($"Rewards: {omens} Omen coin(s) for the Omen items held at the run's end");
    }

    // ------------------------------------------------------------------ using

    /// <summary>Why a coin of this kind can't be used now ("" when it can). Each kind has its own summons per run.</summary>
    public static string WhyNoSummon(CoinKind kind)
    {
        if (!CoinsOn)
            return Loc.T("Coins are off (settings, Rewards).");
        if (RunNow.Length == 0)
            return Loc.T("Coins work during a run.");
        // Umut 2026-10-26: the adventurers' The Chosen items stay Dark Mirror items.
        if (kind == CoinKind.Adventurer && !CodexMode.DarkMirrorNow)
            return Loc.T("The Chosen items only come in the Dark Mirror: use this coin in a Dark Mirror run.");
        if (RewardStore.SummonsThisRun(kind) >= SummonLimit)
            return Loc.P("{0} summon per run: used.", "{0} summons per run: used.", SummonLimit);
        if (Map.Instance?.waveContainer != null && Map.Instance.waveContainer.state != EnemyWaveContainer.State.Empty)
            return Loc.T("Clear the map first.");
        return "";
    }

    /// <summary>Uses a coin: the gear drops at the player's feet. Returns what happened, for the book.</summary>
    public static string Summon(string gearId)
    {
        string why = WhyNoSummon(RewardRules.KindOf(gearId));
        if (why.Length > 0)
            return why;
        var reference = ReferenceOf(gearId);
        if (reference == null)
            return Loc.T("This gear isn't in the game's data.");
        int cost = CoinCost(RewardRules.KindOf(gearId));
        if (!RewardStore.UseCoin(gearId, cost))
            return RewardPages.NeedCoins(cost);
        Plugin.Instance.StartCoroutine(Drop(reference));
        return Loc.F("{0} summoned at your feet.", GearName(gearId));
    }

    /// <summary>The Omen items an Omen coin can summon: the ones this slot has met (all in developer builds).</summary>
    public static List<string> OmenChoices() =>
        GearResource.instance.items.Where(r => (r.gearTag & Gear.Tag.Omen) != 0)
            .Select(r => CodexCatalog.GearId(Gear.Type.Item, r.name))
            .Where(id => Developer || CodexTracker.Progress.Peek(id) is { } p && (p.Seen > 0 || p.PickedUp > 0))
            .OrderBy(GearName, StringComparer.CurrentCulture).ToList();

    public static string SummonOmen(string gearId)
    {
        string why = WhyNoSummon(CoinKind.Omen);
        if (why.Length > 0)
            return why;
        var reference = ReferenceOf(gearId);
        if (reference == null || (reference.gearTag & Gear.Tag.Omen) == 0)
            return Loc.T("This gear isn't in the game's data.");
        int cost = CoinCost(CoinKind.Omen);
        if (!RewardStore.UseOmenCoin(gearId, cost))
            return RewardPages.NeedCoins(cost);
        Plugin.Instance.StartCoroutine(Drop(reference));
        return Loc.F("{0} summoned at your feet.", GearName(gearId));
    }

    /// <summary>Why an activation can't be used now ("" when it can).</summary>
    public static string WhyNoActivation()
    {
        if (!ActivationsOn)
            return Loc.T("Inscription activations are off (settings, Rewards).");
        if (RunNow.Length == 0)
            return Loc.T("Activations work during a run.");
        if (RewardStore.ActivatedThisRun.Count >= ActivationLimit)
            return Loc.P("{0} activation per run: used.", "{0} activations per run: used.", ActivationLimit);
        if (RewardStore.Activations <= 0)
            return Loc.T("No activation left.");
        return "";
    }

    /// <summary>The inscriptions an activation can raise now: ones you have, not the excluded kinds (like the banners).</summary>
    public static List<Inscription.Key> ActivationChoices()
    {
        var list = new List<Inscription.Key>();
        var synergy = Player?.playerComponents?.inventory?.synergy;
        if (synergy == null)
            return list;
        for (int i = 0; i < synergy.inscriptions.Count; i++)
        {
            var key = synergy.inscriptions.Keys[i];
            var inscription = synergy.inscriptions.Array[i];
            // Once per inscription and run (Umut 2026-10-26: the same one could be raised twice).
            if (inscription != null && inscription.count >= 1 && !RewardStore.ActivatedThisRun.Contains(key.ToString()) &&
                key is not (Inscription.Key.SunAndMoon or Inscription.Key.Masterpiece or Inscription.Key.Sin or Inscription.Key.Omen))
                list.Add(key);
        }
        return list;
    }

    public static string Activate(Inscription.Key key)
    {
        string why = WhyNoActivation();
        if (why.Length > 0)
            return why;
        if (RewardStore.ActivatedThisRun.Contains(key.ToString()))
            return Loc.T("Already raised in this run.");
        if (!ActivationChoices().Contains(key))
            return Loc.T("You need an item with this inscription.");
        var inventory = Player.playerComponents.inventory;
        if (!RewardStore.UseActivation(key.ToString()))
            return Loc.T("No activation left.");
        if (!ReferenceEquals(inventory.synergy, _synergy))
        {
            // Not seen by Tick yet: this inventory and run start here.
            _synergy = inventory.synergy;
            _synergyRun = RunNow;
            Added.Clear();
        }
        Raise(inventory.synergy, key);
        inventory.UpdateSynergy();
        Plugin.Log.LogInfo($"Rewards: inscription activation on {key}");
        return Loc.F("{0} +1 for this run.", InscriptionName(key));
    }

    /// <summary>A link on the Rewards page (RewardPages.SummonLink + gear, ...): uses it, returns what happened.</summary>
    public static string Use(string link)
    {
        if (link.StartsWith(RewardPages.SummonLink, StringComparison.Ordinal))
            return Summon(link.Substring(RewardPages.SummonLink.Length));
        if (link.StartsWith(RewardPages.OmenLink, StringComparison.Ordinal))
            return SummonOmen(link.Substring(RewardPages.OmenLink.Length));
        if (link.StartsWith(RewardPages.ActivateLink, StringComparison.Ordinal) &&
            Enum.TryParse(link.Substring(RewardPages.ActivateLink.Length), out Inscription.Key key))
            return Activate(key);
        return "";
    }

    /// <summary>
    /// Quest steps a full run finishes on average with the current settings (the Codex's normal enemies and their kills
    /// per run; always the real steps, also in developer builds).
    /// </summary>
    public static double QuestsPerRun() =>
        RewardRules.QuestsPerRun(CodexCatalog.Entries.Where(e => e.Category == CodexCategory.Enemies)
            .Select(e => CodexBalance.KillsPerRun.TryGetValue(e.Id, out float r) ? (double)r : 0.0), Plugin.RewardsQuestRuns?.Value ?? 2f);

    /// <summary>An enemy's quest step in kills, with the current settings.</summary>
    public static int QuestStepOf(string entryId)
    {
        float perRun = entryId != null && CodexBalance.KillsPerRun.TryGetValue(entryId, out float r) ? r : CodexBalance.DefaultKillsPerRun.Value;
        return RewardRules.QuestStep(perRun, Plugin.RewardsQuestRuns?.Value ?? 2f, Developer);
    }

#if DEV
    /// <summary>Developer builds, F11: a coin of each kind (the held skull's first form, Hope Slasher, The Chosen Warrior's Armor, an Omen coin) and an activation.</summary>
    public static void DevGrant()
    {
        // A summon's worth of each (Coins per summon).
        var current = Player?.playerComponents?.inventory?.weapon?.current;
        for (int i = 0; i < CoinCost(CoinKind.Skull); i++)
        {
            if (current != null)
                RewardStore.AddCoin(CodexCatalog.GearId(Gear.Type.Weapon, RewardRules.BaseSkull(current.name.Replace("(Clone)", "").Trim())));
            RewardStore.AddCoin(CodexCatalog.GearId(Gear.Type.Item, "HopeCutter")); // a legendary item coin
        }
        RewardStore.AddCoin(CodexCatalog.GearId(Gear.Type.Item, RewardRules.ChosenItem("Warrior")));
        RewardStore.AddOmenCoins(CoinCost(CoinKind.Omen));
        RewardStore.AddActivations(1);
        Float("DEV: a summon's coins (held skull, Hope Slasher, The Chosen Warrior's Armor, Omen), 1 activation", "#FFD86B");
        Plugin.Log.LogInfo("Rewards (DEV): test coins, an Omen coin and an activation granted");
    }
#endif

    // ------------------------------------------------------------------ helpers

    private static GearReference ReferenceOf(string gearId)
    {
        if (string.IsNullOrEmpty(gearId) || GearResource.instance == null)
            return null;
        int colon = gearId.IndexOf(':');
        string kind = gearId.Substring(0, Math.Max(0, colon)), name = gearId.Substring(colon + 1);
        return kind == "skull"
            ? GearResource.instance.weapons.FirstOrDefault(r => r.name == name)
            : GearResource.instance.items.FirstOrDefault(r => r.name == name);
    }

    private static IEnumerator Drop(GearReference reference)
    {
        var request = reference.LoadAsync();
        while (!request.isDone)
            yield return null;
        Patches.Guard.Run("Rewards: summon", () =>
        {
            var levelManager = Singleton<Service>.Instance.levelManager;
            if (levelManager?.player == null)
                return;
            levelManager.DropGear(request, levelManager.player.transform.position + Vector3.up);
            Plugin.Log.LogInfo($"Rewards: {reference.name} summoned");
        });
    }

    public static string GearName(string gearId) =>
        CodexCatalog.Entries.FirstOrDefault(e => e.Id == gearId)?.Name ?? gearId.Substring(gearId.IndexOf(':') + 1);

    private static string InscriptionName(Inscription.Key key) =>
        CodexCatalog.Entries.FirstOrDefault(e => e.Id == CodexCatalog.InscriptionId(key))?.Name ?? key.ToString();

    private static void Float(string text, string color)
    {
        try
        {
            var player = Player;
            if (player != null)
                Singleton<Service>.Instance?.floatingTextSpawner?.SpawnBuff(text, player.transform.position + Vector3.up * 2.4f, color);
        }
        catch (Exception)
        {
            // only a notice
        }
    }
}

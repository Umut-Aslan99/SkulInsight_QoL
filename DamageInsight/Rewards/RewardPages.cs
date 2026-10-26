using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Achievements;
using DamageInsight.Codex;
using DamageInsight.History;
using DamageInsight.Lang;

namespace DamageInsight.Rewards;

/// <summary>What the Rewards tab needs from the game (filled by the book; tests fill it by hand).</summary>
public sealed class RewardContext
{
    /// <summary>Rewards are on for this slot; "" or why not (off in the settings, the daily slot).</summary>
    public string Off = "";
    public HashSet<string> Earned = new();
    public RewardTotals Totals = new();
    public Func<string, string> Name = id => id;
    /// <summary>An achievement is still veiled (a story spoiler): its name and reward stay hidden.</summary>
    public Func<AchievementInfo, bool> Veiled = _ => false;
    /// <summary>Why a summon / an activation can't be used now ("" when it can).</summary>
    public Func<CoinKind, string> WhyNoSummon = _ => "";
    public string WhyNoActivation = "";
    public int SummonLimit = 1, ActivationLimit = 2, AdventurerKills = 5, CoinsPerSummon = 3;
    public List<string> OmenChoices = new();
    public List<(string key, string name)> ActivationChoices = new();
    /// <summary>The normal enemies (Codex id and name), for the quests page.</summary>
    public List<(string id, string name)> Enemies = new();
    /// <summary>An enemy's quest step in kills.</summary>
    public Func<string, int> QuestStep = _ => 10;
    public int QuestDarkQuartz = 25, QuestGold = 50;
    /// <summary>Quest steps a full run finishes on average (RewardRules.QuestsPerRun), 0 when unknown.</summary>
    public double QuestsPerRun;
    /// <summary>What the last click did (shown at the top of the page), "" for nothing.</summary>
    public string Message = "";
}

/// <summary>
/// The Rewards tab's texts (0.15; book: You → Rewards): a row per kind of reward for the left page and its page on the
/// right, with links that use a coin or an activation ("rewards:summon:&lt;gear&gt;", ...). Plain logic, no Unity.
/// </summary>
public static class RewardPages
{
    public enum Page { Achievements, Coins, Quests, Activations }

    public const string IdPrefix = "rewards:";
    public const string SummonLink = "rewards:summon:", OmenLink = "rewards:omen:", ActivateLink = "rewards:activate:";

    private const string Dim = RunPages.DimColor;

    public static string IdOf(Page page) => IdPrefix + page.ToString().ToLowerInvariant();

    public static string Default => IdOf(Page.Achievements);

    public static Page PageOf(string id)
    {
        foreach (Page page in Enum.GetValues(typeof(Page)))
            if (IdOf(page) == id)
                return page;
        return Page.Achievements;
    }

    /// <summary>A click on a page link that uses something (a coin, an activation), not a page to open.</summary>
    public static bool IsAction(string link) =>
        link != null && (link.StartsWith(SummonLink, StringComparison.Ordinal) || link.StartsWith(OmenLink, StringComparison.Ordinal)
                         || link.StartsWith(ActivateLink, StringComparison.Ordinal));

    public static string Title(Page page) => page switch
    {
        Page.Coins => Loc.T("Coins"),
        Page.Quests => Loc.T("Kill quests"),
        Page.Activations => Loc.T("Inscription activations"),
        _ => Loc.T("Achievement rewards"),
    };

    public static string Icon(Page page) => page switch
    {
        Page.Coins => "pix:chest",
        Page.Quests => "pix:flag",
        Page.Activations => "pix:star",
        _ => "pix:trophy",
    };

    /// <summary>A left page row: a line or two, the headline number and what it is.</summary>
    public static (string detail, string headline, string caption) Row(Page page, RewardContext c)
    {
        switch (page)
        {
            case Page.Coins:
                int coins = RewardStore.AllCoins.Sum(p => p.Value) + RewardStore.OmenCoins;
                int left = RewardRules.CoinKinds.Sum(k => Math.Max(0, c.SummonLimit - RewardStore.SummonsThisRun(k)));
                return (Loc.F("Summons left in this run: {0}", left), RunPages.N(coins), Loc.T("Coins held"));
            case Page.Quests:
                var (done, quartz, gold) = RewardStore.QuestTotals;
                return (Loc.F("Paid: {0} dark quartz, {1} gold", RunPages.N(quartz), RunPages.N(gold)), RunPages.N(done), Loc.T("Quests done"));
            case Page.Activations:
                return (Loc.F("Used this run: {0} of {1}", RewardStore.ActivatedThisRun.Count, c.ActivationLimit), RunPages.N(RewardStore.Activations),
                    Loc.T("Activations held"));
            default:
                return (Highlights(c.Totals), $"{c.Totals.Count} / {AchievementCatalog.All.Count}", Loc.T("Rewards earned"));
        }
    }

    /// <summary>The two biggest bonuses, for the row ("Physical attack +3 % · Gold gained +2 %").</summary>
    private static string Highlights(RewardTotals t) =>
        string.Join("  ·  ", t.Stats.Where(p => p.Value != 0).OrderByDescending(p => Math.Abs(p.Value)).Take(2)
            .Select(p => RewardCatalog.StatText(p.Key, p.Value)));

    public static string Subtitle(Page page, RewardContext c) =>
        c.Off.Length > 0 ? c.Off : page switch
        {
            Page.Achievements => Loc.T("Every achievement of this slot gives a small lasting bonus."),
            Page.Coins => Loc.T("A coin summons its gear in a later run."),
            Page.Quests => Loc.T("Every normal enemy has an endless quest."),
            _ => Loc.T("Complete an inscription in a run, and in a later run one you have counts one more."),
        };

    public static string Text(Page page, RewardContext c)
    {
        var sb = new StringBuilder();
        if (c.Message.Length > 0)
            sb.Append(c.Message).Append('\n');
        if (c.Off.Length > 0)
            sb.Append($"<color={Dim}>{c.Off}</color>\n");
        sb.Append(page switch
        {
            Page.Coins => CoinsText(c),
            Page.Quests => QuestsText(c),
            Page.Activations => ActivationsText(c),
            _ => AchievementsText(c),
        });
        return sb.ToString();
    }

    // ------------------------------------------------------------------ on other Codex pages

    /// <summary>
    /// An enemy page's quest square (Umut 2026-10-26): it fills up blue with the kills, the reward in its middle
    /// (the game's own currency icons when found), the quest next to it; an adventurer's page: the way to its coin.
    /// "" when rewards (or that part) are off.
    /// </summary>
    public static string QuestMeter(string entryId, CodexCategory category, string key, bool currencyIcons)
    {
        if (!RewardApplier.On)
            return "";
        string adventurer = RewardRules.AdventurerOf(key);
        if (adventurer.Length > 0)
        {
            if (Plugin.RewardsCoins?.Value != true)
                return "";
            int need = Math.Max(1, Plugin.RewardsAdventurerKills.Value), have = RewardStore.AdventurerProgress(adventurer);
            string item = CodexCatalog.GearId(Characters.Gear.Gear.Type.Item, RewardRules.ChosenItem(adventurer));
            string itemName = CodexPages.Entry(item)?.Name ?? item;
            return PageBlocks.Meter("+1", $"{Loc.T("Adventurer coin")}  {have} / {need}\n<color={Dim}>{Loc.F("Every {0} wins: a coin for {1}.", need, CodexPages.LinkTo(item, itemName))}</color>",
                (double)have / need);
        }
        if (category != CodexCategory.Enemies || Plugin.RewardsQuests?.Value != true)
            return "";
        var quest = RewardStore.Quest(entryId);
        int step = Math.Max(1, RewardService.QuestStepOf(entryId));
        int quartz = Plugin.RewardsQuestDarkQuartz.Value, gold = Plugin.RewardsQuestGold.Value;
        string done = quest.Done > 0 ? "  ·  " + Loc.P("done {0} time", "done {0} times", quest.Done) : "";
        return PageBlocks.Meter(RewardCenter(quartz, gold, currencyIcons),
            $"{Loc.T("Kill quest")}  {quest.Kills} / {step}{done}\n<color={Dim}>{Loc.F("Every {0} kills: {1} dark quartz and {2} gold.", step, quartz, gold)}</color>",
            (double)quest.Kills / step);
    }

    /// <summary>The reward in a quest square: dark quartz and gold, with the game's icons when they are there.</summary>
    public static string RewardCenter(int quartz, int gold, bool icons) =>
        icons
            ? $"<color=#8A4FD0><sprite name=\"Others/DarkQuartz_Icon\"> +{quartz}</color>\n<color=#9A7410><sprite name=\"Others/Gold_Icon\"> +{gold}</color>"
            : $"<color=#8A4FD0>+{quartz}</color>\n<color=#9A7410>+{gold}</color>";

    /// <summary>
    /// The Coins section of a skull's or item's page: the coins held with Summon, or (legendary gear) how to earn one.
    /// "" when coins are off, or for gear without coins that gives none.
    /// </summary>
    public static string GearCoinText(string gearId, bool legendary, bool omen, Func<CoinKind, string> whyNoSummonOf)
    {
        if (!RewardApplier.On || Plugin.RewardsCoins?.Value != true)
            return "";
        if (omen)
        {
            // An Omen item's page: summon this one with an Omen coin (Umut 2026-10-26: "a button in the omen items pages").
            var omenText = new StringBuilder(PageBlocks.Section(Loc.T("Omen coins"), "pix:star"));
            int omenCost = RewardRules.CoinCost(CoinKind.Omen, Plugin.RewardsCoinsPerSummon?.Value ?? 3);
            string whyOmen = Why(whyNoSummonOf(CoinKind.Omen), RewardStore.OmenCoins, omenCost);
            if (RewardStore.OmenCoins > 0)
            {
                omenText.Append($"{Loc.T("Omen coins")}  {Count(RewardStore.OmenCoins, omenCost)}<pos=70%>{Use(OmenLink + gearId, Loc.T("Summon"), whyOmen)}\n");
                if (whyOmen.Length > 0)
                    omenText.Append($"<color={Dim}>{whyOmen}</color>\n");
            }
            omenText.Append($"<color={Dim}>{Loc.T("End a run holding Omen items: an Omen coin for each, for an Omen item of your choice.")}</color>\n");
            return omenText.ToString();
        }
        var kind = RewardRules.KindOf(gearId);
        int coins = RewardStore.Coins(gearId), cost = RewardRules.CoinCost(kind, Plugin.RewardsCoinsPerSummon?.Value ?? 3);
        string whyNoSummon = Why(whyNoSummonOf(kind), coins, cost);
        bool chosen = gearId.StartsWith("item:TheChosen", StringComparison.Ordinal);
        if (coins == 0 && !legendary && !chosen)
            return "";
        var sb = new StringBuilder(PageBlocks.Section(Loc.T("Coins"), "pix:chest"));
        if (coins > 0)
        {
            sb.Append(Loc.F("Coins held: {0}", Count(coins, cost))).Append("<pos=70%>").Append(Use(SummonLink + gearId, Loc.T("Summon"), whyNoSummon)).Append('\n');
            if (whyNoSummon.Length > 0)
                sb.Append($"<color={Dim}>{whyNoSummon}</color>\n");
        }
        sb.Append($"<color={Dim}>{(chosen ? Loc.T("Its adventurer's coin summons it: win over that adventurer a few times (Codex, You, Rewards, Coins).") : gearId.StartsWith("skull:", StringComparison.Ordinal) ? Loc.T("Break it or leave it behind on a map: a coin that summons it in a later run (a skull raised with bones gives its first form's coin).") : Loc.T("Break it or leave it behind on a map: a coin that summons it in a later run."))}</color>\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ achievement rewards

    private static string AchievementsText(RewardContext c)
    {
        var sb = new StringBuilder();
        var t = c.Totals;
        sb.Append(PageBlocks.Section(Loc.T("In every run"), "pix:heart"));
        var stats = t.Stats.Where(p => p.Value != 0).OrderBy(p => p.Key).ToList();
        if (stats.Count == 0)
            sb.Append($"<color={Dim}>{Loc.T("Nothing yet: earn achievements in this slot.")}</color>\n");
        foreach (var (stat, percent) in stats.Select(p => (p.Key, p.Value)))
            sb.Append(RewardCatalog.StatText(stat, percent)).Append('\n');
        var targets = t.Targets.Where(p => p.Value != 1.0).OrderBy(p => p.Key, StringComparer.Ordinal).ToList();
        if (targets.Count > 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Against"), "pix:star"));
            foreach (var (target, multiplier) in targets.Select(p => (p.Key, p.Value)))
                sb.Append(RewardCatalog.TargetText(target, multiplier, id => CodexPages.LinkTo(id, c.Name(id)))).Append('\n');
            sb.Append($"<color={Dim}>{Loc.T("Rewards against the same boss multiply: beaten (+3 %) and without getting hit (+10 %) make +13.3 %.")}</color>\n");
        }

        // Every achievement by group: earned ones in ink, the rest dim, veiled spoilers hidden.
        foreach (string group in RewardCatalog.Groups)
        {
            var rewards = AchievementCatalog.All.Select(a => (info: a, reward: RewardCatalog.For(a.Id))).Where(x => x.reward?.Group == group).ToList();
            if (rewards.Count == 0)
                continue;
            int have = rewards.Count(x => c.Earned.Contains(x.info.Id));
            sb.Append(PageBlocks.Section($"{RewardCatalog.GroupName(group)}  <size=80%><color={Dim}>{have} / {rewards.Count}</color></size>", "pix:trophy"));
            foreach (var (info, reward) in rewards)
            {
                bool earned = c.Earned.Contains(info.Id);
                string name = c.Veiled(info) && !earned ? Loc.T("Hidden achievement") : CodexPages.LinkTo(AchievementPages.IdOf(info), info.Name);
                string what = c.Veiled(info) && !earned ? "???" : RewardCatalog.Text(reward, c.Name);
                sb.Append(earned ? $"{name}<pos=52%>{what}\n" : $"<color={Dim}>{name}<pos=52%>{what}</color>\n");
            }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ coins

    private static string Use(string link, string text, string why) =>
        why.Length > 0 ? $"<color={Dim}>{text}</color>" : CodexPages.LinkTo(link, text);

    /// <summary>"A summon takes 3 coins." (also the answer when a summon is clicked without enough).</summary>
    public static string NeedCoins(int cost) => Loc.P("A summon takes {0} coin.", "A summon takes {0} coins.", cost);

    /// <summary>Why a summon can't be made: the kind's reason, or too few coins for it.</summary>
    private static string Why(string kindWhy, int coins, int cost) => kindWhy.Length > 0 ? kindWhy : coins < cost ? NeedCoins(cost) : "";

    /// <summary>Coins held: "x2" when one is a summon, else "2 / 3" toward the next.</summary>
    private static string Count(int coins, int cost) => cost <= 1 ? "x" + coins : $"{coins} / {cost}";

    public static string KindName(CoinKind kind) => kind switch
    {
        CoinKind.Skull => Loc.T("Legendary skull coins"),
        CoinKind.Item => Loc.T("Legendary item coins"),
        CoinKind.Adventurer => Loc.T("Adventurer coins"),
        _ => Loc.T("Omen coins"),
    };

    private static string KindIcon(CoinKind kind) => kind switch
    {
        CoinKind.Skull => "pix:bones",
        CoinKind.Item => "pix:chest",
        CoinKind.Adventurer => "pix:crown",
        _ => "pix:star",
    };

    /// <summary>Under a kind's coins: why they can't be used now, or how many summons of that kind are left.</summary>
    private static void KindStatus(StringBuilder sb, RewardContext c, CoinKind kind)
    {
        string why = c.WhyNoSummon(kind);
        sb.Append(why.Length > 0
            ? $"<color={Dim}>{why}</color>\n"
            : $"<color={Dim}>{Loc.F("Summons left in this run: {0}", Math.Max(0, c.SummonLimit - RewardStore.SummonsThisRun(kind)))}</color>\n");
    }

    private static string CoinsText(RewardContext c)
    {
        var sb = new StringBuilder();
        var coins = RewardStore.AllCoins;
        if (coins.Count == 0 && RewardStore.OmenCoins == 0)
        {
            sb.Append(PageBlocks.Section(Loc.T("Your coins"), "pix:chest"));
            sb.Append($"<color={Dim}>{Loc.T("No coins yet.")}</color>\n");
        }
        sb.Append($"<color={Dim}>{Loc.P("{0} summon per run for each kind of coin.", "{0} summons per run for each kind of coin.", c.SummonLimit)}</color>\n");
        sb.Append($"<color={Dim}>{Loc.P("A summon takes {0} coin: of that gear, or Omen coins for an Omen item. An adventurer's coin is enough on its own.", "A summon takes {0} coins: of that gear, or Omen coins for an Omen item. An adventurer's coin is enough on its own.", Math.Max(1, c.CoinsPerSummon))}</color>\n");
        // Each kind on its own (Umut 2026-10-26): a skull, an item, an adventurer's item and an Omen item in one run.
        foreach (var kind in new[] { CoinKind.Skull, CoinKind.Item })
        {
            var mine = coins.Where(p => RewardRules.KindOf(p.Key) == kind).ToList();
            if (mine.Count == 0)
                continue;
            int cost = RewardRules.CoinCost(kind, c.CoinsPerSummon);
            sb.Append(PageBlocks.Section(KindName(kind), KindIcon(kind)));
            foreach (var (gear, count) in mine.Select(p => (p.Key, p.Value)))
                sb.Append($"{CodexPages.LinkTo(gear, c.Name(gear))}  {Count(count, cost)}<pos=70%>{Use(SummonLink + gear, Loc.T("Summon"), Why(c.WhyNoSummon(kind), count, cost))}\n");
            KindStatus(sb, c, kind);
        }

        sb.Append(PageBlocks.Section(KindName(CoinKind.Adventurer), KindIcon(CoinKind.Adventurer)));
        var chosen = coins.Where(p => RewardRules.KindOf(p.Key) == CoinKind.Adventurer).ToList();
        foreach (var (gear, count) in chosen.Select(p => (p.Key, p.Value)))
            sb.Append($"{CodexPages.LinkTo(gear, c.Name(gear))}  x{count}<pos=70%>{Use(SummonLink + gear, Loc.T("Summon"), c.WhyNoSummon(CoinKind.Adventurer))}\n");
        if (chosen.Count > 0)
            KindStatus(sb, c, CoinKind.Adventurer);
        foreach (string adventurer in RewardRules.Adventurers)
        {
            string item = CodexCatalog.GearId(Characters.Gear.Gear.Type.Item, RewardRules.ChosenItem(adventurer));
            int have = RewardStore.AdventurerProgress(adventurer);
            sb.Append(PageBlocks.Bar(AdventurerName(adventurer) + $"<size=80%><color={Dim}>  · {c.Name(item)}</color></size>",
                $"{have} / {c.AdventurerKills}", (double)have / Math.Max(1, c.AdventurerKills)));
        }

        if (RewardStore.OmenCoins > 0)
        {
            int cost = RewardRules.CoinCost(CoinKind.Omen, c.CoinsPerSummon);
            string why = Why(c.WhyNoSummon(CoinKind.Omen), RewardStore.OmenCoins, cost);
            sb.Append(PageBlocks.Section(KindName(CoinKind.Omen), KindIcon(CoinKind.Omen)));
            sb.Append($"{Loc.T("Omen coins")}  {Count(RewardStore.OmenCoins, cost)}\n");
            if (c.OmenChoices.Count == 0)
                sb.Append($"<color={Dim}>{Loc.T("Meet an Omen item first: an Omen coin summons one you have seen.")}</color>\n");
            foreach (string omen in c.OmenChoices)
                sb.Append($"<indent=4%>{CodexPages.LinkTo(omen, c.Name(omen))}</indent><pos=70%>{Use(OmenLink + omen, Loc.T("Summon"), why)}\n");
            KindStatus(sb, c, CoinKind.Omen);
        }

        var (earned, summons) = RewardStore.CoinTotals;
        sb.Append(PageBlocks.Section(Loc.T("How to earn coins"), "pix:flag"));
        sb.Append(Loc.T("Break a legendary item or skull, or leave it behind on a map: its coin (a skull raised with bones gives its first form's coin). Shop wares and summoned gear don't count.")).Append('\n');
        sb.Append(Loc.F("Win {0} times over one adventurer: its coin, for its rare legendary item (summoned in a Dark Mirror run).", c.AdventurerKills)).Append('\n');
        sb.Append(Loc.T("End a run holding Omen items: an Omen coin for each, for an Omen item of your choice.")).Append('\n');
        sb.Append($"<color={Dim}>{Loc.F("Coins earned in this slot: {0} · summons: {1}", earned, summons)}</color>\n");
        return sb.ToString();
    }

    private static string AdventurerName(string adventurer) => adventurer switch
    {
        "Warrior" => Loc.T("Warrior"),
        "Cleric" => Loc.T("Cleric"),
        "Hero" => Loc.T("Hero"),
        "Magician" => Loc.T("Mage"),
        "Archer" => Loc.T("Hunter"),
        _ => Loc.T("Thief"),
    };

    // ------------------------------------------------------------------ quests

    private static string QuestsText(RewardContext c)
    {
        var sb = new StringBuilder();
        var (done, quartz, gold) = RewardStore.QuestTotals;
        sb.Append(PageBlocks.Section(Loc.T("Kill quests"), "pix:flag"));
        sb.Append(Loc.F("Each step pays {0} dark quartz and {1} gold, right where the enemy fell.", c.QuestDarkQuartz, c.QuestGold)).Append('\n');
        if (c.QuestsPerRun > 0)
            sb.Append(Loc.F("About {0} quests in a full run (all five chapters): about {1} dark quartz and {2} gold.",
                RunPages.N(Math.Round(c.QuestsPerRun)), RunPages.N(Math.Round(c.QuestsPerRun * c.QuestDarkQuartz / 5) * 5),
                RunPages.N(Math.Round(c.QuestsPerRun * c.QuestGold / 5) * 5))).Append('\n');
        sb.Append($"<color={Dim}>{Loc.F("Quests done: {0} · paid {1} dark quartz and {2} gold", done, RunPages.N(quartz), RunPages.N(gold))}</color>\n");

        var quests = c.Enemies
            .Select(e => (e.id, e.name, quest: RewardStore.Quest(e.id)))
            .Where(x => x.quest.Kills > 0 || x.quest.Done > 0)
            .OrderByDescending(x => (double)x.quest.Kills / Math.Max(1, c.QuestStep(x.id))).ToList();
        sb.Append(PageBlocks.Section(Loc.T("In progress"), "pix:hourglass"));
        if (quests.Count == 0)
            sb.Append($"<color={Dim}>{Loc.T("Defeat enemies in a run: every kind starts its own quest.")}</color>\n");
        foreach (var (id, name, quest) in quests)
        {
            int step = c.QuestStep(id);
            string doneText = quest.Done > 0 ? $"<size=80%><color={Dim}>  {Loc.P("done {0} time", "done {0} times", quest.Done)}</color></size>" : "";
            sb.Append(PageBlocks.Bar(CodexPages.LinkTo(id, name) + doneText, $"{quest.Kills} / {step}", (double)quest.Kills / Math.Max(1, step)));
        }
        sb.Append($"<color={Dim}>{Loc.T("A step is about two runs' worth of that enemy (at least 10 kills, at most 150); dark enemies count double. Bosses and adventurers have none.")}</color>\n");
        return sb.ToString();
    }

    // ------------------------------------------------------------------ activations

    private static string ActivationsText(RewardContext c)
    {
        var sb = new StringBuilder();
        sb.Append(PageBlocks.Section(Loc.T("Use an activation"), "pix:star"));
        sb.Append(Loc.F("Activations held: {0} · used this run: {1} of {2}", RewardStore.Activations, RewardStore.ActivatedThisRun.Count, c.ActivationLimit)).Append('\n');
        if (c.WhyNoActivation.Length > 0)
            sb.Append($"<color={Dim}>{c.WhyNoActivation}</color>\n");
        else
            foreach (var (key, name) in c.ActivationChoices)
                sb.Append($"{CodexPages.LinkTo("inscription:" + key, name)}<pos=70%>{CodexPages.LinkTo(ActivateLink + key, Loc.T("+1 for this run"))}\n");
        if (RewardStore.ActivatedThisRun.Count > 0)
            sb.Append($"<color={Dim}>{Loc.F("Raised in this run: {0}", string.Join(", ", RewardStore.ActivatedThisRun.Select(k => c.Name("inscription:" + k))))}</color>\n");
        var maxed = RewardStore.MaxedThisRun.ToList();
        if (maxed.Count > 0)
            sb.Append($"<color={Dim}>{Loc.F("Completed in this run: {0}. Each gives an activation when the run ends.", string.Join(", ", maxed.Select(k => c.Name("inscription:" + k))))}</color>\n");

        // Umut 2026-10-26: "the description on how to earn them is very weird": earn and use, one line each.
        sb.Append(PageBlocks.Section(Loc.T("How it works"), "pix:flag"));
        sb.Append(Loc.F("Earn: complete an inscription in a run (bring it to its last step, like {0} at 4). When the run ends, won or lost, each inscription you completed gives one activation.",
            c.Name("inscription:Brave"))).Append('\n');
        sb.Append(Loc.P("Use: in a later run, pick an inscription you have. It counts one more until that run ends, like the Dark Mirror's banners. {0} activation per run, each inscription once.",
            "Use: in a later run, pick an inscription you have. It counts one more until that run ends, like the Dark Mirror's banners. {0} activations per run, each inscription once.", c.ActivationLimit)).Append('\n');
        sb.Append($"<color={Dim}>{Loc.T("Not for Sun and Moon, Masterpiece, Sin or Omen. An inscription completed only with an activation's help earns none.")}</color>\n");
        return sb.ToString();
    }
}

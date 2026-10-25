using System;
using System.Collections.Generic;
using System.Linq;
using Data;
using DamageInsight.Codex;
using DamageInsight.Daily;
using DamageInsight.History;
using DamageInsight.SaveSlots;
using Platforms;
using Singletons;

namespace DamageInsight.Achievements;

/// <summary>
/// The game side of the slot's achievements (0.14): every time the game gives one (Patches/AchievementPatches: the
/// game calls it again and again, also when Steam has it), the slot keeps the first time and the notice shows. A slot
/// older than 0.14 first gets what its save proves (once, silently), and slot 1 can take over Steam's achievements.
/// Nothing is kept in the daily slot (it borrows the Codex of the slot it was entered from, and its evened-out
/// profile would hand everything out).
/// </summary>
public static class AchievementTracker
{
    private static readonly HashSet<string> UnknownSaid = new();

    private static bool Recording => !DailyRun.InDailySlot && !CodexTracker.Paused;

    /// <summary>The game gives an achievement (PlatformManager.SetAchievement).</summary>
    public static void OnSet(Achievement.Type type)
    {
        if (!Recording)
            return;
        string id = type.ToString();
        var info = AchievementCatalog.Get(id);
        if (info == null)
        {
            if (UnknownSaid.Add(id))
                Plugin.Log.LogInfo($"Achievements: the game gave {id}, which the table doesn't know (a game update?)");
            return;
        }
        // An older slot: what its save proves comes first (silently), so it doesn't pop up as new.
        CheckProofs();
        if (!AchievementStore.Earn(id, DateTime.UtcNow, EarnedHow.Run, RunJournal.Current?.Id ?? ""))
            return;
        Plugin.Log.LogInfo($"Achievements: slot {SlotStore.Active} earned {id} ({AchievementStore.Count()} / {AchievementCatalog.All.Count})");
        if (Plugin.AchievementNotice?.Value != false)
            DamageInsight.UI.AchievementToast.Show(info);
    }

    /// <summary>Once per slot: takes what the save proves (the book's first look at the page, the first earn).</summary>
    public static void CheckProofs()
    {
        if (!Recording || AchievementStore.ProofsChecked)
            return;
        var proven = Proven();
        var now = DateTime.UtcNow;
        int added = proven.Count(id => AchievementStore.Earn(id, now, EarnedHow.Save));
        AchievementStore.MarkProofsChecked();
        Plugin.Log.LogInfo($"Achievements: slot {SlotStore.Active}: {added} proven by the save and the Codex ({string.Join(", ", proven)})");
    }

    /// <summary>
    /// What the save and the Codex prove (only things the game can't give again in this slot, or would only give
    /// again much later): the game's permanent counters and flags, the stage best times (a chapter reached proves the
    /// boss before it), the Codex's kills and skulls, the run history.
    /// </summary>
    private static List<string> Proven()
    {
        var ids = new List<string>();
        void Add(string id)
        {
            if (!ids.Contains(id) && AchievementCatalog.Get(id) != null)
                ids.Add(id);
        }
        void Try(string what, Action read)
        {
            try
            {
                read();
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Achievements: {what} not read for the save's proofs: {e.Message}");
            }
        }

        Try("the counters", () =>
        {
            if (GameData.Progress.deaths > 0) Add("TheLegendBegins");
            if (GameData.Progress.totalAdventurerKills >= 100) Add("HeroSlayer");
            if (GameData.Currency.heartQuartz.totalIncome >= 100) Add("Darkheart");
            if (GameData.Progress.cutscene.GetData(CutScenes.Key.ending)) Add("GoHome");
        });
        Try("the Dark Mirror", () =>
        {
            int cleared = Math.Min(GameData.HardmodeProgress.clearedLevel, AchievementCatalog.DarkMirrorLevels.Length - 1);
            if (cleared >= 0) Add("AnotherHarmonia");
            for (int level = 0; level <= cleared; level++)
                Add(AchievementCatalog.DarkMirrorLevels[level]);
            var castle = GameData.HardmodeProgress.instance.demonCastleDefense;
            if (castle.emperorCleared) { Add("TheWarBegins"); Add("TruePeace"); }
            if (castle.hiddenBossClearedLevel >= 0) Add("TrueSalvation");
            if (castle.fragmentGainCount >= Achievement.BoneSpecialistCount) Add("BoneSpecialist");
            if (castle.CountOfGainedSuperInscription() >= Achievement.WitchsHelpCount) Add("WitchsHelp");
            foreach (Hardmode.DCDefense.HiddenBossClues.Type clue in Enum.GetValues(typeof(Hardmode.DCDefense.HiddenBossClues.Type)))
                if (castle.GetHiddenBossClues(clue)) { Add("HiddenExistence"); break; }
        });
        Try("the stage best times", () =>
        {
            foreach (var value in PersistentSingleton<PlatformManager>.Instance.platform.data.IntData)
            {
                if (value.Value == int.MaxValue || !value.Key.StartsWith(GameRecords.BestTimePrefix, StringComparison.Ordinal))
                    continue;
                string chapter = value.Key.Substring(GameRecords.BestTimePrefix.Length).Split('/')[0].Replace("Hardmode", "");
                switch (chapter)
                {
                    case "Chapter2": Add("SavedElderEnt"); break;
                    case "Chapter3": Add("TheendofGoldManeKnights"); break;
                    case "Chapter4": Add("FakeGoddessGone"); break;
                    case "Chapter5": Add("LeoniasFall"); break;
                    case "Chapter6": Add("TheWarBegins"); break;
                }
            }
        });
        Try("the Codex", () =>
        {
            foreach (var (enemy, id) in new[]
            {
                ("enemy:Yggdrasil", "SavedElderEnt"), ("enemy:AwakenLeiana", "TheendofGoldManeKnights"), ("enemy:Chimera", "FakeGoddessGone"),
                ("enemy:Pope", "LeoniasFall"), ("enemy:FirstHero3", "FirstHeroLastBattle"), ("enemy:DarkSkul2", "TrueSalvation"),
            })
                if ((CodexTracker.Progress.Peek(enemy)?.Kills ?? 0) > 0)
                    Add(id);
            foreach (var entry in CodexCatalog.Entries.Where(e => e.Category == CodexCategory.Skulls && e.Group == "Legendary"))
                if ((CodexTracker.Progress.Peek(entry.Id)?.PickedUp ?? 0) > 0 && AchievementCatalog.ForSkull(entry.Key) is { Kind: AchievementKind.Skull } skull)
                    Add(skull.Id);
        });
        Try("the run history", () =>
        {
            foreach (var run in RunJournal.Runs())
            {
                if (run.Easy) Add("RookieWelcome");
                if (run.DarkMirror && run.Result == RunResult.Death) Add("NewBeginning");
                if (run.Ending == "normal") Add("FirstHeroLastBattle");
                if (run.Ending == "emperor") Add("TruePeace");
            }
        });
        return ids;
    }

    /// <summary>This slot's earn of an achievement; for SkulInsight's daily ones the daily player's.</summary>
    public static EarnedAchievement EarnedOf(AchievementInfo info)
    {
        if (info == null || info.Kind != AchievementKind.Daily)
            return AchievementStore.Get(info?.Id);
        return DailyService.State.Achievements.TryGetValue(info.Id, out var at)
            ? new EarnedAchievement { Id = info.Id, At = at, How = EarnedHow.Run } : null;
    }

    /// <summary>A counted achievement's progress in this save (the daily ones: the server's last word), or null.</summary>
    public static (int have, int need)? Progress(AchievementInfo info)
    {
        if (info == null || info.Target <= 0)
            return null;
        if (info.Kind == AchievementKind.Daily)
            return (Math.Min(DailyService.State.Counter(info.Counts), info.Target), info.Target);
        try
        {
            int have = info.Id switch
            {
                "HeroSlayer" => GameData.Progress.totalAdventurerKills,
                "Darkheart" => GameData.Currency.heartQuartz.totalIncome,
                "BoneSpecialist" => GameData.HardmodeProgress.instance.demonCastleDefense.fragmentGainCount,
                "WitchsHelp" => GameData.HardmodeProgress.instance.demonCastleDefense.CountOfGainedSuperInscription(),
                _ => -1,
            };
            return have < 0 ? null : (Math.Min(have, info.Target), info.Target);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Slot 1 is the game you had before save slots: it may take over your Steam account's achievements once.</summary>
    public static bool CanTakeOverSteam => Recording && SlotStore.Active == 1 && AchievementStore.SteamTaken == null;

    /// <summary>Copies Steam's earned achievements into this slot (with Steam's unlock times); how many were new.</summary>
    public static int TakeOverSteam()
    {
        if (!CanTakeOverSteam)
            return 0;
        int added = 0;
        foreach (var info in AchievementCatalog.All.Where(a => a.OnSteam))
            if (AchievementSteam.TryState(info.Id, out bool achieved, out var at) && achieved
                && AchievementStore.Earn(info.Id, at ?? DateTime.UtcNow, EarnedHow.Steam))
                added++;
        AchievementStore.MarkSteamTaken(DateTime.UtcNow);
        Plugin.Log.LogInfo($"Achievements: slot 1 took over {added} achievements from Steam");
        return added;
    }

    /// <summary>The veil is down: earned, revealed by holding, the setting is off, or its boss was met.</summary>
    public static bool Unveiled(AchievementInfo info)
    {
        if (info == null || !info.Secret || Plugin.AchievementVeil?.Value == false)
            return true;
        if (AchievementStore.IsEarned(info.Id) || AchievementStore.IsRevealed(info.Id))
            return true;
        if (info.LiftedBy.Length == 0)
            return false;
        var page = CodexTracker.Progress.Peek(info.LiftedBy);
        return page != null && (page.Seen > 0 || page.Kills > 0);
    }
}

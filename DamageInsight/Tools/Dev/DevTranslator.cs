using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx;
using Scenes;
using Services;
using Singletons;
using TMPro;
using UnityEngine;

namespace DamageInsight.Tools;

/// <summary>
/// Translates the Korean labels of Skul's developer menu (F2) and the developers' test map to English.
/// Texts it doesn't know yet are written to BepInEx\DamageInsight\untranslated.txt so they can be added.
/// </summary>
public sealed class DevTranslator : MonoBehaviour
{
    private const float Interval = 0.5f;
    private float _next;
    private readonly HashSet<string> _reported = new();

    public static string UntranslatedFile => Path.Combine(Paths.BepInExRootPath, "DamageInsight", "untranslated.txt");

    /// <summary>Whole labels. Checked first, exact match (after trimming).</summary>
    private static readonly Dictionary<string, string> Labels = new()
    {
        // Developer menu, main page
        ["신화 DLC"] = "Mythology DLC",
        ["신화\nDLC"] = "Mythology\nDLC",
        ["하드모드"] = "Hard mode",
        ["단계"] = "Hard mode level",
        ["클리어한 최대 레벨"] = "Highest level cleared",
        ["클리어한\n최대 레벨"] = "Highest level\ncleared",
        ["클리어한 횟수"] = "Times cleared",
        ["클리어한\n횟수"] = "Times\ncleared",
        ["무한부활"] = "Infinite revive",
        ["체력수치표시"] = "Show HP numbers",
        ["다음 스테이지"] = "Next stage",
        ["다음 맵"] = "Next map",
        ["장비 목록"] = "Gear list",
        ["데이터 컨트롤"] = "Save data",
        ["로그 보기"] = "Show log",
        ["UI 숨기기"] = "Hide UI",
        ["스킬 리롤"] = "Reroll skills",
        ["10,000 골드"] = "+10,000 Gold",
        ["1,000 마석"] = "+1,000 Dark Quartz",
        ["100 뼈조각"] = "+100 Bone shards",
        ["100 심장마석"] = "+100 Heart Quartz",
        ["각성"] = "Awaken skull",
        ["오른쪽 3개 전부"] = "All 3 buffs →",
        ["공격력 100배"] = "Attack x100",
        ["쿨다운제거"] = "No cooldowns",
        ["체력 1만"] = "10,000 HP",
        ["방어막 +10"] = "Shield +10",
        ["테스트 맵"] = "Test map",
        ["맵 목록"] = "Map list",
        // Test map signs
        ["모험가"] = "Adventurers",
        ["검은적"] = "Dark enemies",
        ["적 생성"] = "Spawn enemies",
        ["필드 NPC"] = "Field NPCs",
        ["보스"] = "Bosses",
        ["적 소환"] = "Summon enemy",
        ["포션드랍"] = "Drop potion",
        ["단발변경"] = "Single shot",
        ["연발변경"] = "Rapid fire",
        ["중지"] = "Stop",
        ["풀어주기"] = "Release",
        ["대화하기"] = "Talk",
        ["들어가기"] = "Enter",
        // Sub-pages (gear list, save data, stats)
        ["100 뼛조각"] = "+100 Bone shards",
        ["신화팩\nDLC"] = "Mythology pack\nDLC",
        ["뒤로가기"] = "Back",
        ["뒤로"] = "Back",
        ["다음맵"] = "Next map",
        ["드랍시 해금"] = "Unlock on drop",
        ["드랍시 해금해제"] = "Don't unlock on drop",
        ["1회차 클리어"] = "Mark first clear",
        ["시드 초기화"] = "Reset seed",
        ["데이터 초기화"] = "Reset save data",
        ["회 클리어"] = "clears",
        ["다크 미러"] = "Dark Mirror",
        ["마왕성 방어전용\nn회 클리어"] = "Demon Castle defense\ncleared n times",
        ["힌트 설정 (true/false)"] = "Hints (true/false)",
        ["랜덤 아이템 드랍"] = "Drop random item",
        ["기어 해금"] = "Unlock all gear",
        ["검은능력 해금"] = "Unlock dark abilities",
        ["검은능력 해금 초기화"] = "Reset dark ability unlocks",
        ["검은능력 초기화"] = "Reset dark abilities",
        ["인벤토리 초기화"] = "Clear inventory",
        ["추가 Stat, + 되는 양"] = "Bonus stats (amount added)",
        ["적용"] = "Apply",
        ["최종"] = "Final",
    };

    static DevTranslator()
    {
        // The stat page shows the game's internal Korean stat names (Stat.Kind.name, e.g. "공격력/물리").
        // Their C# field names are English ("PhysicalAttackDamage"), so all 46 stats translate automatically.
        foreach (var field in typeof(Characters.Stat.Kind).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
        {
            if (field.GetValue(null) is Characters.Stat.Kind kind && !string.IsNullOrEmpty(kind.name) && !Labels.ContainsKey(kind.name))
                Labels[kind.name] = Humanize(field.Name);
        }
    }

    /// <summary>"PhysicalAttackDamage" → "Physical attack damage".</summary>
    public static string Humanize(string name)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c) && !char.IsUpper(name[i - 1]))
                sb.Append(' ').Append(char.ToLowerInvariant(c));
            else
                sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Words replaced inside longer texts, when no whole label matches.</summary>
    private static readonly (string korean, string english)[] Words =
    {
        ("심장마석", "Heart Quartz"), ("마석", "Dark Quartz"), ("뼈조각", "Bone shards"), ("골드", "Gold"),
        ("하드모드", "Hard mode"), ("체력", "HP"), ("공격력", "Attack"), ("방어막", "Shield"),
        ("쿨다운", "cooldown"), ("스킬", "skill"), ("아이템", "item"), ("장비", "gear"), ("무기", "skull"),
        ("정수", "quintessence"), ("각인", "inscription"), ("레벨", "level"), ("단계", "level"), ("맵", "map"),
        ("스테이지", "stage"), ("챕터", "chapter"), ("검은적", "dark enemy"), ("모험가", "adventurer"),
        ("초기화", "reset"), ("저장", "save"), ("불러오기", "load"), ("삭제", "delete"), ("잠금 해제", "unlock"),
        ("해금", "unlock"), ("전부", "all"), ("추가", "add"), ("닫기", "close"), ("확인", "OK"), ("취소", "cancel"),
    };

    private void Update()
    {
        if (!Plugin.DevToolsEnabled.Value || Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + Interval;

        try
        {
            var panel = Scene<GameBase>.instance?.uiManager?.testingTool;
            if (panel != null && panel.gameObject.activeInHierarchy)
                TranslateUnder(panel.transform);

            var map = Singleton<Service>.Instance?.levelManager?.currentChapter?.map;
            if (map != null && map.name.Contains("CheatMap"))
                TranslateUnder(map.transform);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Dev translator: {e.Message}");
            _next = Time.unscaledTime + 10f;
        }
    }

    private void TranslateUnder(Transform root)
    {
        foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
        {
            string original = text.text;
            if (!HasHangul(original))
                continue;
            string translated = Translate(original);
            if (translated != original)
                text.text = translated;
            if (HasHangul(translated))
                Report(original);
        }
    }

    public static string Translate(string text)
    {
        string trimmed = text.Trim().Replace("\r", "");
        if (Labels.TryGetValue(trimmed, out var label))
            return label;
        var sb = new StringBuilder(text);
        foreach (var (korean, english) in Words)
            sb.Replace(korean, english);
        return sb.ToString();
    }

    public static bool HasHangul(string s) =>
        !string.IsNullOrEmpty(s) && s.Any(c => (c >= '가' && c <= '힣') || (c >= 'ᄀ' && c <= 'ᇿ') || (c >= '㄰' && c <= '㆏'));

    private void Report(string text)
    {
        if (!_reported.Add(text))
            return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(UntranslatedFile));
            File.AppendAllText(UntranslatedFile, text.Replace("\n", "\\n") + "\n", Encoding.UTF8);
        }
        catch
        {
            // best effort
        }
    }
}

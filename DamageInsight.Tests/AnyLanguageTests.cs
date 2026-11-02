using System.Collections.Generic;
using DamageInsight.Lang;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Runs kept in their player's language, shown in the viewer's (0.16.1, Umut 2026-11-01).</summary>
[Collection("Loc")]
public class AnyLanguageTests
{
    // Columns as the game's string table has them (Loc.GameOrder): ko, en, ja, zh-Hans, zh-Hant, de, es, pt-BR, ru, pl, fr.
    private static readonly IReadOnlyList<string[]> Rows = new List<string[]>
    {
        new[] { "나이트메어", "Nightmare", "ナイトメア", "梦魇", "夢魘", "Albtraum", "Pesadilla", "Pesadelo", "Кошмар", "Koszmar", "Cauchemar" },
        new[] { "지옥 바이크", "Hell Bike", "ヘルバイク", "地狱摩托", "地獄摩托", "Höllenrad", "Moto infernal", "Moto infernal", "Адский байк", "Piekielny motor", "Moto infernale" },
        new[] { "갈고리 사슬", "Hook Chain", "フックチェーン", "钩爪锁链", "鉤爪鎖鏈", "Hakenkette", "Cadena", "Corrente", "Цепь", "Łańcuch", "Chaîne" },
        new[] { "숲 깊은 곳", "Deep Forest", "森の奥", "森林深处", "森林深處", "Tiefer Wald", "Bosque", "Floresta", "Чаща", "Las", "Forêt" },
    };

    [Fact]
    public void A_chinese_run_reads_in_the_viewers_language()
    {
        var before = AnyLanguage.GameRows;
        AnyLanguage.GameRows = () => Rows;
        AnyLanguage.Reset();
        try
        {
            // The damage table's "skull: attack kind" (the mod's words found in its own files), skills, stages.
            Assert.Equal("Nightmare: Skills", AnyLanguage.Here("梦魇：技能"));
            Assert.Equal("Hell Bike · Hook Chain", AnyLanguage.Here("地狱摩托 · 钩爪锁链"));
            Assert.Equal("Deep Forest", AnyLanguage.Here("森林深处"));
            Assert.Equal("Nightmare: Swap", AnyLanguage.Here("Albtraum: Tausch"));        // a German player's run
            Assert.Equal("Some Player Name", AnyLanguage.Here("Some Player Name"));       // not a name of the game
            Assert.Equal("", AnyLanguage.Here(null));
            Loc.With("de", () =>
            {
                Assert.Equal("Albtraum: Fertigkeiten", AnyLanguage.Here("梦魇：技能"));
                Assert.Equal("Höllenrad · Hakenkette", AnyLanguage.Here("Hell Bike · Hook Chain"));
            });
            Loc.With("zh-Hans", () => Assert.Equal("梦魇：技能", AnyLanguage.Here("Nightmare: Skills")));
        }
        finally
        {
            AnyLanguage.GameRows = before;
            AnyLanguage.Reset();
        }
    }
}

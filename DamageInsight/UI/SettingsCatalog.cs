using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using DamageInsight.Codex;
using DamageInsight.Lang;

namespace DamageInsight.UI;

public enum SettingKind
{
    /// <summary>Arrows step through <see cref="SettingItem.Values"/> (on/off, numbers, icons).</summary>
    Choice,
    /// <summary>A key: select the row and press the new key.</summary>
    Key,
    /// <summary>A button that does something (<see cref="SettingItem.Action"/>), e.g. reset.</summary>
    Action,
}

/// <summary>One row of the in-game settings page.</summary>
public sealed class SettingItem
{
    public string Section = "", Label = "", Help = "";
    public SettingKind Kind;
    public ConfigEntryBase Entry;
    /// <summary>Action rows: what the page does when the button is pressed (see <see cref="SettingsCatalog"/>).</summary>
    public string Action;
    /// <summary>Lists and on/off go round at the ends; numbers stop at their smallest and largest value.</summary>
    public bool Wraps = true;
    /// <summary>The value is an icon spec (see <see cref="IconLibrary"/>); the page shows the icon next to it.</summary>
    public bool ShowsIcon;

    internal readonly List<object> Steps = new();
    internal Func<object, string> Formatter = v => Convert.ToString(v, CultureInfo.InvariantCulture);

    public string Format(object value) => value == null ? "" : Formatter(value);

    public string DefaultText => Entry == null ? "" : Format(Entry.DefaultValue);

    /// <summary>
    /// The values the arrows step through: the steps plus the entry's current value, in case a hand-edited
    /// config holds one between them (numbers keep their order).
    /// </summary>
    public List<object> Values()
    {
        var values = new List<object>(Steps);
        object current = Entry?.BoxedValue;
        if (current == null || values.Any(v => Same(v, current)))
            return values;
        if (IsNumber(current))
        {
            double c = Convert.ToDouble(current, CultureInfo.InvariantCulture);
            int at = values.FindIndex(v => IsNumber(v) && Convert.ToDouble(v, CultureInfo.InvariantCulture) > c);
            values.Insert(at < 0 ? values.Count : at, current);
        }
        else
        {
            values.Insert(0, current);
        }
        return values;
    }

    /// <summary>Where <paramref name="value"/> is in <paramref name="values"/> (0 if it isn't).</summary>
    public static int IndexOf(IList<object> values, object value)
    {
        for (int i = 0; i < values.Count; i++)
            if (Same(values[i], value))
                return i;
        return 0;
    }

    internal static bool Same(object a, object b)
    {
        if (IsNumber(a) && IsNumber(b))
            return Math.Abs(Convert.ToDouble(a, CultureInfo.InvariantCulture) - Convert.ToDouble(b, CultureInfo.InvariantCulture)) < 1e-4;
        return Equals(a, b);
    }

    private static bool IsNumber(object v) => v is float or double or int or long;
}

/// <summary>
/// What the in-game settings page shows: every entry of the mod's config file as a row (grouped by section, in a
/// fixed section order), with a short label, the values its arrows step through and the config's own description
/// as help text. Numbers get sensible ranges from <see cref="Specs"/>; the Codex unlock numbers take theirs from
/// <see cref="CodexBalance"/>. New config entries show up by themselves (on/off, keys and enums need nothing).
/// </summary>
public static class SettingsCatalog
{
    /// <summary>Action rows: put the combat log window back to its default place.</summary>
    public const string ResetWindow = "ResetWindow";
    /// <summary>Action rows: every setting back to its default.</summary>
    public const string ResetAll = "ResetAll";

    private enum Unit { Plain, Seconds, Percent }

    private sealed class Spec
    {
        public string Label, Help;
        public double Min, Max, Step;
        public Unit Unit;
        public bool Hidden;
    }

    // Config sections in page order, with the heading shown for them. Unknown sections follow in config order.
    private static readonly (string section, string heading)[] Sections =
    {
        ("General", Loc.N("General")),
        ("Damage Numbers", Loc.N("Damage numbers")),
        ("Damage Icons", Loc.N("Damage icons")),
        ("Boss Health Bar", Loc.N("Boss health bars")),
        ("Descriptions", Loc.N("Descriptions")),
        ("Combat Log", Loc.N("Combat log")),
        ("Mini Log", Loc.N("Mini log")),
        ("Cooldown Ticker", Loc.N("Cooldown seconds")),
        ("Codex", Loc.N("Codex")),
        ("Codex balance", Loc.N("Codex unlocks")),
        ("Developer", Loc.N("Developer")),
    };

    private static readonly Dictionary<string, string> BalanceGroups = new()
    {
        ["Enemies"] = Loc.N("Codex unlocks: enemies"),
        ["Bosses"] = Loc.N("Codex unlocks: bosses"),
        ["Gear"] = Loc.N("Codex unlocks: gear"),
    };

    private static Spec On(string label, string help) => new() { Label = label, Help = help };

    private static Spec Number(string label, double min, double max, double step, Unit unit = Unit.Plain, string help = null) =>
        new() { Label = label, Min = min, Max = max, Step = step, Unit = unit, Help = help };

    /// <summary>
    /// Labels, help and number ranges per "Section/Key" (English; shown translated). The config file keeps its own
    /// English descriptions. Entries not listed get a label from their key and the config's description.
    /// </summary>
    private static readonly Dictionary<string, Spec> Specs = new()
    {
        ["General/Language"] = On(Loc.N("Language"),
            Loc.N("The language of the mod's texts. \"Game language\" follows the game's own setting.")),
        ["Damage Numbers/ShowSourceTag"] = On(Loc.N("Show source"),
            Loc.N("Mark each damage number with where the damage came from: an icon or a short tag (ATK, SKILL, ITEM...).")),
        ["Damage Numbers/UseIcons"] = On(Loc.N("Use icons"),
            Loc.N("Use icons instead of text tags where an icon is set (see Damage icons below).")),
        ["Damage Numbers/HoldSeconds"] = Number(Loc.N("Stay visible"), 0, 5, 0.1, Unit.Seconds,
            Loc.N("How long a damage number stays fully visible after it pops up (the game's own: about 0.15 s).")),
        ["Damage Numbers/FadeSeconds"] = Number(Loc.N("Fade out"), 0.1, 5, 0.1, Unit.Seconds,
            Loc.N("How long a damage number takes to fade out.")),
        ["Boss Health Bar/ShowNumbers"] = On(Loc.N("HP numbers"),
            Loc.N("Show current / maximum HP as text on the health bars of bosses, adventurers and dark enemies.")),
        ["Descriptions/ShowDamageNumbers"] = On(Loc.N("Damage numbers"),
            Loc.N("Add your real damage (with your current stats) to the descriptions of skulls, skills, swaps, items, essences and inscriptions.")),
        ["Descriptions/PreviewPickup"] = On(Loc.N("Pickup preview"),
            Loc.N("Items on the ground, in shops and in the swap menu show their numbers as if you had picked them up (their stats and inscription steps included).")),
        ["Combat Log/ToggleKey"] = On(Loc.N("Open / close key"), Loc.N("The key that opens and closes the combat log.")),
        ["Combat Log/UseDialogueBackground"] = On(Loc.N("Dialogue frame"),
            Loc.N("Use the NPC dialogue box artwork as the window background (off: a plain dark panel).")),
        ["Combat Log/BackgroundOpacity"] = Number(Loc.N("Background"), 0, 1, 0.05, Unit.Percent,
            Loc.N("How solid the dialogue background is (0% invisible, 100% solid).")),
        ["Combat Log/WindowRect"] = new Spec { Hidden = true }, // the "Window position" reset row stands in for it
        ["Combat Log/RecordCalculations"] = On(Loc.N("Calculations"),
            Loc.N("Record how every hit's number came about (stats, items, inscriptions, dark abilities, crits, debuffs); hover a log line to see it.")),
        ["Combat Log/SaveToFile"] = On(Loc.N("Save to file"),
            Loc.N("Also write every hit (with its calculation) to BepInEx/DamageInsight/CombatLogs, one file per game session (the newest 30 are kept).")),
        ["Mini Log/Enabled"] = On(Loc.N("Show mini log"),
            Loc.N("Show a few log lines above the minimap. Can also be switched in the combat log window.")),
        ["Mini Log/Lines"] = Number(Loc.N("Lines"), 1, 8, 1, Unit.Plain, Loc.N("How many lines the mini log shows.")),
        ["Mini Log/HideAfterSeconds"] = Number(Loc.N("Hide after"), 1, 30, 1, Unit.Seconds,
            Loc.N("Hide the mini log when no new damage came in for this long.")),
        ["Mini Log/ShowPie"] = On(Loc.N("Pie chart"), Loc.N("Show a tiny pie chart at the right of the mini log.")),
        ["Mini Log/GapAboveMinimap"] = Number(Loc.N("Gap above map"), 0, 300, 5, Unit.Plain,
            Loc.N("Distance between the minimap and the mini log (in pixels at 1920x1080).")),
        ["Cooldown Ticker/Enabled"] = On(Loc.N("Show seconds"),
            Loc.N("Show the remaining seconds on the item and ability icons at the bottom of the screen (83s, 2m+, 10m).")),
        ["Cooldown Ticker/Opacity"] = Number(Loc.N("Opacity"), 0, 1, 0.05, Unit.Percent, Loc.N("How strongly the seconds are drawn.")),
        ["Codex/Enabled"] = On(Loc.N("Record progress"),
            Loc.N("Record your progress for the Codex (enemies met and killed, gear found) and capture enemy pictures.")),
        ["Codex/ToggleKey"] = On(Loc.N("Open / close key"), Loc.N("The key that opens and closes the Codex book.")),
        ["Codex/ShowMoveHints"] = On(Loc.N("Move hints"),
            Loc.N("Under a boss move in the Codex: when the boss uses it (HP range, distance, cooldown...), read from its AI. Shown once you have beaten the boss.")),
        ["Codex/FilmBossAttacks"] = On(Loc.N("Film boss attacks"),
            Loc.N("Film each boss attack once (a small picture 10 times a second, effects included) to show it in the Codex.")),
    };

    /// <summary>Whether "Section/Key" has its own label and range here (tests: every number in Plugin.cs must).</summary>
    public static bool HasSpec(string section, string key) =>
        Specs.ContainsKey(section + "/" + key) || (section == "Codex balance" && CodexBalance.All.Any(s => s.Key == key));

    /// <summary>
    /// The rows for <paramref name="config"/>, in the current language. <paramref name="inscriptionIcons"/>: the
    /// game's inscription icon names (Arms, Mutation...), the choices for the damage icons;
    /// <paramref name="inscriptionName"/>: their names in the game's language (null: made from the key).
    /// </summary>
    public static List<SettingItem> Build(ConfigFile config, IEnumerable<string> inscriptionIcons, Func<string, string> inscriptionName = null)
    {
        var icons = (inscriptionIcons ?? Enumerable.Empty<string>())
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();

        var items = new List<(int rank, int order, SettingItem item)>();
        var unknownSections = new List<string>();
        int order = 0;
        foreach (var pair in config)
        {
            string section = pair.Key.Section;
            int rank = Array.FindIndex(Sections, s => s.section == section);
            if (rank < 0)
            {
                if (!unknownSections.Contains(section))
                    unknownSections.Add(section);
                rank = Sections.Length + unknownSections.IndexOf(section);
            }

            if (section == "Combat Log" && pair.Key.Key == "WindowRect")
            {
                // Stands in for the hidden WindowRect entry.
                items.Add((rank, order++, new SettingItem
                {
                    Section = Heading(section), Label = Loc.T("Window position"), Kind = SettingKind.Action, Action = ResetWindow,
                    Help = Loc.T("Puts the combat log window back to its default place and size (you can drag it and pull its corners; it remembers where you left it)."),
                }));
                continue;
            }

            var item = Item(pair.Value, icons, inscriptionName);
            if (item != null)
                items.Add((rank, order++, item));
        }
        items.Add((int.MaxValue, order, new SettingItem
        {
            Section = Loc.T("All settings"), Label = Loc.T("Reset everything"), Kind = SettingKind.Action, Action = ResetAll,
            Help = Loc.T("Puts every setting on this page back to its default. Your Codex progress stays as it is."),
        }));
        return items.OrderBy(i => i.rank).ThenBy(i => i.order).Select(i => i.item).ToList();
    }

    private static SettingItem Item(ConfigEntryBase entry, List<string> icons, Func<string, string> inscriptionName)
    {
        var def = entry.Definition;
        Specs.TryGetValue(def.Section + "/" + def.Key, out var spec);
        if (spec?.Hidden == true)
            return null;

        var item = new SettingItem
        {
            Entry = entry,
            Section = Heading(def.Section),
            Label = spec?.Label != null ? Loc.T(spec.Label) : Words(def.Key),
            Help = spec?.Help != null ? Loc.T(spec.Help) : entry.Description?.Description ?? "",
        };

        if (def.Section == "Codex balance" && CodexBalance.All.FirstOrDefault(s => s.Key == def.Key) is { } balance)
        {
            item.Section = BalanceGroups.TryGetValue(balance.Group, out var heading) ? Loc.T(heading) : balance.Group;
            item.Label = Loc.T(balance.Label);
            item.Help = (balance.Help.Length > 0 ? Loc.T(balance.Help) + " " : "") + Loc.T("Takes effect in the Codex at once.");
            spec = Number(balance.Label, balance.Min, balance.Max, balance.Step, Unit.Plain, balance.Help);
        }

        Type type = entry.SettingType;
        if (type == typeof(bool))
        {
            item.Steps.Add(false);
            item.Steps.Add(true);
            item.Formatter = v => v is true ? Loc.T("On") : Loc.T("Off");
        }
        else if (type == typeof(KeyboardShortcut))
        {
            item.Kind = SettingKind.Key;
            item.Formatter = v => v is KeyboardShortcut k && k.MainKey != UnityEngine.KeyCode.None ? k.ToString() : Loc.T("Not set");
            item.Help = (item.Help.Length > 0 ? item.Help + " " : "") + Loc.T("Select it and press the new key (Esc keeps the old one).");
        }
        else if (type.IsEnum)
        {
            foreach (var v in Enum.GetValues(type))
                item.Steps.Add(v);
            item.Formatter = v => Loc.Name(Words(v.ToString()));
        }
        else if (type == typeof(float) || type == typeof(int))
        {
            spec ??= Fallback(entry);
            bool whole = type == typeof(int);
            foreach (double v in NumberSteps(spec.Min, spec.Max, spec.Step))
                item.Steps.Add(whole ? (object)(int)Math.Round(v) : (float)v);
            item.Wraps = false;
            var unit = spec.Unit;
            item.Formatter = v => FormatNumber(Convert.ToDouble(v, CultureInfo.InvariantCulture), unit);
        }
        else if (type == typeof(string) && def.Section == "Damage Icons")
        {
            string source = IconRowLabel(def.Key);
            item.Label = spec?.Label != null ? Loc.T(spec.Label) : source;
            item.Help = Loc.F("The icon next to {0} damage numbers. \"Text tag\" shows a short word instead (ATK, SKILL, ITEM...). Other icons (skill:Name, item:Name...) can be typed into the config file.", source);
            item.ShowsIcon = true;
            item.Steps.Add("none");
            foreach (var name in icons)
                item.Steps.Add("inscription:" + name);
            item.Formatter = v => IconText((string)v, inscriptionName);
        }
        else if (type == typeof(string) && entry.Description?.AcceptableValues is AcceptableValueList<string> list)
        {
            foreach (var v in list.AcceptableValues)
                item.Steps.Add(v);
            item.Formatter = def.Section == "General" && def.Key == "Language" ? LanguageName : v => Loc.Name(Words((string)v));
        }
        else
        {
            return null; // free text has no arrows; nothing else in the config is free text today
        }
        return item;
    }

    /// <summary>"auto" → "Game language", a code → the language's own name ("Deutsch").</summary>
    private static string LanguageName(object value)
    {
        string code = value as string ?? "";
        if (code == "auto")
            return Loc.T("Game language");
        foreach (var (c, name) in Loc.Languages)
            if (c == code)
                return name;
        return code;
    }

    private static string Heading(string section)
    {
        foreach (var (s, heading) in Sections)
            if (s == section)
                return Loc.T(heading);
        return section;
    }

    /// <summary>A range for a number nobody described: up to four times its default.</summary>
    private static Spec Fallback(ConfigEntryBase entry)
    {
        if (entry.Description?.AcceptableValues is AcceptableValueRange<float> fr)
            return Number(null, fr.MinValue, fr.MaxValue, NiceStep((fr.MaxValue - fr.MinValue) / 20.0));
        if (entry.Description?.AcceptableValues is AcceptableValueRange<int> ir)
            return Number(null, ir.MinValue, ir.MaxValue, Math.Max(1, Math.Round(NiceStep((ir.MaxValue - ir.MinValue) / 20.0))));
        double d = Math.Abs(Convert.ToDouble(entry.DefaultValue, CultureInfo.InvariantCulture));
        double max = Math.Max(1, d * 4);
        double step = entry.SettingType == typeof(int) ? Math.Max(1, Math.Round(NiceStep(max / 20))) : NiceStep(max / 20);
        return Number(null, 0, max, step);
    }

    /// <summary>1, 2 or 5 times a power of ten, at least <paramref name="raw"/>.</summary>
    internal static double NiceStep(double raw)
    {
        if (raw <= 0)
            return 1;
        double pow = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        foreach (double m in new[] { 1.0, 2.0, 5.0, 10.0 })
            if (m * pow >= raw - 1e-9)
                return m * pow;
        return 10 * pow;
    }

    /// <summary>
    /// The values from <paramref name="min"/> to <paramref name="max"/> in steps of <paramref name="step"/>. Long
    /// ranges get coarser as the numbers grow (up to 10 one by one, then by 5, from 100 by 50, from 1000 by 500),
    /// so holding an arrow gets anywhere in a few seconds. Both ends are always included.
    /// </summary>
    public static List<double> NumberSteps(double min, double max, double step)
    {
        var values = new List<double>();
        if (step <= 0 || max < min)
        {
            values.Add(min);
            return values;
        }
        bool coarse = (max - min) / step > 60;
        double v = min;
        while (v < max - 1e-9 && values.Count < 1000)
        {
            values.Add(Math.Round(v, 6));
            double s = step;
            if (coarse && v >= 10)
                s = Math.Max(step, Math.Pow(10, Math.Floor(Math.Log10(v + 1e-9))) / 2);
            v = Math.Round((Math.Floor(v / s + 1e-9) + 1) * s, 6); // next multiple of the step
        }
        values.Add(Math.Round(max, 6));
        return values;
    }

    private static string FormatNumber(double v, Unit unit) => unit switch
    {
        Unit.Seconds => v.ToString("0.##", CultureInfo.InvariantCulture) + " s",
        Unit.Percent => Math.Round(v * 100).ToString(CultureInfo.InvariantCulture) + "%",
        _ => v.ToString("0.##", CultureInfo.InvariantCulture),
    };

    /// <summary>The damage source a damage icon row is for, in the current language.</summary>
    private static string IconRowLabel(string source) => source switch
    {
        "Basic" => Loc.T("Basic attacks"),
        "Skill" => Loc.T("Skills"),
        "Item" => Loc.T("Items"),
        "Quintessence" => Loc.T("Quintessences"),
        "Poison" => Loc.T("Poison"),
        "Burn" => Loc.T("Burn"),
        "Bleed" => Loc.T("Bleed"),
        "Shock" => Loc.T("Shock"),
        "Ember" => Loc.T("Ember"),
        "Status" => Loc.T("Other statuses"),
        "Dash" => Loc.T("Dash"),
        "Swap" => Loc.T("Swap"),
        "DarkAbility" => Loc.T("Dark abilities"),
        "Other" => Loc.T("Other"),
        _ => Words(source),
    };

    /// <summary>"inscription:ExcessiveBleeding" → "Excessive bleeding" (or the game's name for it), "none" → "Text tag".</summary>
    internal static string IconText(string spec, Func<string, string> inscriptionName = null)
    {
        if (string.IsNullOrWhiteSpace(spec) || spec.Trim().Equals("none", StringComparison.OrdinalIgnoreCase))
            return Loc.T("Text tag");
        int colon = spec.IndexOf(':');
        if (colon < 0)
            return Words(spec.Trim());
        string kind = spec.Substring(0, colon).Trim(), name = spec.Substring(colon + 1).Trim();
        if (!kind.Equals("inscription", StringComparison.OrdinalIgnoreCase))
            return $"{name} ({kind})";
        string named = inscriptionName?.Invoke(name);
        return string.IsNullOrEmpty(named) ? Words(name) : named;
    }

    /// <summary>"ExcessiveBleeding" → "Excessive bleeding", "HPBar" → "HP bar".</summary>
    internal static string Words(string camel)
    {
        if (string.IsNullOrEmpty(camel))
            return "";
        var sb = new StringBuilder();
        for (int i = 0; i < camel.Length; i++)
        {
            char c = camel[i];
            bool upper = char.IsUpper(c);
            bool wordStart = i > 0 && upper &&
                             (!char.IsUpper(camel[i - 1]) || (i + 1 < camel.Length && char.IsLower(camel[i + 1])));
            if (wordStart && camel[i - 1] != ' ')
                sb.Append(' ');
            // Lower-case words after the first, but keep abbreviations (HP, UI) as they are.
            bool abbreviation = upper && ((i + 1 < camel.Length && char.IsUpper(camel[i + 1])) ||
                                          (i > 0 && char.IsUpper(camel[i - 1]) && (i + 1 == camel.Length || !char.IsLower(camel[i + 1]))));
            sb.Append(wordStart && !abbreviation ? char.ToLowerInvariant(c) : c);
        }
        return sb.ToString();
    }
}

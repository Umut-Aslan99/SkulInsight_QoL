using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Characters;
using Characters.Gear.Synergy.Inscriptions;
using DamageInsight.Describe;
using DamageInsight.Lang;
using DamageInsight.UI;
using Services;
using Singletons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.Codex;

/// <summary>
/// The You page: a better, in-Codex version of the game's inventory. Left: both skulls, the quintessence, the dark
/// abilities, the 9 items, the active inscriptions and the stats; right: the selected thing in full (its Arsenal
/// page text with the player's current stats). Updates while the book is open.
/// </summary>
public sealed partial class CodexWindow
{
    private sealed class Slot
    {
        public Image Background, Icon;
        public TextMeshProUGUI Label;
        public string Id;
    }

    private readonly List<Slot> _youSlots = new();
    private readonly Slot[] _skullSlots = new Slot[2], _itemSlots = new Slot[9], _darkSlots = new Slot[4];
    private Slot _essenceSlot, _statsSlot;
    private readonly List<Slot> _inscriptionSlots = new();
    private TextMeshProUGUI _youEmpty, _darkHeading;
    private RectTransform _youContent;

    private const int InscriptionRows = 9;

    private void ForgetYouWidgets()
    {
        _youSlots.Clear();
        _inscriptionSlots.Clear();
        _essenceSlot = _statsSlot = null;
        _youEmpty = _darkHeading = null;
        _youContent = null;
    }

    private void BuildYou(RectTransform panel)
    {
        _youEmpty = Label(panel, Loc.T("Start a run to see your character here."), 22, InkDim, TextAlignmentOptions.Center);
        _youEmpty.rectTransform.Place(32, 300, PageWidth - 64, 120);
        _youContent = UiKit.Rect("You content", panel).Fill();
        var page = _youContent;
        float half = (PageWidth - 64 - 8) / 2f;

        Heading(page, Loc.T("Skulls"), 32, 84);
        for (int i = 0; i < 2; i++)
            _skullSlots[i] = NewSlot(page, 32 + i * (half + 8), 110, half, 70, 56, "you:skull" + i);

        Heading(page, Loc.T("Quintessence"), 32, 192);
        _essenceSlot = NewSlot(page, 32, 218, half, 70, 56, "you:essence");
        _darkHeading = Heading(page, Loc.T("Dark abilities"), 32 + half + 8, 192);
        for (int i = 0; i < 4; i++)
            _darkSlots[i] = NewSlot(page, 32 + half + 8 + i * 80, 218, 74, 70, 56, "you:dark" + i);

        Heading(page, Loc.T("Items"), 32, 300);
        for (int i = 0; i < 9; i++)
            _itemSlots[i] = NewSlot(page, 32 + i * 75, 326, 69, 69, 56, "you:item" + i);

        Heading(page, Loc.T("Inscriptions"), 32, 408);
        for (int i = 0; i < InscriptionRows * 2; i++)
            _inscriptionSlots.Add(NewSlot(page, 32 + (i / InscriptionRows) * (half + 8), 434 + (i % InscriptionRows) * 34, half, 32, 26, null));

        _statsSlot = NewSlot(page, 32, BookHeight - 24 - 60, half, 44, 32, "you:stats");
        _statsSlot.Label.text = $"<b>{StatsTitle()}</b>";
        _statsSlot.Label.fontSize = 20;
    }

    private TextMeshProUGUI Heading(RectTransform page, string text, float x, float y)
    {
        var label = Label(page, $"<b>{text}</b>", 17, Accent, TextAlignmentOptions.TopLeft);
        label.rectTransform.Place(x, y, 330, 24);
        label.enableWordWrapping = false;
        return label;
    }

    /// <summary>A selectable box: icon on the left (or centred in small boxes), text on the right.</summary>
    private Slot NewSlot(RectTransform page, float x, float y, float width, float height, float icon, string id)
    {
        var bg = UiKit.Image("Slot", page, PageShade);
        bg.rectTransform.Place(x, y, width, height);
        var slot = new Slot { Background = bg, Id = id };
        slot.Icon = UiKit.Image("Icon", bg.transform, Color.white);
        slot.Icon.preserveAspect = true;
        bool iconOnly = width < icon + 40;
        slot.Icon.rectTransform.Place(iconOnly ? (width - icon) / 2 : 7, (height - icon) / 2, icon, icon);
        slot.Label = Label(bg.transform, "", 17, Ink, TextAlignmentOptions.MidlineLeft);
        slot.Label.rectTransform.Place(iconOnly ? 0 : icon + 14, 0, iconOnly ? 0 : width - icon - 20, height);
        slot.Label.enableWordWrapping = true;
        slot.Label.overflowMode = TextOverflowModes.Ellipsis;
        var button = bg.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() =>
        {
            if (slot.Id == null)
                return;
            _nav.Select(slot.Id);
            _listDirty = true;
            _nextRefresh = 0f;
            RefreshSections();
            EventSystem.current?.SetSelectedGameObject(null);
        });
        _youSlots.Add(slot);
        return slot;
    }

    private static Character Player => Singleton<Service>.Instance?.levelManager?.player;

    private static string StatsTitle()
    {
        string title = CodexCatalog.Localize("stat/title");
        return title.Length > 0 ? title : Loc.T("Stats");
    }

    // ---------------------------------------------------------------- left page

    private void DrawYouList()
    {
        var player = Player;
        var inventory = player?.playerComponents?.inventory;
        bool has = inventory != null;
        _youEmpty.gameObject.SetActive(!has);
        _youContent.gameObject.SetActive(has);
        if (!has)
            return;

        var weapons = inventory.weapon;
        for (int i = 0; i < 2; i++)
        {
            var weapon = weapons?.weapons[i];
            var entry = weapon == null ? null : GearEntry(weapon);
            bool held = weapon != null && weapon == weapons.current;
            SetSlot(_skullSlots[i], entry, weapon == null ? "" :
                $"{NameOf(entry, weapon.displayName)}\n<size=80%><color=#8A7058>{(held ? Loc.T("Held") : Loc.T("Swap"))}</color></size>");
        }

        var essence = inventory.quintessence?.items.FirstOrDefault();
        SetSlot(_essenceSlot, essence == null ? null : GearEntry(essence), essence == null ? $"<color=#8A7058>{Loc.T("None")}</color>" : NameOf(GearEntry(essence), essence.displayName));

        var upgrades = inventory.upgrade?.upgrades;
        bool anyDark = upgrades != null && upgrades.Any(u => u != null);
        _darkHeading.gameObject.SetActive(anyDark);
        for (int i = 0; i < 4; i++)
        {
            var upgrade = anyDark && i < upgrades.Count ? upgrades[i] : null;
            _darkSlots[i].Background.gameObject.SetActive(anyDark);
            var entry = upgrade?.reference == null ? null : CodexPages.Entry(CodexCatalog.DarkId(upgrade.reference.name));
            SetSlot(_darkSlots[i], entry, "");
        }

        var items = inventory.item?.items;
        for (int i = 0; i < 9; i++)
        {
            var item = items != null && i < items.Count ? items[i] : null;
            SetSlot(_itemSlots[i], item == null ? null : GearEntry(item), "");
        }

        var active = ActiveInscriptions(inventory);
        for (int i = 0; i < _inscriptionSlots.Count; i++)
        {
            var slot = _inscriptionSlots[i];
            bool shown = i < active.Count;
            slot.Background.gameObject.SetActive(shown);
            if (!shown)
                continue;
            var inscription = active[i];
            slot.Id = "you:insc:" + inscription.key;
            slot.Icon.sprite = SafeInscriptionIcon(inscription.key);
            slot.Icon.color = slot.Icon.sprite == null ? new Color(0, 0, 0, 0) : Color.white;
            string next = NextStepText(inscription, compact: true);
            slot.Label.text = $"{inscription.name}  <b>{inscription.count}</b>" + (next.Length > 0 ? $"  <size=80%><color=#8A7058>{next}</color></size>" : "");
            slot.Label.fontSize = 15;
            slot.Background.color = slot.Id == _selectedId ? RowSelected : PageShade;
        }
        _statsSlot.Background.color = _statsSlot.Id == _selectedId ? RowSelected : PageShade;
    }

    private void SetSlot(Slot slot, CodexEntry entry, string label)
    {
        var icon = entry == null ? null : IconOf(entry);
        slot.Icon.sprite = icon;
        slot.Icon.color = icon == null ? new Color(0, 0, 0, 0) : Color.white;
        slot.Label.text = label;
        slot.Background.color = slot.Id == _selectedId ? RowSelected : PageShade;
    }

    private static string NameOf(CodexEntry entry, string fallback) => entry?.Name ?? fallback ?? "";

    private static CodexEntry GearEntry(Characters.Gear.Gear gear) =>
        CodexPages.Entry(CodexCatalog.GearId(gear.type, gear.name.Replace("(Clone)", "").Trim()));

    /// <summary>The inscriptions with at least one count, highest count first (like the game's inventory).</summary>
    private static List<Inscription> ActiveInscriptions(Characters.Player.Inventory inventory)
    {
        var list = new List<Inscription>();
        var inscriptions = inventory?.synergy?.inscriptions;
        if (inscriptions == null)
            return list;
        foreach (var key in Inscription.keys)
        {
            if (key == Inscription.Key.None)
                continue;
            var inscription = inscriptions[key];
            if (inscription != null && inscription.count > 0)
                list.Add(inscription);
        }
        return list.OrderByDescending(i => i.step).ThenByDescending(i => i.count).Take(InscriptionRows * 2).ToList();
    }

    /// <summary>"Step 2 · 2 more for step 3", "Step 3 (max)" or, compact, "2 more for step 3".</summary>
    private static string NextStepText(Inscription inscription, bool compact)
    {
        var steps = inscription.steps;
        if (steps == null || steps.Count == 0)
            return "";
        int step = inscription.step, last = steps.Count - 1;
        string now = step > 0 ? Loc.F("Step {0}", step) : Loc.T("No step yet");
        if (step >= last)
            return compact ? Loc.T("max") : $"{now} ({Loc.T("max")})";
        int more = steps[step + 1] - inscription.count;
        string next = Loc.P("{0} more for step {1}", "{0} more for step {1}", more, step + 1);
        return compact ? next : $"{now}  ·  {next}";
    }

    // ---------------------------------------------------------------- right page

    private void DrawYouDetail()
    {
        ApplyLayout(PageLayout.Sheet);
        _stage = 3;
        _clipCount = 0;
        _clipPrev.Background.gameObject.SetActive(false);
        _clipNext.Background.gameObject.SetActive(false);
        _clipLabel.text = "";
        foreach (var chip in new[] { _filmChip, _modeChip, _memberChip, _categoryChip })
            chip.Background.gameObject.SetActive(false);
        _nextText.text = "";
        _portraitMark.text = "";

        var player = Player;
        var inventory = player?.playerComponents?.inventory;
        string id = _selectedId ?? CodexNavigation.YouDefault;
        if (inventory == null)
        {
            ShowYou(null, Loc.T("You"), "", Loc.T("Start a run to see your character here."), id);
            return;
        }

        var stats = GearDescriptions.CurrentStats(ignorePreview: true);
        if (id == "you:stats")
        {
            var held = inventory.weapon?.current;
            ShowYou(held == null ? null : IconOf(GearEntry(held)), StatsTitle(), "", StatsText(player, stats), id);
            return;
        }
        if (id.StartsWith("you:insc:"))
        {
            string key = id.Substring("you:insc:".Length);
            var entry = CodexPages.Entry("inscription:" + key);
            var inscription = CodexPages.InscriptionOf(key);
            if (entry == null || inscription == null)
            {
                ShowYou(null, "", "", "", id);
                return;
            }
            var p = CodexTracker.Page(entry.Id);
            string head = $"<b>{Loc.F("Count: {0}", inscription.count)}</b>  ·  {NextStepText(inscription, compact: false)}" +
                          (inscription.isSuper ? $"  ·  <color=#9C3B2A>{Loc.T("Tuned")}</color>" : "") + "\n" + ArsenalLink(entry) + "\n";
            ShowYou(IconOf(entry), entry.Name, CategoryName(entry.Category),
                head + CodexPages.Text(entry, p, Math.Max(2, CodexTiers.Tier(entry.Category, p, entry.Id)), stats), id);
            return;
        }

        Characters.Gear.Gear gear = null;
        CodexEntry gearEntry = null;
        if (id.StartsWith("you:skull") && int.TryParse(id.Substring("you:skull".Length), out int s) && s is >= 0 and < 2)
        {
            var weapon = inventory.weapon?.weapons[s];
            gear = weapon;
            // The swap skull's numbers use its own base damage, not the held skull's.
            var damage = weapon != null ? weapon.GetComponent<AttackDamage>() : null;
            if (damage != null)
            {
                stats = stats.Copy();
                stats.SkullMin = damage.minAttackDamage;
                stats.SkullMax = damage.maxAttackDamage;
            }
        }
        else if (id == "you:essence")
            gear = inventory.quintessence?.items.FirstOrDefault();
        else if (id.StartsWith("you:item") && int.TryParse(id.Substring("you:item".Length), out int i) && i is >= 0 and < 9)
            gear = i < inventory.item.items.Count ? inventory.item.items[i] : null;
        else if (id.StartsWith("you:dark") && int.TryParse(id.Substring("you:dark".Length), out int d) && d is >= 0 and < 4)
        {
            var upgrade = d < inventory.upgrade.upgrades.Count ? inventory.upgrade.upgrades[d] : null;
            gearEntry = upgrade?.reference == null ? null : CodexPages.Entry(CodexCatalog.DarkId(upgrade.reference.name));
        }
        if (gear != null)
            gearEntry = GearEntry(gear);
        if (gearEntry == null)
        {
            ShowYou(null, Loc.T("Empty"), "", $"<color=#8A7058>{Loc.T("Nothing here yet.")}</color>", id);
            return;
        }
        var progress = CodexTracker.Page(gearEntry.Id);
        int tier = Math.Max(2, CodexTiers.Tier(gearEntry.Category, progress, gearEntry.Id));
        ShowYou(IconOf(gearEntry), gearEntry.Name, $"{Loc.Name(gearEntry.Group)} · {CategoryName(gearEntry.Category)}",
            ArsenalLink(gearEntry) + "\n\n" + CodexPages.Text(gearEntry, progress, tier, stats), id);
    }

    private static string ArsenalLink(CodexEntry entry) =>
        CodexPages.LinkTo(entry.Id, Loc.T("Open its Arsenal page")) + "  " + $"<color=#8A7058><size=85%>({Loc.T("neutral numbers, to compare")})</size></color>";

    private void ShowYou(Sprite icon, string title, string subtitle, string text, string id)
    {
        _portrait.sprite = icon;
        _portrait.color = icon == null ? new Color(0, 0, 0, 0) : Color.white;
        _title.text = title;
        _subtitle.text = subtitle;
        SetPage(text);
        string shownFor = "you#" + id;
        if (shownFor != _textShownFor)
        {
            _textShownFor = shownFor;
            _descriptionScroll.verticalNormalizedPosition = 1f;
        }
    }

    // ---------------------------------------------------------------- stats sheet

    /// <summary>
    /// Every stat of the game's own panel (its names, in its format), then the damage stats the panel doesn't show,
    /// each with a short explanation.
    /// </summary>
    private static string StatsText(Character player, StatSnapshot snapshot)
    {
        var stat = player.stat;
        var sb = new StringBuilder();
        string Pct(double v) => (v * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        string Signed(double v) => (v >= 0 ? "+" : "") + (v * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
        string X(double v) => "x" + v.ToString("0.00", CultureInfo.InvariantCulture);
        double F(Stat.Kind kind) => stat.GetFinal(kind);
        void Row(string name, string value, string help)
        {
            sb.Append(name).Append("<pos=58%><b>").Append(value).Append("</b>\n");
            if (help.Length > 0)
                sb.Append($"<size=80%><color=#8A7058>{help}</color></size>\n");
        }
        string Game(string key, string fallback)
        {
            string text = CodexCatalog.Localize($"stat/{key}/name");
            return text.Length > 0 ? text : fallback;
        }

        sb.Append(CodexPages.Heading(Loc.T("As in the game's panel"), "inscription:Fortress"));
        Row(Game("health", Loc.T("Health")), F(Stat.Kind.Health).ToString("0", CultureInfo.InvariantCulture), "");
        Row(Game("takingDamage", Loc.T("Damage taken")), X(F(Stat.Kind.TakingDamage)), Loc.T("Damage you take is multiplied by this (lower is better)."));
        Row(Game("physicalAttackDamage", Loc.T("Physical attack")), Pct(F(Stat.Kind.PhysicalAttackDamage)), Loc.T("Multiplies your physical damage."));
        Row(Game("magicalAttackDamage", Loc.T("Magic attack")), Pct(F(Stat.Kind.MagicAttackDamage)), Loc.T("Multiplies your magic damage."));
        Row(Game("attackSpeed", Loc.T("Attack speed")), Pct(F(Stat.Kind.BasicAttackSpeed)), Loc.T("How fast basic attacks play."));
        Row(Game("movementSpeed", Loc.T("Movement speed")), Pct(F(Stat.Kind.MovementSpeed) / 5.0), "");
        Row(Game("chargingSpeed", Loc.T("Charge speed")), Pct(F(Stat.Kind.ChargingSpeed)), Loc.T("How fast charge attacks fill up."));
        Row(Game("skillCooldownSpeed", Loc.T("Skill cooldown speed")), Pct(F(Stat.Kind.SkillCooldownSpeed)), Loc.T("Cooldowns run this much faster (200% = half the time)."));
        Row(Game("swapCooldownSpeed", Loc.T("Swap cooldown speed")), Pct(F(Stat.Kind.SwapCooldownSpeed)), "");
        Row(Game("essenceCooldownSpeed", Loc.T("Quintessence cooldown speed")), Pct(F(Stat.Kind.EssenceCooldownSpeed)), "");
        Row(Game("criticalChance", Loc.T("Crit chance")), Signed(F(Stat.Kind.CriticalChance) - 1.0), "");
        Row(Game("criticalDamage", Loc.T("Crit damage")), X(F(Stat.Kind.CriticalDamage)), Loc.T("A critical hit deals this much of its damage."));

        sb.Append(CodexPages.Heading(Loc.T("Hidden damage stats"), "inscription:Arms"));
        sb.Append($"<size=85%><color=#8A7058>{Loc.T("Not on the game's panel; the mod's numbers use them.")}</color></size>\n");
        Row(Loc.T("All damage"), Pct(F(Stat.Kind.AttackDamage)), Loc.T("Multiplies every hit (after the other bonuses are added up)."));
        Row(Loc.T("Basic attack damage"), Pct(F(Stat.Kind.BasicAttackDamage)), Loc.T("Added to your attack for basic attacks."));
        Row(Loc.T("Skill damage"), Pct(F(Stat.Kind.SkillAttackDamage)), Loc.T("Added to your attack for skills."));
        Row(Loc.T("Projectile damage"), Pct(F(Stat.Kind.ProjectileAttackDamage)), Loc.T("Added to your attack for projectiles."));
        Row(Loc.T("Skill speed"), Pct(F(Stat.Kind.SkillAttackSpeed)), Loc.T("How fast skills play."));
        Row(Loc.T("Dash cooldown speed"), Pct(F(Stat.Kind.DashCooldownSpeed)), "");
        if (snapshot.SkullMax > 0)
            Row(Loc.T("Skull damage"), DescriptionFormatter.Range(snapshot.SkullMin, snapshot.SkullMax), Loc.T("The held skull's base damage: many hits are a percentage of it."));

        sb.Append(CodexPages.Heading(Loc.T("Status effects"), "inscription:Poisoning"));
        Row(Loc.T("Poison tick"), "-" + F(Stat.Kind.PoisonTickFrequency).ToString("0.##", CultureInfo.InvariantCulture) + " s", Loc.T("Taken off the time between poison hits."));
        Row(Loc.T("Bleed damage"), Pct(F(Stat.Kind.BleedDamage)), "");
        Row(Loc.T("Burn splash damage"), Pct(F(Stat.Kind.EmberDamage)), Loc.T("The splash of a burning enemy (damage and size)."));
        Row(Loc.T("Freeze time"), "+" + F(Stat.Kind.FreezeDuration).ToString("0.##", CultureInfo.InvariantCulture) + " s", "");
        Row(Loc.T("Stun time"), "+" + F(Stat.Kind.StunDuration).ToString("0.##", CultureInfo.InvariantCulture) + " s", "");
        return sb.ToString();
    }
}

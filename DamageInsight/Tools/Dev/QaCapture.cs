using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using BepInEx;
using Characters;
using Characters.Controllers;
using Characters.Gear;
using Characters.Gear.Items;
using Characters.Gear.Quintessences;
using Characters.Gear.Weapons;
using DamageInsight.Codex;
using DamageInsight.Describe;
using DamageInsight.Lang;
using GameResources;
using HarmonyLib;
using Level;
using Scenes;
using Services;
using Singletons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DamageInsight.Tools;

/// <summary>
/// QA capture (docs/QA_CAPTURE_PLAN.md): plays every skull, item and quintessence through the game's own screens and
/// saves what the player sees. Per gear and language: dropped on the ground (popup, popup detail view), picked up,
/// shown in the inventory (simple and detail view, every skill of a skull) — each as a cropped picture plus a JSON with
/// every text box's text and TextMeshPro facts (overflowing, lines, height needed vs. height given, missing glyphs).
/// Runs in the developers' test map with the player invulnerable; the Codex records nothing meanwhile.
/// Started by Developer/QaCaptureOnce (English, where you are), or unattended by a request file
/// (BepInEx/DamageInsight/QA/request.json, written by tools/qa/night-capture.ps1): then it also skips the title
/// screen's "press any key", and can quit the game when done. Resumable (progress.txt); a stop.txt stops it.
/// </summary>
public sealed class QaCapture : MonoBehaviour
{
    public static string Root => Path.Combine(Paths.BepInExRootPath, "DamageInsight", "QA");
    private static string RequestFile => Path.Combine(Root, "request.json");
    private static string StopFile => Path.Combine(Root, "stop.txt");

    /// <summary>A request file asks for an unattended run: the title screen doesn't wait for a key.</summary>
    public static bool AutoStart;

    /// <summary>The boxes' "hold for details" views stay open (skills, inscriptions).</summary>
    public static bool ForceDetail;

    /// <summary>A capture is running: the scroll boxes open without their unrolling animation.</summary>
    public static bool Capturing;

    private sealed class Request
    {
        public List<string> Languages = new() { "en" };
        public List<string> Categories = new() { "items", "essences", "weapons" };
        public bool Quit;
        public string Run = "";
        public int Limit;        // per category (0 = all), for test runs
        public string Only = ""; // comma-separated gear names, for test runs
        public bool FullScreens; // also a full-screen JPG per gear (English always gets one)
        public bool Full;        // the full texts instead of the compact ones (Descriptions/Compact off)
    }

    private Request _request;
    private bool _running, _finished;
    private RectTransform _parchment; // the inventory's scroll: part of the panel the texts must stay in
    private string _statsBefore = "";
    private bool _compactBefore = true;
    private string _runDir;
    private readonly HashSet<string> _done = new();
    private StringBuilder _index;
    private int _shots, _failures;

    private void Awake()
    {
        if (!File.Exists(RequestFile))
            return;
        try
        {
            _request = ParseRequest(File.ReadAllText(RequestFile));
            AutoStart = true;
            Application.runInBackground = true; // the window is on the other monitor, without focus
            Log($"request found: {string.Join(",", _request.Languages)} / {string.Join(",", _request.Categories)}");
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[QA] request.json unreadable: {e.Message}");
        }
    }

    private void Update()
    {
        if (_running || _finished)
            return;
        bool manual = Plugin.QaCaptureOnce != null && Plugin.QaCaptureOnce.Value;
        if (!AutoStart && !manual)
            return;
        var levelManager = Singleton<Service>.Instance?.levelManager;
        if (levelManager?.player == null || Map.Instance == null || Scene<GameBase>.instance?.uiManager == null)
            return;
        _request ??= new Request();
        if (manual)
            Plugin.QaCaptureOnce.Value = false;
        StartCoroutine(Run());
    }

    // ------------------------------------------------------------------ the run

    private IEnumerator Run()
    {
        _running = true;
        var req = _request;
        _runDir = Path.Combine(Root, req.Run.Length > 0 ? req.Run : DateTime.Now.ToString("yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture));
        Directory.CreateDirectory(_runDir);
        LoadProgress();
        _index = new StringBuilder();
        Log($"start: {_runDir}");
        yield return new WaitForSecondsRealtime(3f);
        Capturing = true;
        DamageInsight.UI.MiniLog.Hidden = true;
        _compactBefore = Plugin.DescriptionsCompact.Value;
        if (req.Full)
            Plugin.DescriptionsCompact.Value = false;
        yield return Prepare();

        int languageBefore = Data.GameData.Settings.language;
        foreach (string lang in req.Languages)
        {
            if (!SetLanguage(lang))
                continue;
            yield return new WaitForSecondsRealtime(1.5f); // the mod's language follows within 0.5 s
            foreach (string category in req.Categories)
            {
                var refs = References(category, req);
                if (category != "weapons")
                    yield return HoldLittleBone();
                int n = 0;
                foreach (var reference in refs)
                {
                    if (File.Exists(StopFile))
                    {
                        Log("stop.txt found: stopping");
                        goto end;
                    }
                    string key = $"{lang}/{category}/{reference.name}";
                    n++;
                    if (_done.Contains(key))
                        continue;
                    if (n % 10 == 1)
                        Log($"{lang} {category} {n}/{refs.Count} ({_shots} pictures, {_failures} problems)");
                    yield return CaptureGear(reference, category, lang);
                    MarkDone(key);
                }
            }
        }
    end:
        SetLanguage(Loc.GameOrder.ElementAtOrDefault(languageBefore) ?? Loc.English);
        Finish();
        Log($"done: {_shots} pictures, {_failures} problems -> {_runDir}");
        File.WriteAllText(Path.Combine(_runDir, "done.txt"), $"{DateTime.Now:O}\n{_shots} pictures\n{_failures} problems\n");
        _running = false;
        _finished = true;
        if (req.Quit)
        {
            yield return new WaitForSecondsRealtime(2f);
            Application.Quit();
        }
    }

    /// <summary>Test map, invulnerable, no input, Codex paused, numbers shown, empty inventory, Little Bone.</summary>
    private IEnumerator Prepare()
    {
        var levelManager = Singleton<Service>.Instance.levelManager;
        CodexTracker.Paused = true;
        ExtraInfo.Forced = true;
        Application.runInBackground = true;
        var mapBefore = Map.Instance;
        Try("test map", DevTools.EnterTestMap);
        float until = Time.realtimeSinceStartup + 25f;
        while (Time.realtimeSinceStartup < until && (Map.Instance == mapBefore || Map.Instance == null || levelManager.player == null))
            yield return null;
        yield return new WaitForSecondsRealtime(3f);

        var player = levelManager.player;
        Try("invulnerable", () => player.invulnerable.Attach(this));
        Try("block input", () => PlayerInput.blocked.Attach(this));
        Try("empty inventory", () =>
        {
            var inventory = player.playerComponents.inventory;
            for (int i = inventory.item.items.Count - 1; i >= 0; i--)
                if (inventory.item.items[i] != null)
                    inventory.item.Discard(i);
            inventory.item.Trim();
            for (int i = inventory.quintessence.items.Count - 1; i >= 0; i--)
                if (inventory.quintessence.items[i] != null)
                    inventory.quintessence.Discard(i);
        });
        // Items and quintessences are shown with Little Bone (the skull damage some items use).
        yield return HoldLittleBone();
        _statsBefore = StatsText();
        yield return new WaitForSecondsRealtime(1f);
    }

    private void Finish()
    {
        Capturing = false;
        DamageInsight.UI.MiniLog.Hidden = false;
        Plugin.DescriptionsCompact.Value = _compactBefore;
        ExtraInfo.Forced = false;
        ForceDetail = false;
        CodexTracker.Paused = false;
        try
        {
            var player = Singleton<Service>.Instance?.levelManager?.player;
            player?.invulnerable.Detach(this);
            PlayerInput.blocked.Detach(this);
        }
        catch (Exception)
        {
            // the player may be gone
        }
        File.AppendAllText(Path.Combine(_runDir, "index.tsv"), _index.ToString(), Encoding.UTF8);
        _index.Clear();
    }

    private static List<GearReference> References(string category, Request req)
    {
        var gear = GearResource.instance;
        IEnumerable<GearReference> refs = category switch
        {
            "weapons" => gear.weapons,
            "items" => gear.items,
            "essences" => gear.essences,
            _ => Enumerable.Empty<GearReference>(),
        };
        // Test gear, reference copies and transformed bodies are not real gear.
        refs = refs.Where(r => r != null && !r.name.StartsWith("_") && !r.name.StartsWith("Ref_")
            && !r.name.Contains("Polymorph") && r.name.IndexOf("Test", StringComparison.OrdinalIgnoreCase) < 0);
        if (req.Only.Length > 0)
        {
            var only = new HashSet<string>(req.Only.Split(',').Select(s => s.Trim()));
            refs = refs.Where(r => only.Contains(r.name));
        }
        // The Olive Tree Cudgel keeps doubling physical attack after it is discarded (night 2026-10-11): last.
        var list = refs.OrderBy(r => r.name == "OliveTreeCudgel" ? 1 : 0).ThenBy(r => r.name, StringComparer.Ordinal).ToList();
        return req.Limit > 0 ? list.Take(req.Limit).ToList() : list;
    }

    // ------------------------------------------------------------------ one gear

    private IEnumerator CaptureGear(GearReference reference, string category, string lang)
    {
        var levelManager = Singleton<Service>.Instance.levelManager;
        var player = levelManager.player;
        var ui = Scene<GameBase>.instance.uiManager;
        if (player == null || ui == null)
        {
            Fail(reference, "no player");
            yield break;
        }

        GearRequest request = null;
        if (!Try("load " + reference.name, () => request = reference.LoadAsync()) || request == null)
        {
            Fail(reference, "load failed");
            yield break;
        }
        float until = Time.realtimeSinceStartup + 15f;
        while (!request.isDone && Time.realtimeSinceStartup < until)
            yield return null;
        if (!request.isDone)
        {
            Fail(reference, "load timed out");
            yield break;
        }

        Gear gear = null;
        Vector3 at = player.transform.position + new Vector3(0.8f, 0.5f, 0f);
        if (!Try("drop " + reference.name, () => gear = levelManager.DropGear(request, at)) || gear == null)
        {
            Fail(reference, "drop failed");
            yield break;
        }
        yield return new WaitForSecondsRealtime(0.8f); // it falls and lands
        var dropped = gear.dropped;
        var popup = ui.gearPopupCanvas.gearPopup;
        string display = Safe(() => gear.displayName) ?? reference.name;

        // 1. The ground popup, then its detail view (hold down).
        if (dropped != null && Try("popup", () => dropped.OpenPopupBy(player)))
        {
            yield return new WaitForSecondsRealtime(0.35f);
            yield return Shot(lang, category, reference.name, display, "popup", popup.transform as RectTransform);
            ForceDetail = true;
            yield return new WaitForSecondsRealtime(0.25f);
            var detail = popup._detailContainer != null && popup._detailContainer.activeInHierarchy ? popup._detailContainer.transform as RectTransform : null;
            // No detail view (quintessences): nothing new to show.
            if (detail != null && detail.GetComponentsInChildren<TMP_Text>(false).Any(t => t.isActiveAndEnabled && !string.IsNullOrWhiteSpace(t.text)))
                yield return Shot(lang, category, reference.name, display, "popup_detail", popup.transform as RectTransform, measure: detail);
            ForceDetail = false;
            Try("close popup", dropped.ClosePopup);
        }
        else
            Fail(reference, "no popup");

        // 2. Picked up the game's way, 3. shown in the inventory.
        var itemsBefore = player.playerComponents.inventory.item.items.ToList();
        if (dropped == null || !Try("pick up", () => dropped.InteractWith(player)))
        {
            Fail(reference, "pick up failed");
            DestroyStrayGear(null);
            yield break;
        }
        yield return new WaitForSecondsRealtime(0.6f);
        var panel = ui.inventory;
        if (Try("open inventory", panel.Open))
        {
            yield return new WaitForSecondsRealtime(0.5f);
            var element = SlotOf(panel, player, gear);
            // An item that turns into another one when picked up (Lake in a Bottle): the new item in the inventory.
            if (element == null && gear is Item)
            {
                var items = player.playerComponents.inventory.item.items;
                var changed = items.FirstOrDefault(i => i != null && !itemsBefore.Contains(i));
                if (changed != null)
                {
                    gear = changed;
                    element = SlotOf(panel, player, changed);
                }
            }
            if (element == null)
                Fail(reference, "not in the inventory after picking it up");
            else
            {
                Select(element);
                yield return new WaitForSecondsRealtime(0.25f);
                var option = panel._gearOption.transform as RectTransform;
                _parchment = panel.GetComponentInChildren<global::UI.Inventory.ScrollAnimator>(true)?._scroll;
                yield return Shot(lang, category, reference.name, display, "inventory", option, fullScreen: lang == "en" || _request.FullScreens);
                ForceDetail = true;
                yield return new WaitForSecondsRealtime(0.25f);
                yield return Shot(lang, category, reference.name, display, "inventory_detail", option);
                if (gear is Weapon weapon)
                    yield return OtherSkills(weapon, element, option, lang, category, reference.name, display);
                ForceDetail = false;
            }
            Try("close inventory", panel.Close);
            _parchment = null;
            yield return Frames(3);
        }

        // 4. Gone again: items and quintessences discarded, the skull stays until the next one replaces it.
        Try("clean up", () =>
        {
            var inventory = player.playerComponents.inventory;
            if (gear is Item item)
            {
                inventory.item.Discard(item);
                inventory.item.Trim();
            }
            else if (gear is Quintessence essence)
            {
                int index = inventory.quintessence.items.IndexOf(essence);
                if (index >= 0)
                    inventory.quintessence.Discard(index);
            }
        });
        DestroyStrayGear(gear as Weapon);
        // Safety net: nothing stays in the item / quintessence slots (an item that turned into another one, an item
        // that hands out another item: night 2026-10-11, Lake in a Bottle and Undine stayed and changed every number).
        Try("empty inventory", () =>
        {
            var inventory = player.playerComponents.inventory;
            bool left = false;
            for (int i = inventory.item.items.Count - 1; i >= 0; i--)
                if (inventory.item.items[i] != null)
                {
                    left = true;
                    inventory.item.Discard(i);
                }
            inventory.item.Trim();
            for (int i = inventory.quintessence.items.Count - 1; i >= 0; i--)
                if (inventory.quintessence.items[i] != null && category != "essences")
                {
                    left = true;
                    inventory.quintessence.Discard(i);
                }
            if (left)
                Log($"{reference.name}: something stayed in the inventory, removed");
        });
        yield return Frames(2);
        if (category != "weapons")
        {
            string stats = StatsText();
            if (stats != _statsBefore && stats.Length > 0)
            {
                Log($"{lang} {reference.name}: the stats changed for good ({_statsBefore} -> {stats})");
                _index.Append($"{lang}\t{category}\t{reference.name}\t\tstats\tSTATS {_statsBefore} -> {stats}\t0\t0\t0\t\n");
                _statsBefore = stats;
            }
        }
    }

    /// <summary>Skulls with more skills than slots: the other skills shown in the slots one after another.</summary>
    private IEnumerator OtherSkills(Weapon weapon, global::UI.Inventory.GearElement element, RectTransform option,
        string lang, string category, string name, string display)
    {
        List<SkillInfo> original = null;
        List<SkillInfo> rest = null;
        Try("skills", () =>
        {
            original = new List<SkillInfo>(weapon.currentSkills);
            rest = weapon.skills.Where(s => s != null && !original.Contains(s)).ToList();
        });
        if (original == null || rest == null || original.Count == 0)
            yield break;
        int round = 1;
        while (rest.Count > 0)
        {
            var pick = rest.Take(original.Count).ToList();
            rest = rest.Skip(original.Count).ToList();
            while (pick.Count < original.Count)
                pick.Add(original[pick.Count]);
            if (!Try("skills " + round, () => weapon.currentSkills = pick))
                break;
            Select(element);
            yield return Frames(4);
            yield return Shot(lang, category, name, display, "inventory_detail_skills" + round, option);
            round++;
        }
        Try("skills back", () => weapon.currentSkills = original);
        Select(element);
        yield return Frames(2);
    }

    /// <summary>Little Bone becomes the held skull (unless it already is).</summary>
    private IEnumerator HoldLittleBone()
    {
        var current = Singleton<Service>.Instance.levelManager.player?.playerComponents?.inventory?.weapon?.current;
        if (current != null && current.name.Replace("(Clone)", "").Trim() == "Skul")
            yield break;
        var skul = GearResource.instance.weapons.FirstOrDefault(r => r.name == "Skul");
        if (skul != null)
            yield return DropAndPickUp(skul);
        DestroyStrayGear(null);
    }

    /// <summary>The player's damage stats as text, to notice gear that changes them for good.</summary>
    private static string StatsText()
    {
        try
        {
            var s = GearDescriptions.CurrentStats(ignorePreview: true);
            return string.Join(" ", new[] { s.Physical, s.Magic, s.Projectile, s.Basic, s.Skill, s.AttackDamage, s.CritDamage }
                .Select(v => v.ToString("0.###", CultureInfo.InvariantCulture)));
        }
        catch (Exception)
        {
            return "";
        }
    }

    private IEnumerator DropAndPickUp(GearReference reference)
    {
        var levelManager = Singleton<Service>.Instance.levelManager;
        var request = reference.LoadAsync();
        while (!request.isDone)
            yield return null;
        Gear gear = null;
        Try("drop " + reference.name, () => gear = levelManager.DropGear(request, levelManager.player.transform.position));
        yield return new WaitForSecondsRealtime(0.8f);
        if (gear?.dropped != null)
            Try("pick up " + reference.name, () => gear.dropped.InteractWith(levelManager.player));
        yield return new WaitForSecondsRealtime(0.6f);
    }

    private static void Select(global::UI.Inventory.GearElement element)
    {
        Try("select", () =>
        {
            EventSystem.current?.SetSelectedGameObject(element.gameObject);
            element.onSelected?.Invoke();
        });
    }

    private static global::UI.Inventory.GearElement SlotOf(global::UI.Inventory.Panel panel, Character player, Gear gear)
    {
        var inventory = player.playerComponents.inventory;
        switch (gear)
        {
            case Weapon weapon:
                if (inventory.weapon.current == weapon || inventory.weapon.polymorphOrCurrent == weapon)
                    return panel._weapons[0];
                return inventory.weapon.next == weapon ? panel._weapons[1] : null;
            case Quintessence essence:
                int e = inventory.quintessence.items.IndexOf(essence);
                return e >= 0 && e < panel._quintessences.Length ? panel._quintessences[e] : null;
            case Item item:
                int i = inventory.item.items.IndexOf(item);
                return i >= 0 && i < panel._items.Length ? panel._items[i] : null;
        }
        return null;
    }

    /// <summary>Gear lying near the player (a replaced skull, a failed pickup) is removed, except <paramref name="keep"/>.</summary>
    private static void DestroyStrayGear(Gear keep)
    {
        Try("stray gear", () =>
        {
            var player = Singleton<Service>.Instance.levelManager.player;
            foreach (var dropped in FindObjectsOfType<DroppedGear>())
            {
                var gear = dropped.gear;
                if (gear == null || gear == keep || gear.state == Gear.State.Equipped)
                    continue;
                if (Vector3.Distance(dropped.transform.position, player.transform.position) < 20f)
                    Destroy(gear.gameObject);
            }
        });
    }

    // ------------------------------------------------------------------ pictures and facts

    private IEnumerator Shot(string lang, string category, string name, string display, string surface, RectTransform box,
        bool fullScreen = false, RectTransform measure = null)
    {
        yield return new WaitForEndOfFrame();
        Texture2D screen = null, crop = null;
        try
        {
            if (box == null || !box.gameObject.activeInHierarchy)
                throw new Exception("box not shown");
            var root = measure != null ? measure : box;
            var panelRect = PanelRect(root, _parchment);
            var texts = Texts(root, panelRect);
            screen = ScreenCapture.CaptureScreenshotAsTexture();
            // The picture: the box with its own frames and every drawn text (the parchment only counts for "outside").
            var rect = CropRect(box, PanelRect(root, null), texts.Select(t => t.component), screen.width, screen.height);
            crop = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false);
            crop.SetPixels(screen.GetPixels((int)rect.x, (int)rect.y, (int)rect.width, (int)rect.height));
            crop.Apply();

            string dir = Path.Combine(_runDir, lang, category);
            Directory.CreateDirectory(dir);
            string file = $"{FileName(name)}__{surface}";
            File.WriteAllBytes(Path.Combine(dir, file + ".png"), crop.EncodeToPNG());
            if (fullScreen)
                File.WriteAllBytes(Path.Combine(dir, file + "__full.jpg"), screen.EncodeToJPG(75));

            int overflowing = texts.Count(t => t.outside > 6 || t.overlaps.Count > 0);
            double worst = texts.Count == 0 ? 0 : texts.Max(t => t.outside);
            string missing = string.Concat(texts.SelectMany(t => t.missing).Distinct());
            File.WriteAllText(Path.Combine(dir, file + ".json"), Json(lang, category, name, display, surface, screen, rect, texts), Encoding.UTF8);
            _index.Append($"{lang}\t{category}\t{name}\t{display.Replace('\t', ' ')}\t{surface}\t{lang}/{category}/{file}.png\t{texts.Count}\t{overflowing}\t{worst.ToString("0.00", CultureInfo.InvariantCulture)}\t{missing}\n");
            _shots++;
        }
        catch (Exception e)
        {
            _failures++;
            _index.Append($"{lang}\t{category}\t{name}\t{display}\t{surface}\tERROR {e.Message.Replace('\t', ' ').Replace('\n', ' ')}\t0\t0\t0\t\n");
            Plugin.Log.LogWarning($"[QA] {lang}/{category}/{name} {surface}: {e.Message}");
        }
        finally
        {
            if (screen != null)
                Destroy(screen);
            if (crop != null)
                Destroy(crop);
        }
        if (_index.Length > 20000)
        {
            File.AppendAllText(Path.Combine(_runDir, "index.tsv"), _index.ToString(), Encoding.UTF8);
            _index.Clear();
        }
    }

    private sealed class TextFacts
    {
        public TMP_Text component;
        public string path = "", text = "", plain = "", overflowMode = "", font = "";
        public bool overflowing, autoSize;
        public int lines;
        public float preferred, height, width, rendered, fontSize;
        public double ratio;               // height the text needs / height its box gives (info: boxes may grow)
        public float outside;              // pixels the drawn text reaches beyond the measured box (overflow you can see)
        public Rect drawn;                 // the drawn text on screen
        public List<string> overlaps = new(); // other text boxes the drawn text runs into
        public List<char> missing = new();
    }

    private static readonly Regex Tag = new("<[^>]+>");

    /// <summary>How visible a graphic is: its colour's alpha times its canvas groups' alpha.</summary>
    private static float Alpha(UnityEngine.UI.Graphic g)
    {
        float a = g.color.a;
        foreach (var group in g.GetComponentsInParent<CanvasGroup>())
        {
            a *= group.alpha;
            if (group.ignoreParentGroups)
                break;
        }
        return a;
    }

    /// <summary>
    /// The visible panel on screen: every visible image under the box (backgrounds, frames), plus an extra rect (the
    /// inventory's parchment). Box objects themselves are often just anchors, so their own rect says little.
    /// </summary>
    private static Rect PanelRect(RectTransform root, RectTransform extra)
    {
        Rect? union = null;
        void Add(Rect r)
        {
            if (r.width <= 1 || r.height <= 1)
                return;
            union = union == null ? r : Rect.MinMaxRect(Math.Min(union.Value.xMin, r.xMin), Math.Min(union.Value.yMin, r.yMin),
                Math.Max(union.Value.xMax, r.xMax), Math.Max(union.Value.yMax, r.yMax));
        }
        float screenArea = Screen.width * (float)Screen.height;
        foreach (var image in root.GetComponentsInChildren<UnityEngine.UI.Image>(false))
        {
            if (!image.isActiveAndEnabled || Alpha(image) <= 0.05f)
                continue;
            var r = ScreenRect(image.rectTransform, null);
            if (r.width * r.height < 0.5f * screenArea) // a full-screen overlay is not the panel (night 2026-10-11)
                Add(r);
        }
        if (extra != null && extra.gameObject.activeInHierarchy)
            Add(ScreenRect(extra, null));
        return union ?? ScreenRect(root, null);
    }

    private static List<TextFacts> Texts(RectTransform box, Rect boxRect)
    {
        var list = new List<TextFacts>();
        foreach (var t in box.GetComponentsInChildren<TMP_Text>(false))
        {
            if (!t.isActiveAndEnabled || string.IsNullOrWhiteSpace(t.text) || Alpha(t) < 0.05f)
                continue;
            t.ForceMeshUpdate();
            var f = new TextFacts
            {
                component = t,
                path = PathOf(t.transform, box),
                text = t.text,
                plain = Tag.Replace(t.text, ""),
                overflowMode = t.overflowMode.ToString(),
                font = t.font != null ? t.font.name : "",
                overflowing = t.isTextOverflowing,
                autoSize = t.enableAutoSizing,
                lines = t.textInfo?.lineCount ?? 0,
                preferred = t.preferredHeight,
                height = t.rectTransform.rect.height,
                width = t.rectTransform.rect.width,
                rendered = t.textBounds.size.y,
                fontSize = t.fontSize,
            };
            f.ratio = f.height > 1 ? Math.Max(f.preferred, f.rendered) / f.height : 0;
            f.drawn = ScreenRect(box, t);
            if (f.drawn.width > 0)
                f.outside = Math.Max(0, Math.Max(Math.Max(boxRect.xMin - f.drawn.xMin, f.drawn.xMax - boxRect.xMax),
                    Math.Max(boxRect.yMin - f.drawn.yMin, f.drawn.yMax - boxRect.yMax)));
            if (t.font != null)
                foreach (char c in f.plain.Distinct())
                    if (!char.IsWhiteSpace(c) && !char.IsControl(c) && !t.font.HasCharacter(c, searchFallbacks: true))
                        f.missing.Add(c);
            list.Add(f);
        }
        // Texts running into each other (the King Arthur screenshot: the passive text over the skill boxes).
        foreach (var a in list)
            foreach (var b in list)
            {
                if (a == b || a.drawn.width <= 0 || b.drawn.width <= 0
                    || a.component.transform.IsChildOf(b.component.transform) || b.component.transform.IsChildOf(a.component.transform))
                    continue;
                float w = Math.Min(a.drawn.xMax, b.drawn.xMax) - Math.Max(a.drawn.xMin, b.drawn.xMin);
                float h = Math.Min(a.drawn.yMax, b.drawn.yMax) - Math.Max(a.drawn.yMin, b.drawn.yMin);
                float smaller = Math.Min(a.drawn.width * a.drawn.height, b.drawn.width * b.drawn.height);
                if (w > 0 && h > 0 && smaller > 50 && w * h > 0.15f * smaller)
                    a.overlaps.Add(b.path);
            }
        return list;
    }

    /// <summary>A box (text == null) or a text's drawn bounds, on screen in pixels.</summary>
    private static Rect ScreenRect(RectTransform box, TMP_Text text)
    {
        var canvas = box.GetComponentInParent<Canvas>()?.rootCanvas;
        var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        var points = new List<Vector3>();
        if (text == null)
        {
            var corners = new Vector3[4];
            box.GetWorldCorners(corners);
            points.AddRange(corners);
        }
        else
        {
            var b = text.textBounds;
            if (b.size == Vector3.zero)
                return Rect.zero;
            points.Add(text.transform.TransformPoint(b.min));
            points.Add(text.transform.TransformPoint(b.max));
        }
        var screen = points.Select(p => RectTransformUtility.WorldToScreenPoint(cam, p)).ToList();
        float xMin = screen.Min(p => p.x), xMax = screen.Max(p => p.x), yMin = screen.Min(p => p.y), yMax = screen.Max(p => p.y);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
    }

    /// <summary>The box and its panel on screen, grown to every text's drawn bounds (overflowing text is drawn outside).</summary>
    private static Rect CropRect(RectTransform box, Rect panel, IEnumerable<TMP_Text> texts, int screenWidth, int screenHeight)
    {
        var canvas = box.GetComponentInParent<Canvas>()?.rootCanvas;
        var cam = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
        float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
        void Add(Vector3 world)
        {
            var p = RectTransformUtility.WorldToScreenPoint(cam, world);
            xMin = Math.Min(xMin, p.x);
            yMin = Math.Min(yMin, p.y);
            xMax = Math.Max(xMax, p.x);
            yMax = Math.Max(yMax, p.y);
        }
        var corners = new Vector3[4];
        box.GetWorldCorners(corners);
        foreach (var c in corners)
            Add(c);
        xMin = Math.Min(xMin, panel.xMin);
        yMin = Math.Min(yMin, panel.yMin);
        xMax = Math.Max(xMax, panel.xMax);
        yMax = Math.Max(yMax, panel.yMax);
        foreach (var t in texts)
        {
            var b = t.textBounds;
            if (b.size == Vector3.zero)
                continue;
            Add(t.transform.TransformPoint(b.min));
            Add(t.transform.TransformPoint(b.max));
        }
        const float margin = 12f;
        float x = Mathf.Clamp(xMin - margin, 0, screenWidth - 2), y = Mathf.Clamp(yMin - margin, 0, screenHeight - 2);
        float w = Mathf.Clamp(xMax + margin, x + 2, screenWidth) - x, h = Mathf.Clamp(yMax + margin, y + 2, screenHeight) - y;
        return new Rect(Mathf.Floor(x), Mathf.Floor(y), Mathf.Floor(w), Mathf.Floor(h));
    }

    private static string Json(string lang, string category, string name, string display, string surface, Texture2D screen,
        Rect crop, List<TextFacts> texts)
    {
        var sb = new StringBuilder("{");
        sb.Append($"\"language\":{Q(lang)},\"category\":{Q(category)},\"name\":{Q(name)},\"display\":{Q(display)},\"surface\":{Q(surface)},");
        sb.Append($"\"screen\":[{screen.width},{screen.height}],\"crop\":[{N(crop.x)},{N(crop.y)},{N(crop.width)},{N(crop.height)}],");
        sb.Append($"\"mod\":{Q(MyPluginInfo.PLUGIN_VERSION)},\"time\":{Q(DateTime.Now.ToString("O"))},\"texts\":[");
        for (int i = 0; i < texts.Count; i++)
        {
            var t = texts[i];
            if (i > 0)
                sb.Append(',');
            sb.Append($"{{\"path\":{Q(t.path)},\"overflowing\":{(t.overflowing ? "true" : "false")},\"ratio\":{N(t.ratio)},\"lines\":{t.lines},");
            sb.Append($"\"outside\":{N(t.outside)},\"overlaps\":[{string.Join(",", t.overlaps.Select(Q))}],");
            sb.Append($"\"preferredHeight\":{N(t.preferred)},\"renderedHeight\":{N(t.rendered)},\"height\":{N(t.height)},\"width\":{N(t.width)},");
            sb.Append($"\"fontSize\":{N(t.fontSize)},\"autoSize\":{(t.autoSize ? "true" : "false")},\"overflowMode\":{Q(t.overflowMode)},\"font\":{Q(t.font)},");
            sb.Append($"\"missing\":{Q(new string(t.missing.ToArray()))},\"plain\":{Q(t.plain)},\"text\":{Q(t.text)}}}");
        }
        return sb.Append("]}").ToString();
    }

    // ------------------------------------------------------------------ language, progress, helpers

    /// <summary>Switches the game's language (the mod follows it); false if the game doesn't know the code.</summary>
    private static bool SetLanguage(string code)
    {
        int index = Array.IndexOf(Loc.GameOrder, code);
        if (index < 0)
            return false;
        return Try("language " + code, () =>
        {
            var table = Localization.languangeNumberToIndex.Array;
            int number = Array.IndexOf(table, index);
            if (number >= 0)
                Localization.Change(number);
            else
                Data.GameData.Settings.language = index;
        });
    }

    private void LoadProgress()
    {
        _done.Clear();
        string file = Path.Combine(_runDir, "progress.txt");
        if (File.Exists(file))
            foreach (string line in File.ReadAllLines(file))
                if (line.Length > 0)
                    _done.Add(line);
    }

    private void MarkDone(string key)
    {
        _done.Add(key);
        try
        {
            File.AppendAllText(Path.Combine(_runDir, "progress.txt"), key + "\n");
        }
        catch (Exception)
        {
            // progress is a convenience
        }
    }

    private void Fail(GearReference reference, string what)
    {
        _failures++;
        Plugin.Log.LogWarning($"[QA] {reference?.name}: {what}");
        _index?.Append($"\t\t{reference?.name}\t\t\tERROR {what}\t0\t0\t0\t\n");
    }

    private static bool Try(string what, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"[QA] {what} failed: {e.GetBaseException().Message}");
            return false;
        }
    }

    private static string Safe(Func<string> read)
    {
        try
        {
            return read();
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static IEnumerator Frames(int n)
    {
        for (int i = 0; i < n; i++)
            yield return null;
    }

    private static void Log(string text) => Plugin.Log.LogInfo("[QA] " + text);

    private static string PathOf(Transform t, Transform root)
    {
        var parts = new List<string>();
        for (var x = t; x != null && x != root; x = x.parent)
            parts.Add(x.name);
        parts.Reverse();
        return string.Join("/", parts);
    }

    private static string FileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private static string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);

    private static string Q(string s)
    {
        var sb = new StringBuilder("\"");
        foreach (char c in s ?? "")
            sb.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "",
                '\t' => "\\t",
                _ when c < 0x20 => $"\\u{(int)c:x4}",
                _ => c.ToString(),
            });
        return sb.Append('"').ToString();
    }

    private static Request ParseRequest(string json)
    {
        var d = GearDoc.ParseRaw(json) as Dictionary<string, object> ?? new Dictionary<string, object>();
        var r = new Request();
        List<string> Strings(object o) => o is List<object> l ? l.Select(x => Convert.ToString(x, CultureInfo.InvariantCulture)).ToList() : null;
        if (d.TryGetValue("languages", out var langs) && Strings(langs) is { Count: > 0 } ls)
            r.Languages = ls.Contains("all") ? Loc.GameOrder.ToList() : ls;
        if (d.TryGetValue("categories", out var cats) && Strings(cats) is { Count: > 0 } cs)
            r.Categories = cs;
        if (d.TryGetValue("quit", out var quit))
            r.Quit = quit is bool b && b;
        if (d.TryGetValue("run", out var run))
            r.Run = Convert.ToString(run, CultureInfo.InvariantCulture) ?? "";
        if (d.TryGetValue("limit", out var limit))
            r.Limit = Convert.ToInt32(limit, CultureInfo.InvariantCulture);
        if (d.TryGetValue("only", out var only))
            r.Only = Convert.ToString(only, CultureInfo.InvariantCulture) ?? "";
        if (d.TryGetValue("fullScreens", out var full))
            r.FullScreens = full is bool f && f;
        if (d.TryGetValue("full", out var fullTexts))
            r.Full = fullTexts is bool ft && ft;
        return r;
    }

    // ------------------------------------------------------------------ patches (only act while capturing)

    /// <summary>Unattended start: the title screen goes on without waiting for a key.</summary>
    [HarmonyPatch(typeof(Main), "CWaitForInput")]
    public static class SkipTitleKey
    {
        private static bool Prefix(ref IEnumerator __result)
        {
            if (!AutoStart)
                return true;
            __result = Skip();
            return false;
        }

        private static IEnumerator Skip()
        {
            GameResourceLoader.instance.WaitForStrings();
            yield return null;
            yield return Singleton<Service>.Instance.fadeInOut.CFadeOut();
        }
    }

    /// <summary>The inventory's (and popups') parchment appears fully open at once while capturing.</summary>
    [HarmonyPatch(typeof(global::UI.Inventory.ScrollAnimator), nameof(global::UI.Inventory.ScrollAnimator.Appear))]
    public static class NoScrollAnimation
    {
        private static bool Prefix(global::UI.Inventory.ScrollAnimator __instance)
        {
            if (!Capturing)
                return true;
            var scroll = __instance._scroll.sizeDelta;
            var mask = __instance._mask.sizeDelta;
            scroll.x = __instance._scrollWidth;
            mask.x = __instance._maskWidth;
            __instance._scroll.sizeDelta = scroll;
            __instance._mask.sizeDelta = mask;
            return false;
        }
    }

    [HarmonyPatch(typeof(global::UI.Inventory.WeaponOption), "Update")]
    public static class WeaponDetail
    {
        private static bool Prefix(global::UI.Inventory.WeaponOption __instance)
        {
            if (!ForceDetail)
                return true;
            __instance._simpleContainer.SetActive(false);
            __instance._detailContainer.SetActive(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(global::UI.Inventory.ItemOption), "Update")]
    public static class ItemDetail
    {
        private static bool Prefix(global::UI.Inventory.ItemOption __instance)
        {
            if (!ForceDetail)
                return true;
            __instance._simpleContainer.SetActive(false);
            __instance._detailContainer.SetActive(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(global::UI.Inventory.OliveTreeCudgelOption), "Update")]
    public static class CudgelDetail
    {
        private static bool Prefix(global::UI.Inventory.OliveTreeCudgelOption __instance)
        {
            if (!ForceDetail)
                return true;
            __instance._simpleContainer.SetActive(false);
            __instance._detailContainer.SetActive(true);
            return false;
        }
    }

    [HarmonyPatch(typeof(global::UI.GearPopup.GearPopup), "ProcessDetailView")]
    public static class PopupDetail
    {
        private static bool Prefix(global::UI.GearPopup.GearPopup __instance)
        {
            if (!ForceDetail)
                return true;
            __instance._detailContainer.SetActive(true);
            return false;
        }
    }
}

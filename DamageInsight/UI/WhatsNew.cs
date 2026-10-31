using DamageInsight.Lang;
using Services;
using Singletons;
using TMPro;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// "What's new" (Umut 2026-10-12: most players update through r2modman and never open the store page): the first time
/// a new version of the mod reaches the castle, a small window lists the highlights and their keys, once. Closed by its
/// button, Esc or Enter. Setting General/WhatsNew (on); the version last shown is kept in General/LastVersionSeen.
/// </summary>
public sealed class WhatsNew : MonoBehaviour
{
    private Canvas _canvas;
    private float _castleSince = -1f;
    private bool _shown;

    /// <summary>The window is open (the cursor stays visible: CursorPatch).</summary>
    public static bool IsOpen { get; private set; }

    /// <summary>"0.12" of "0.12.1": a hotfix of the same version doesn't show the same highlights again.</summary>
    private static string Feature(string version)
    {
        var parts = (version ?? "").Split('.');
        return parts.Length >= 2 ? parts[0] + "." + parts[1] : version ?? "";
    }

    /// <summary>
    /// The highlights of this version (English keys, translated when shown); after a version skipped (0.13 straight to
    /// 0.15: 0.14.0 was withdrawn from Thunderstore), one line names what 0.14 brought.
    /// </summary>
    private static string[] Highlights(string lastSeen)
    {
        var list = new System.Collections.Generic.List<string>
        {
            Loc.T("Restart room: press F5 twice to go back to the moment you entered the room, as when you close the game and continue. In a daily run it ends the run."),
            Loc.T("Daily points, new from November 1: the boss fights instead of the game's double-counted kills, speed counts more, flawless rooms and boss fights (no health lost) count, and four achievements of the day give bonus points. The day's board is the run alone; the streak counts on the month board."),
            Loc.T("Yesterday's daily winners give their names to the adventurers you meet."),
            Loc.T("The settings page is new: the features on the left in groups (what changes the game, what only shows, helpers, online, general), each with its switch; hover one to see its settings on the right."),
            Loc.T("Fixed: hovering with the mouse selects items, skulls and dark abilities again; the game no longer stutters when a strong build kills a lot."),
            Loc.T("The crash reports now also note frames per second and what slows the game down (setting Show FPS for a line on screen)."),
        };
        if (OlderThan(lastSeen, 0, 15))
            list.Add(Loc.T("Also new since 0.15: rewards for achievements, coins, kill quests and inscription activations (they can be turned off at the top of the settings page), six daily boards."));
        if (OlderThan(lastSeen, 0, 14))
            list.Add(Loc.T("Also new since 0.14: achievements per save slot with a small notice, Clear view, the Mods browser, healing orbs that are easier to see, macOS."));
        return list.ToArray();
    }

    private static bool OlderThan(string version, int major, int minor)
    {
        var parts = (version ?? "").Split('.');
        if (parts.Length < 2 || !int.TryParse(parts[0], out int a) || !int.TryParse(parts[1], out int b))
            return false; // never shown before: a new player needs no history
        return a < major || a == major && b < minor;
    }

    private void Update()
    {
        if (_shown || Plugin.WhatsNewEnabled == null || !Plugin.WhatsNewEnabled.Value
            || Feature(Plugin.LastVersionSeen.Value) == Feature(MyPluginInfo.PLUGIN_VERSION))
            return;
        // Only in the castle, a moment after arriving (never in the middle of a fight).
        var chapter = Singleton<Service>.Instance?.levelManager?.currentChapter;
        bool castle = Singleton<Service>.Instance?.levelManager?.player != null && chapter != null
                      && chapter.type is Level.Chapter.Type.Castle or Level.Chapter.Type.HardmodeCastle;
        if (!castle || global::UI.Dialogue.anyDialogueOpened)
        {
            _castleSince = -1f;
            return;
        }
        if (_castleSince < 0f)
            _castleSince = Time.unscaledTime;
        if (Time.unscaledTime - _castleSince < 3f)
            return;
        _shown = true;
        Patches.Guard.Run("What's new", Show);
    }

    private void Show()
    {
        _canvas = UiKit.OverlayCanvas("SkulInsight What's new", 2100);
        // Clicks need a raycaster on the canvas and an event system (the button did nothing without them: 0.12.0).
        _canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        ModEventSystem.Ensure();
        IsOpen = true;
        var panel = UiKit.Image("Panel", _canvas.transform, new Color32(0x1B, 0x12, 0x24, 0xF2));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(860, 620);
        var outline = panel.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = UiKit.ChipOn;
        outline.effectDistance = new Vector2(2, -2);

        // A new player (nothing seen before) gets the welcome with the choice instead (0.16, Umut 2026-10-30: the split,
        // option 1: what only shows information apart from what changes the game; gameplay off unless chosen).
        _welcome = string.IsNullOrEmpty(Plugin.LastVersionSeen?.Value);
        var title = UiKit.Text("Title", rect, _welcome ? Loc.F("Welcome to SkulInsight QoL {0}", MyPluginInfo.PLUGIN_VERSION)
            : Loc.F("SkulInsight QoL {0}: what's new", MyPluginInfo.PLUGIN_VERSION), 30,
            TextAlignmentOptions.Center, new Color32(0xF2, 0xD7, 0x8B, 0xFF));
        title.rectTransform.Place(30, 24, 800, 44);
        if (_welcome)
        {
            ShowWelcome(rect);
            return;
        }

        var sb = new System.Text.StringBuilder();
        foreach (string line in Highlights(Plugin.LastVersionSeen?.Value))
            sb.Append("»  ").Append(line).Append("\n<size=45%>\n</size>");
        sb.Append($"<color=#A89F94>{Loc.T("Everything else: the Changelog button in your mod manager.")}</color>");
        var body = UiKit.Text("Body", rect, sb.ToString(), 18);
        body.rectTransform.Place(40, 84, 780, 450);
        // German and Russian are about a third longer: the text shrinks to fit rather than run into the button.
        body.enableAutoSizing = true;
        body.fontSizeMin = 13;
        body.fontSizeMax = 18;

        var close = new UiKit.Chip(rect, Loc.T("Got it"), 0, 0, 160, Close, UiKit.ChipOn);
        close.SetOn(true);
        close.ClickSound = false;
        close.Background.rectTransform.anchorMin = close.Background.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        close.Background.rectTransform.pivot = new Vector2(0.5f, 0f);
        close.Background.rectTransform.anchoredPosition = new Vector2(0, 22);
        close.Background.rectTransform.sizeDelta = new Vector2(160, 36);

        var hint = UiKit.Text("Hint", rect, Loc.T("Shown once after an update (setting: What's new)."), 14,
            TextAlignmentOptions.BottomRight, new Color32(0x8A, 0x80, 0x76, 0xFF));
        hint.rectTransform.Stretch(20, 570, 20, 10);
    }

    private bool _welcome;

    /// <summary>The first start: what the mod shows, what it can change, and the choice (Esc: information only).</summary>
    private void ShowWelcome(RectTransform rect)
    {
        string text = Loc.T("This mod shows what the game keeps hidden: damage numbers with their source, real numbers in the descriptions, a combat log, the Codex, your runs and achievements, and daily runs online (off until you turn them on).") +
                      "\n\n" + Loc.T("It can also change the game a little: rewards for your achievements (small bonuses, coins, kill quests) and Restart room (press F5 twice to go back to the start of the room). Do you want those too?") +
                      $"\n\n<color=#A89F94>{Loc.T("You can change this at any time: Esc, SkulInsight QoL, the group Gameplay at the top.")}</color>";
        var body = UiKit.Text("Body", rect, text, 20);
        body.rectTransform.Place(40, 90, 780, 400);
        body.enableAutoSizing = true;
        body.fontSizeMin = 14;
        body.fontSizeMax = 20;

        Choice(Loc.T("Only information"), false, -190f);
        Choice(Loc.T("Information and gameplay"), true, 190f);

        void Choice(string label, bool gameplay, float x)
        {
            var chip = new UiKit.Chip(rect, label, 0, 0, 340, () => Choose(gameplay), UiKit.ChipOn);
            chip.SetOn(!gameplay); // information only is the default (Esc)
            chip.ClickSound = false;
            var r = chip.Background.rectTransform;
            r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
            r.pivot = new Vector2(0.5f, 0f);
            r.anchoredPosition = new Vector2(x, 26);
            r.sizeDelta = new Vector2(340, 40);
        }
    }

    /// <summary>The welcome's choice: the gameplay features (rewards, restart room) on or off.</summary>
    private void Choose(bool gameplay)
    {
        Patches.Guard.Run("Welcome (choice)", () =>
        {
            if (Plugin.RewardsEnabled != null)
                Plugin.RewardsEnabled.Value = gameplay;
            if (Plugin.RoomRestartEnabled != null)
                Plugin.RoomRestartEnabled.Value = gameplay;
            Plugin.Log.LogInfo($"Welcome: {(gameplay ? "information and gameplay" : "information only")}");
        });
        Close();
    }

    private void LateUpdate()
    {
        if (_canvas == null)
            return;
        if (_welcome && Input.GetKeyDown(KeyCode.Escape))
            Choose(false);
        else if (!_welcome && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return)))
            Close();
    }

    private void Close()
    {
        MenuSounds.Play(MenuSounds.Kind.Close);
        Plugin.LastVersionSeen.Value = MyPluginInfo.PLUGIN_VERSION;
        if (_canvas != null)
            Destroy(_canvas.gameObject);
        _canvas = null;
        IsOpen = false;
    }
}

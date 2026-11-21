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

    private static WhatsNew _instance;
    /// <summary>Opened from the settings page (General, Changelog): not the once-after-an-update window.</summary>
    private bool _fromSettings;
    private RectTransform _content, _viewport;
    private bool _navigation = true; // the event system's navigation before the window took the arrow keys

    private void Awake() => _instance = this;

    /// <summary>The changelog from the settings page (General, Changelog): every version, the newest in the box.</summary>
    public static void OpenChangelog()
    {
        if (_instance == null || _instance._canvas != null)
            return;
        _instance._fromSettings = true;
        Patches.Guard.Run("Changelog", _instance.Show);
    }

    /// <summary>Esc on the settings page while the window is open: the window closes first.</summary>
    public static bool CloseIfOpen()
    {
        if (_instance == null || _instance._canvas == null)
            return false;
        _instance.Close();
        return true;
    }

    private void Update()
    {
        // After every update, a hotfix too (Umut 2026-11-01: "Our whats new should open up once the mod updated").
        if (_shown || _canvas != null || Plugin.WhatsNewEnabled == null || !Plugin.WhatsNewEnabled.Value
            || Plugin.LastVersionSeen.Value == MyPluginInfo.PLUGIN_VERSION)
            return;
        // Co-op test copies (tools/coop/start-instances.ps1) run unattended and never save the setting: it would show
        // at every start and cover the middle of the screen.
        if (DamageInsight.Coop.Testing.CoopLaunch.Current.FromLauncher)
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
        _welcome = !_fromSettings && string.IsNullOrEmpty(Plugin.LastVersionSeen?.Value);
        var title = UiKit.Text("Title", rect, _welcome ? Loc.F("Welcome to SkulInsight QoL {0}", MyPluginInfo.PLUGIN_VERSION)
            : _fromSettings ? $"SkulInsight QoL {MyPluginInfo.PLUGIN_VERSION}  ·  {Loc.T("Changelog")}"
            : Loc.F("SkulInsight QoL {0}: what's new", MyPluginInfo.PLUGIN_VERSION), 30,
            TextAlignmentOptions.Center, new Color32(0xF2, 0xD7, 0x8B, 0xFF));
        title.rectTransform.Place(30, 24, 800, 44);
        if (_welcome)
        {
            ShowWelcome(rect);
            return;
        }

        // Every version (Umut 2026-11-01: "should include ALL changelogs"), what is new for the player in a box at the
        // top ("boxed in and highlighted, cause only that really matters"), the earlier ones below; it scrolls.
        var (boxed, earlier) = Changelog.Split(_fromSettings ? MyPluginInfo.PLUGIN_VERSION : Plugin.LastVersionSeen?.Value);
        _viewport = UiKit.Rect("Changelog", rect);
        _viewport.Place(30, 80, 790, 462);
        _viewport.gameObject.AddComponent<UnityEngine.UI.Image>().color = Color.clear; // catches the mouse wheel
        _viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
        _content = UiKit.Rect("Versions", _viewport);
        _content.anchorMin = new Vector2(0f, 1f);
        _content.anchorMax = new Vector2(1f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;

        const float width = 770f, pad = 16f;
        var box = UiKit.Image("New", _content, new Color32(0x33, 0x25, 0x40, 0xFF));
        var boxLine = box.gameObject.AddComponent<UnityEngine.UI.Outline>();
        boxLine.effectColor = UiKit.ChipOn;
        boxLine.effectDistance = new Vector2(2, -2);
        var boxText = UiKit.Text("New text", box.rectTransform, Changelog.Text(boxed), 18);
        float boxHeight = boxText.GetPreferredValues(boxText.text, width - 2 * pad, 0f).y + 2 * pad;
        box.rectTransform.Place(2, 2, width - 4, boxHeight);
        boxText.rectTransform.Place(pad, pad, width - 2 * pad, boxHeight - 2 * pad);
        float y = boxHeight + 22f;
        if (earlier.Count > 0)
        {
            var heading = UiKit.Text("Earlier", _content, Loc.T("Earlier versions"), 16, TextAlignmentOptions.TopLeft,
                new Color32(0xA8, 0x9F, 0x94, 0xFF));
            heading.rectTransform.Place(pad, y, width - 2 * pad, 24);
            y += 30f;
            var rest = UiKit.Text("Earlier text", _content, Changelog.Text(earlier), 16, TextAlignmentOptions.TopLeft,
                new Color32(0xC9, 0xC0, 0xB5, 0xFF));
            float restHeight = rest.GetPreferredValues(rest.text, width - 2 * pad, 0f).y;
            rest.rectTransform.Place(pad, y, width - 2 * pad, restHeight);
            y += restHeight + 12f;
        }
        _content.sizeDelta = new Vector2(0f, y);

        var scroll = _viewport.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
        scroll.content = _content;
        scroll.viewport = _viewport;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
        scroll.inertia = false;
        scroll.scrollSensitivity = 40f;
        var bar = UiKit.Image("Scroll bar", rect, new Color32(0x3A, 0x2E, 0x46, 0xFF));
        bar.rectTransform.Place(826, 80, 6, 462);
        var handle = UiKit.Image("Handle", bar.rectTransform, UiKit.ChipOn);
        handle.rectTransform.Fill();
        var scrollbar = bar.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
        scrollbar.handleRect = handle.rectTransform;
        scrollbar.targetGraphic = handle;
        scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
        scrollbar.transition = UnityEngine.UI.Selectable.Transition.None;
        scrollbar.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        scroll.verticalScrollbar = scrollbar;
        scroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHide;
        // The arrow keys scroll this window (LateUpdate), not the menu behind it.
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events != null)
        {
            _navigation = events.sendNavigationEvents;
            events.sendNavigationEvents = false;
        }

        var close = new UiKit.Chip(rect, Loc.T("Got it"), 0, 0, 160, Close, UiKit.ChipOn);
        close.SetOn(true);
        close.ClickSound = false;
        close.Background.rectTransform.anchorMin = close.Background.rectTransform.anchorMax = new Vector2(0.5f, 0f);
        close.Background.rectTransform.pivot = new Vector2(0.5f, 0f);
        close.Background.rectTransform.anchoredPosition = new Vector2(0, 22);
        close.Background.rectTransform.sizeDelta = new Vector2(160, 36);

        if (!_fromSettings)
        {
            var hint = UiKit.Text("Hint", rect, Loc.T("Shown once after an update (setting: What's new)."), 14,
                TextAlignmentOptions.BottomRight, new Color32(0x8A, 0x80, 0x76, 0xFF));
            hint.rectTransform.Stretch(20, 570, 20, 10);
        }
    }

    /// <summary>The arrow keys, Page Up / Down, Home and End scroll the changelog.</summary>
    private void ScrollKeys()
    {
        if (_content == null || _viewport == null)
            return;
        float view = _viewport.rect.height, most = Mathf.Max(0f, _content.rect.height - view), step = 0f;
        if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S))
            step = 60f;
        else if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W))
            step = -60f;
        else if (Input.GetKeyDown(KeyCode.PageDown))
            step = view * 0.9f;
        else if (Input.GetKeyDown(KeyCode.PageUp))
            step = -view * 0.9f;
        else if (Input.GetKeyDown(KeyCode.Home))
            step = -most;
        else if (Input.GetKeyDown(KeyCode.End))
            step = most;
        if (step != 0f)
            _content.anchoredPosition = new Vector2(_content.anchoredPosition.x, Mathf.Clamp(_content.anchoredPosition.y + step, 0f, most));
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
        else
            ScrollKeys();
    }

    private void Close()
    {
        MenuSounds.Play(MenuSounds.Kind.Close);
        // From the settings page it was looked at, not shown after an update (a new player's welcome still comes).
        if (!_fromSettings)
            Plugin.LastVersionSeen.Value = MyPluginInfo.PLUGIN_VERSION;
        var events = UnityEngine.EventSystems.EventSystem.current;
        if (events != null && _content != null)
            events.sendNavigationEvents = _navigation;
        if (_canvas != null)
            Destroy(_canvas.gameObject);
        _canvas = null;
        _content = _viewport = null;
        _fromSettings = false;
        IsOpen = false;
    }
}

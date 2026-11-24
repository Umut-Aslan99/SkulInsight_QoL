using DamageInsight.Coop;
using DamageInsight.Lang;
using Services;
using Singletons;
using TMPro;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// The warning about another co-op mod or a co-op part that didn't load (<see cref="CoopConflicts"/>; Umut
/// 2026-11-24): at every game start in the castle (co-op switched on), every time the Co-op window opens, and when a
/// host / join / search was refused because of it. It names what was found and how to remove it; hosting and joining
/// stay blocked until then.
/// </summary>
public sealed class CoopConflictNotice : MonoBehaviour
{
    /// <summary>The window is open (the cursor stays visible: CursorPatch).</summary>
    public static bool IsOpen { get; private set; }

    private static CoopConflictNotice _instance;
    private Canvas _canvas;
    private bool _startShown;
    private float _castleSince = -1f;

    private void Awake() => _instance = this;

    // After every plugin's Awake: BepInEx lists a plugin only once it loaded it.
    private void Start() => Patches.Guard.Run("Other co-op mods (scan)", CoopConflicts.Scan);

    /// <summary>Shows the warning now when co-op is blocked on this game (the Co-op window opened, a start refused).</summary>
    public static void ShowNow()
    {
        if (_instance == null || _instance._canvas != null || CoopConflicts.Blocker == null)
            return;
        Patches.Guard.Run("Co-op conflict notice", _instance.Show);
    }

    private void Update()
    {
        if (_startShown || _canvas != null || Plugin.CoopEnabled?.Value != true || CoopConflicts.Blocker == null
            || Coop.Testing.CoopLaunch.Current.FromLauncher)
            return;
        // In the castle, a moment after arriving, after the What's new and Update windows (never in a fight).
        var chapter = Singleton<Service>.Instance?.levelManager?.currentChapter;
        bool castle = Singleton<Service>.Instance?.levelManager?.player != null && chapter != null
                      && chapter.type is Level.Chapter.Type.Castle or Level.Chapter.Type.HardmodeCastle;
        if (!castle || global::UI.Dialogue.anyDialogueOpened || WhatsNew.IsOpen || UpdateNotice.IsOpen || SettingsPage.IsOpen)
        {
            _castleSince = -1f;
            return;
        }
        if (_castleSince < 0f)
            _castleSince = Time.unscaledTime;
        if (Time.unscaledTime - _castleSince < 5f)
            return;
        _startShown = true;
        Patches.Guard.Run("Co-op conflict notice", Show);
    }

    private void Show()
    {
        bool otherMods = CoopConflicts.Found.Count > 0;
        _canvas = UiKit.OverlayCanvas("SkulInsight Co-op conflict notice", 2300); // above the Co-op window
        _canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        ModEventSystem.Ensure();
        IsOpen = true;
        var panel = UiKit.Image("Panel", _canvas.transform, new Color32(0x24, 0x12, 0x14, 0xF4));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(820, 360);
        var outline = panel.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color32(0xE0, 0x5A, 0x4A, 0xFF);
        outline.effectDistance = new Vector2(2, -2);

        string heading = otherMods ? Loc.T("Another co-op mod is installed") : Loc.T("Co-op is not installed completely");
        var title = UiKit.Text("Title", rect, heading, 30, TextAlignmentOptions.Center, new Color32(0xF2, 0x9B, 0x8B, 0xFF));
        title.rectTransform.Place(30, 22, 760, 44);
        string text = otherMods
            ? Loc.F("Found: {0}. It changes the same parts of the game as SkulInsight's co-op, so enemies would only go for one player. Turn it off or uninstall it (in your mod manager, or delete its files from BepInEx/plugins and BepInEx/patchers), then restart Skul.",
                string.Join(", ", CoopConflicts.Found))
            : Loc.T("SkulInsight's co-op part (BepInEx/patchers/SkulInsight/DamageInsight.Preloader.dll) didn't load or doesn't fit this version. Reinstall or update SkulInsight QoL with your mod manager (or copy its patchers folder into BepInEx/patchers), then restart Skul.");
        text += $"\n\n<color=#E0B0A0>{Loc.T("Until then you can't host, join or search for a co-op game.")}</color>";
        var body = UiKit.Text("Body", rect, text, 20, TextAlignmentOptions.Top);
        body.rectTransform.Place(40, 80, 740, 200);
        body.enableAutoSizing = true;
        body.fontSizeMin = 14;
        body.fontSizeMax = 20;

        var close = new UiKit.Chip(rect, Loc.T("Got it"), 0, 0, 160, Close, UiKit.ChipOn);
        close.SetOn(true);
        close.ClickSound = false;
        var r = close.Background.rectTransform;
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0f);
        r.pivot = new Vector2(0.5f, 0f);
        r.anchoredPosition = new Vector2(0, 22);
        r.sizeDelta = new Vector2(160, 36);
        Plugin.Log.LogInfo("[Coop] conflict notice shown: " + CoopConflicts.Summary);
    }

    private void LateUpdate()
    {
        if (_canvas != null && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return)))
            Close();
    }

    private void Close()
    {
        MenuSounds.Play(MenuSounds.Kind.Close);
        if (_canvas != null)
            Destroy(_canvas.gameObject);
        _canvas = null;
        IsOpen = false;
    }
}

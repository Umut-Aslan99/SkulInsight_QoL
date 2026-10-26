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
            Loc.T("Rewards: every achievement of your save slot gives a small lasting bonus that fits it, like more damage to a boss you beat (more without getting hit), attack, speed or gold (Codex, You, Rewards)."),
            Loc.T("Coins: break a legendary item or skull, or beat an adventurer a few times, and its coin summons that gear in a later run. A run ended with Omen items gives Omen coins."),
            Loc.T("Kill quests: every normal enemy has an endless quest that pays dark quartz and gold."),
            Loc.T("Inscription activations: complete an inscription in a run, and in a later run one you have counts one more, like the Dark Mirror's banners."),
            Loc.T("Rewards can be turned off at the top of the settings page; daily runs never use them."),
            Loc.T("Daily runs: every day Normal, the Dark Mirror at a random level and Dark Mirror 10, each with and without the DLC and each with its own board. Everyone gets the same gifts in the castle and the same blessing."),
        };
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
        if (UnityEngine.EventSystems.EventSystem.current == null)
        {
            var events = new GameObject("DamageInsight_EventSystem");
            DontDestroyOnLoad(events);
            events.AddComponent<UnityEngine.EventSystems.EventSystem>();
            events.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();
        }
        IsOpen = true;
        var panel = UiKit.Image("Panel", _canvas.transform, new Color32(0x1B, 0x12, 0x24, 0xF2));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(860, 620);
        var outline = panel.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = UiKit.ChipOn;
        outline.effectDistance = new Vector2(2, -2);

        var title = UiKit.Text("Title", rect, Loc.F("SkulInsight QoL {0}: what's new", MyPluginInfo.PLUGIN_VERSION), 30,
            TextAlignmentOptions.Center, new Color32(0xF2, 0xD7, 0x8B, 0xFF));
        title.rectTransform.Place(30, 24, 800, 44);

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

    private void LateUpdate()
    {
        if (_canvas != null && (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return)))
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

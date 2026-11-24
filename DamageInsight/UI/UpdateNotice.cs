using System;
using System.Collections;
using DamageInsight.Describe;
using DamageInsight.Lang;
using DamageInsight.Mods;
using Services;
using Singletons;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;

namespace DamageInsight.UI;

/// <summary>
/// "Update available" (Umut 2026-11-23: "a notification at the beginning where the changelog shows that says something
/// along the lines 'update available ... your version is outdated ... Please update SkulInsight QoL' ... in all
/// languages"; "should also show, even if the person already saw and clicked the changelog window away"). Once per game
/// start it asks the mod's own Thunderstore package for its newest version number (nothing else: no download, no
/// data about the player); when that is newer than the running one, a small window says so in the castle, at every
/// start, after the What's new window if that one shows too. Co-op and matchmaking pair only equal versions, so an old
/// version also finds nobody. Setting General/UpdateNotice (on). Builds with the self-update (not Thunderstore's) show
/// it only when General/AutoUpdate is off: otherwise the update comes by itself.
/// </summary>
public sealed class UpdateNotice : MonoBehaviour
{
    public const string ApiUrl = "https://thunderstore.io/api/experimental/package/DocRun/SkulInsight_QoL/";

    /// <summary>The newest version on Thunderstore, or null (not asked, no answer).</summary>
    public static string Latest { get; private set; }

    /// <summary>Thunderstore has a newer version than the running one.</summary>
    public static bool Outdated => Latest != null && ThunderstoreCatalog.IsNewer(Latest, MyPluginInfo.PLUGIN_VERSION);

    /// <summary>The window is open (the cursor stays visible: CursorPatch).</summary>
    public static bool IsOpen { get; private set; }

    private Canvas _canvas;
    private bool _shown;
    private float _castleSince = -1f;
    private static UpdateNotice _instance;

    private void Awake() => _instance = this;

    /// <summary>The launcher's window test (CoopWindowTest): the window as if <paramref name="latest"/> were out.</summary>
    internal static void TestShow(string latest)
    {
        if (_instance == null || _instance._canvas != null)
            return;
        Latest = latest;
        _instance._shown = true;
        Patches.Guard.Run("Update notice (test)", _instance.Show);
    }

    /// <summary>The newest version number from the package API's answer, or null. Plain logic, tested.</summary>
    public static string LatestIn(string json)
    {
        try
        {
            string version = GearDoc.ParseNode(json).Child("latest").Str("version_number") ?? "";
            return System.Version.TryParse(version, out _) ? version : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void Start()
    {
        // Co-op test copies (the launcher) run unattended: no window over the middle of their screen.
        if (Plugin.UpdateNoticeEnabled?.Value == false || Coop.Testing.CoopLaunch.Current.FromLauncher)
            return;
        StartCoroutine(Ask());
    }

    private static IEnumerator Ask()
    {
        using var ask = UnityWebRequest.Get(ApiUrl);
        ask.timeout = 20;
        ask.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        yield return ask.SendWebRequest();
        Latest = ask.result == UnityWebRequest.Result.Success ? LatestIn(ask.downloadHandler.text) : null;
        Plugin.Log.LogInfo(Latest == null ? $"Update notice: Thunderstore didn't answer ({ask.error})"
            : Outdated ? $"Update notice: {Latest} is out, this is {MyPluginInfo.PLUGIN_VERSION}"
            : $"Update notice: {MyPluginInfo.PLUGIN_VERSION} is up to date (Thunderstore: {Latest})");
    }

    private void Update()
    {
        if (_shown || _canvas != null || !Outdated || Plugin.UpdateNoticeEnabled?.Value == false)
            return;
#if !THUNDERSTORE
        if (Plugin.AutoUpdate?.Value != false)
            return; // the self-update brings it (ModService)
#endif
        // In the castle, a moment after arriving, and after the What's new window (never in a fight or a dialogue).
        var chapter = Singleton<Service>.Instance?.levelManager?.currentChapter;
        bool castle = Singleton<Service>.Instance?.levelManager?.player != null && chapter != null
                      && chapter.type is Level.Chapter.Type.Castle or Level.Chapter.Type.HardmodeCastle;
        if (!castle || global::UI.Dialogue.anyDialogueOpened || WhatsNew.IsOpen || SettingsPage.IsOpen || CoopWindow.IsOpen)
        {
            _castleSince = -1f;
            return;
        }
        if (_castleSince < 0f)
            _castleSince = Time.unscaledTime;
        if (Time.unscaledTime - _castleSince < 4f)
            return;
        _shown = true;
        Patches.Guard.Run("Update notice", Show);
    }

    private void Show()
    {
        _canvas = UiKit.OverlayCanvas("SkulInsight Update notice", 2200); // above the mod's other windows
        _canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        ModEventSystem.Ensure();
        IsOpen = true;
        var panel = UiKit.Image("Panel", _canvas.transform, new Color32(0x1B, 0x12, 0x24, 0xF2));
        var rect = panel.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(760, 300);
        var outline = panel.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = new Color32(0xF2, 0xA9, 0x3B, 0xFF);
        outline.effectDistance = new Vector2(2, -2);

        var title = UiKit.Text("Title", rect, Loc.T("Update available"), 30, TextAlignmentOptions.Center,
            new Color32(0xF2, 0xD7, 0x8B, 0xFF));
        title.rectTransform.Place(30, 22, 700, 44);
        string text = Loc.F("SkulInsight QoL {0} is out. Your version is outdated: {1}. Please update SkulInsight QoL (r2modman or Thunderstore).",
                          Latest, MyPluginInfo.PLUGIN_VERSION) +
                      $"\n\n<color=#A89F94>{Loc.T("Co-op and matchmaking only bring players with the same version together.")}</color>";
        var body = UiKit.Text("Body", rect, text, 20, TextAlignmentOptions.Top);
        body.rectTransform.Place(40, 80, 680, 140);
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
        Plugin.Log.LogInfo($"Update notice shown: {Latest} is out, this is {MyPluginInfo.PLUGIN_VERSION}");
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

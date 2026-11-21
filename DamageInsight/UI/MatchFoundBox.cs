using System.Linq;
using DamageInsight.Coop;
using DamageInsight.Coop.Match;
using DamageInsight.Lang;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// The "Match found" box (package MM): over the game, also with the Co-op window closed, while a matchmaking proposal
/// waits for this player: the partner, the estimated ping, the mode, Accept / Decline and the 15 s countdown. After
/// Accept it says it waits for the partner. Lives on the plugin object next to the Co-op window.
/// </summary>
public sealed class MatchFoundBox : MonoBehaviour
{
    private const float Width = 600f, Height = 236f;

    private static readonly Color Back = new Color32(0x14, 0x11, 0x27, 0xF7);
    private static readonly Color Frame = new Color32(0x3D, 0xDC, 0x97, 0xFF);
    private static readonly Color Ink = new Color32(0xEC, 0xEA, 0xF7, 0xFF);
    private static readonly Color Dim = new Color32(0x9C, 0x97, 0xC4, 0xFF);
    private static readonly Color Green = new Color32(0x3D, 0xDC, 0x97, 0xFF);
    private static readonly Color Red = new Color32(0xE5, 0x62, 0x6A, 0xFF);
    private static readonly Color Amber = new Color32(0xFF, 0xC8, 0x5A, 0xFF);

    private Canvas _canvas;
    private TextMeshProUGUI _partner, _details, _countdown;
    private UiKit.Chip _accept, _decline;
    private int _language = -1;
    private float _nextDraw;
    private MatchState _shownState;

    private void Update()
    {
        Patches.Guard.Run("Match found box", () =>
        {
            var client = CoopRuntime.Instance?.Matchmaking?.Client;
            bool show = client != null && client.State is MatchState.Proposed or MatchState.Accepted && client.Match != null;
            if (!show)
            {
                if (_canvas != null && _canvas.gameObject.activeSelf)
                    _canvas.gameObject.SetActive(false);
                return;
            }
            if (_canvas != null && _language != Loc.Version)
            {
                Destroy(_canvas.gameObject);
                _canvas = null;
            }
            if (_canvas == null)
                Build();
            if (!_canvas.gameObject.activeSelf)
            {
                _canvas.gameObject.SetActive(true);
                MenuSounds.Play(MenuSounds.Kind.Select);
                _nextDraw = 0f;
            }
            if (Time.unscaledTime >= _nextDraw || _shownState != client.State)
            {
                _nextDraw = Time.unscaledTime + 0.2f;
                Draw(client);
            }
            // Without the mouse (MM.md Left 4): Enter or a controller's first face button accepts, Backspace or its
            // second declines. Matches are found in the castle, where a stray jump does no harm.
            if (client.State == MatchState.Proposed && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Pad(1)))
                OnAccept();
            else if (Input.GetKeyDown(KeyCode.Backspace) || Pad(2))
                OnDecline();
        });
    }

    private static bool Pad(int button)
    {
        try
        {
            var device = InControl.InputManager.ActiveDevice;
            return device != null && (button == 1 ? device.Action1.WasPressed : device.Action2.WasPressed);
        }
        catch (System.Exception)
        {
            return false; // no InControl yet (start of the game)
        }
    }

    private void Build()
    {
        _language = Loc.Version;
        _canvas = UiKit.OverlayCanvas("DamageInsight_MatchFound", 2160);
        _canvas.gameObject.AddComponent<GraphicRaycaster>();
        ModEventSystem.Ensure();
        var box = UiKit.Image("Box", _canvas.transform, Back);
        var rect = box.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0, -90);
        rect.sizeDelta = new Vector2(Width, Height);
        var outline = box.gameObject.AddComponent<Outline>();
        outline.effectColor = Frame;
        outline.effectDistance = new Vector2(2.5f, -2.5f);

        Text(rect, Loc.T("Match found"), 30, Green).rectTransform.Place(24, 14, Width - 48, 40);
        _partner = Text(rect, "", 22, Ink);
        _partner.richText = false; // a name is shown as typed
        _partner.rectTransform.Place(24, 60, Width - 48, 30);
        _details = Text(rect, "", 17, Dim);
        _details.rectTransform.Place(24, 94, Width - 48, 26);
        _countdown = Text(rect, "", 17, Amber);
        _countdown.rectTransform.Place(24, 124, Width - 48, 26);

        string accept = Loc.T("Accept"), decline = Loc.T("Decline");
        float aw = UiKit.ChipWidth(accept) + 40, dw = UiKit.ChipWidth(decline) + 40;
        _accept = new UiKit.Chip(rect, accept, 24, Height - 64, aw, OnAccept, Green);
        _accept.Background.rectTransform.sizeDelta = new Vector2(aw, 44);
        _decline = new UiKit.Chip(rect, decline, 24 + aw + 12, Height - 64, dw, OnDecline, Red);
        _decline.Background.rectTransform.sizeDelta = new Vector2(dw, 44);
        _canvas.gameObject.SetActive(false);
    }

    private void Draw(MatchClient client)
    {
        _shownState = client.State;
        var match = client.Match;
        string names = string.Join(", ", match.Partners.Select(p => p.Name));
        _partner.text = Loc.F("Partner: {0}", names);
        int ping = client.CheckedPing >= 0 ? client.CheckedPing : match.EstimatedPing;
        string mode = ModeText(match.Mode, match.Level);
        _details.text = ping >= 0 ? Loc.F("Estimated ping: {0} ms", ping) + "   ·   " + mode : mode;
        bool waiting = client.State == MatchState.Accepted;
        _countdown.text = waiting ? Loc.T("Waiting for your partner...") : Loc.F("Accept within {0} s.", client.ProposalSecondsLeft);
        _accept.Background.gameObject.SetActive(!waiting);
        _accept.SetOn(!waiting);
        _decline.SetOn(true);
    }

    internal static string ModeText(string mode, int level) => mode switch
    {
        "darkmirror" => level >= 0 ? Loc.F("Dark Mirror {0}", level) : Loc.T("Dark Mirror"),
        "any" => Loc.T("Any"),
        _ => Loc.T("Normal"),
    };

    private void OnAccept()
    {
        CoopRuntime.Instance?.Matchmaking?.Client.Accept();
        _nextDraw = 0f;
    }

    private void OnDecline()
    {
        CoopRuntime.Instance?.Matchmaking?.Client.Decline();
        _nextDraw = 0f;
    }

    private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color) =>
        UiKit.Text("Text", parent, text, size, TextAlignmentOptions.TopLeft, color);

    private void OnDestroy()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
    }
}

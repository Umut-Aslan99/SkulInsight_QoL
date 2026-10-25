using System.Collections.Generic;
using DamageInsight.Achievements;
using DamageInsight.Lang;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// The achievement notice (0.14, Umut: "a small box that is not too intrusive but comes down from top of the screen,
/// displays an achievement the player got and fades away or leaves from the top of the screen again"): icon, name and
/// what it was for slide down from the top edge, stay a few seconds and slide back up, fading. Several in a row wait
/// their turn. Runs on unscaled time (the game may be paused). Setting Codex/AchievementNotice.
/// </summary>
public sealed class AchievementToast : MonoBehaviour
{
    private const float Height = 92f, MinWidth = 440f, MaxWidth = 760f, Top = 18f;
    private const float InSeconds = 0.4f, StaySeconds = 4.5f, OutSeconds = 0.45f;

    private static AchievementToast _instance;
    private readonly Queue<AchievementInfo> _waiting = new();
    private AchievementInfo _shown;
    private float _time;

    private Canvas _canvas;
    private CanvasGroup _group;
    private RectTransform _box;
    private Image _icon;
    private TextMeshProUGUI _caption, _name, _description;

    public static void Show(AchievementInfo info)
    {
        if (info == null)
            return;
        Patches.Guard.Run("Achievement notice", () =>
        {
            if (_instance == null)
            {
                var go = new GameObject("DamageInsight_AchievementToast");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<AchievementToast>();
            }
            _instance._waiting.Enqueue(info);
        });
    }

    private void Update()
    {
        if (_shown == null)
        {
            if (_waiting.Count == 0)
                return;
            _shown = _waiting.Dequeue();
            _time = 0f;
            Patches.Guard.Run("Achievement notice", Fill);
        }
        _time += Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        Patches.Guard.Run("Achievement notice", Animate);
    }

    private void Fill()
    {
        if (_canvas == null)
            Build();
        _canvas.enabled = true;
        _caption.text = Loc.T("Achievement unlocked");
        _name.text = $"<b>{_shown.Name}</b>";
        _description.text = _shown.Description;
        float text = Mathf.Max(_name.GetPreferredValues(_name.text).x, _description.GetPreferredValues(_description.text).x);
        _box.sizeDelta = new Vector2(Mathf.Clamp(text + 128f, MinWidth, MaxWidth), Height);
        SetIcon();
        AchievementSound.Play();
    }

    private void SetIcon()
    {
        var sprite = _shown.Kind == AchievementKind.Daily ? PixelIcons.Get(_shown.Pixel)
            : AchievementSteam.Icon(_shown.OnSteam ? _shown.Id : _shown.BaseId, grey: false);
        _icon.sprite = sprite;
        _icon.color = sprite == null ? new Color(0, 0, 0, 0) : Color.white;
    }

    /// <summary>Slides in (eased out), stays, slides out (eased in) while it fades; then the next one.</summary>
    private void Animate()
    {
        if (_icon.sprite == null)
            SetIcon(); // Steam may hand the icon over a moment later
        float hidden = Height + 12f, shown = -Top;
        float y, alpha;
        if (_time < InSeconds)
        {
            float t = 1f - Mathf.Pow(1f - _time / InSeconds, 3f);
            y = Mathf.Lerp(hidden, shown, t);
            alpha = t;
        }
        else if (_time < InSeconds + StaySeconds)
        {
            y = shown;
            alpha = 1f;
        }
        else if (_time < InSeconds + StaySeconds + OutSeconds)
        {
            float t = Mathf.Pow((_time - InSeconds - StaySeconds) / OutSeconds, 2f);
            y = Mathf.Lerp(shown, hidden, t);
            alpha = 1f - t;
        }
        else
        {
            _shown = null;
            _canvas.enabled = _waiting.Count > 0;
            return;
        }
        _box.anchoredPosition = new Vector2(0f, y);
        _group.alpha = alpha;
    }

    private void Build()
    {
        _canvas = UiKit.OverlayCanvas("DamageInsight_AchievementToast", 5300);
        var background = UiKit.Image("Box", _canvas.transform, new Color32(0x1B, 0x12, 0x24, 0xF0));
        _box = background.rectTransform;
        _box.anchorMin = _box.anchorMax = _box.pivot = new Vector2(0.5f, 1f);
        _box.sizeDelta = new Vector2(MinWidth, Height);
        var outline = background.gameObject.AddComponent<Outline>();
        outline.effectColor = new Color32(0xE8, 0xB8, 0x4A, 0xFF);
        outline.effectDistance = new Vector2(2, -2);
        background.raycastTarget = false;
        _group = background.gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;

        var frame = UiKit.Image("Icon frame", _box, new Color32(0x0E, 0x09, 0x14, 0xFF));
        frame.rectTransform.Place(12, 10, 72, 72);
        frame.raycastTarget = false;
        var frameOutline = frame.gameObject.AddComponent<Outline>();
        frameOutline.effectColor = new Color32(0xA8, 0x74, 0x24, 0xFF);
        frameOutline.effectDistance = new Vector2(1.5f, -1.5f);
        _icon = UiKit.Image("Icon", frame.transform, Color.white);
        _icon.preserveAspect = true;
        _icon.raycastTarget = false;
        _icon.rectTransform.Fill(3);

        _caption = UiKit.Text("Caption", _box, "", 14, TextAlignmentOptions.TopLeft, new Color32(0xE8, 0xB8, 0x4A, 0xFF));
        _caption.rectTransform.Stretch(100, 10, 16, Height - 30);
        _caption.enableWordWrapping = false;
        _caption.characterSpacing = 4;
        _name = UiKit.Text("Name", _box, "", 22, TextAlignmentOptions.TopLeft);
        _name.rectTransform.Stretch(100, 28, 16, Height - 58);
        _name.enableWordWrapping = false;
        _name.overflowMode = TextOverflowModes.Ellipsis;
        _description = UiKit.Text("Description", _box, "", 15, TextAlignmentOptions.TopLeft, UiKit.DimTextColor);
        _description.rectTransform.Stretch(100, 60, 16, 8);
        _description.enableWordWrapping = false;
        _description.overflowMode = TextOverflowModes.Ellipsis;
    }
}

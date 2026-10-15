using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// A short message at the top of the screen (crash reports: "the screen has been black...", "your last session ended
/// unexpectedly..."). Drawn above everything, the game's black fade included, like the mini log. One message per id;
/// it goes away after its time, or with <see cref="Hide"/> (0 seconds: stays until hidden).
/// </summary>
public sealed class Notice : MonoBehaviour
{
    private const float Width = 980f;

    private static Notice _instance;
    private readonly Dictionary<string, (string text, float until)> _messages = new();
    private Canvas _canvas;
    private RectTransform _panel;
    private TextMeshProUGUI _text;
    private bool _dirty;

    public static void Show(string id, string text, float seconds)
    {
        Patches.Guard.Run("Notice", () =>
        {
            if (_instance == null)
            {
                var go = new GameObject("DamageInsight_Notice");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<Notice>();
            }
            _instance._messages[id] = (text, seconds > 0f ? Time.unscaledTime + seconds : float.MaxValue);
            _instance._dirty = true;
        });
    }

    public static void Hide(string id)
    {
        if (_instance != null && _instance._messages.Remove(id))
            _instance._dirty = true;
    }

    private void Update()
    {
        float now = Time.unscaledTime;
        var expired = new List<string>();
        foreach (var pair in _messages)
            if (pair.Value.until <= now)
                expired.Add(pair.Key);
        foreach (string id in expired)
            _messages.Remove(id);
        if (expired.Count > 0)
            _dirty = true;
        if (!_dirty)
            return;
        _dirty = false;
        Patches.Guard.Run("Notice", Draw);
    }

    private void Draw()
    {
        if (_messages.Count == 0)
        {
            if (_canvas != null)
                _canvas.enabled = false;
            return;
        }
        if (_canvas == null)
        {
            _canvas = UiKit.OverlayCanvas("DamageInsight_Notice", 5200);
            var background = UiKit.Image("Panel", _canvas.transform, new Color32(0x1B, 0x12, 0x24, 0xEE));
            _panel = background.rectTransform;
            _panel.anchorMin = _panel.anchorMax = _panel.pivot = new Vector2(0.5f, 1f);
            var outline = background.gameObject.AddComponent<UnityEngine.UI.Outline>();
            outline.effectColor = UiKit.ChipOn;
            outline.effectDistance = new Vector2(2, -2);
            _text = UiKit.Text("Text", _panel, "", 19, TextAlignmentOptions.TopLeft);
            _text.rectTransform.Stretch(18, 12, 18, 12);
        }
        _canvas.enabled = true;
        var sb = new System.Text.StringBuilder("<color=#F2D78B>SkulInsight QoL</color>");
        foreach (var message in _messages.Values)
            sb.Append('\n').Append(message.text);
        _text.text = sb.ToString();
        float height = _text.GetPreferredValues(_text.text, Width - 36f, 0f).y + 24f;
        _panel.anchoredPosition = new Vector2(0f, -24f);
        _panel.sizeDelta = new Vector2(Width, Mathf.Max(60f, height));
    }
}

using System.Linq;
using Characters;
using DamageInsight.Codex;
using TMPro;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// Testing aid under a boss's or adventurer's health bar: how many of its moves were seen and filmed, which are still
/// missing, and "ALL MOVES SEEN" once every move it has was seen (in this mode). Developer builds only for now.
/// </summary>
public class MoveCounterLabel : MonoBehaviour
{
    private CharacterHealthBar _bar;
    private RectTransform _rect;
    private TextMeshProUGUI _text, _shadow;
    private float _nextUpdate;
    private string _last = "";

    public static void Attach(CharacterHealthBar bar, TMP_FontAsset font)
    {
        if (bar == null || bar._container == null || bar._healthBar == null || bar.GetComponentInChildren<MoveCounterLabel>(true) != null)
            return;
        var go = new GameObject("DamageInsight_MoveCounter", typeof(RectTransform));
        go.layer = bar._container.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(bar._container, worldPositionStays: false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 1f);
        rect.SetAsLastSibling();
        var label = go.AddComponent<MoveCounterLabel>();
        label._bar = bar;
        label._rect = rect;
        label._shadow = Text(rect, font, new Color(0f, 0f, 0f, 0.85f), new Vector2(2, -2));
        label._text = Text(rect, font, Color.white, Vector2.zero);
    }

    private static TextMeshProUGUI Text(RectTransform parent, TMP_FontAsset font, Color color, Vector2 offset)
    {
        var go = new GameObject("Text", typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rect = (RectTransform)go.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = offset;
        var text = go.AddComponent<TextMeshProUGUI>();
        if (font != null)
            text.font = font;
        text.alignment = TextAlignmentOptions.Top;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Overflow;
        text.raycastTarget = false;
        text.color = color;
        text.richText = true;
        return text;
    }

    private void LateUpdate()
    {
        bool on = Plugin.CodexMoveCounter != null && Plugin.CodexMoveCounter.Value;
        Place();
        if (Time.unscaledTime < _nextUpdate)
            return;
        _nextUpdate = Time.unscaledTime + 0.25f;
        string text = on ? TextFor(_bar._character) : "";
        if (text == _last)
            return;
        _last = text;
        _text.text = text;
        _shadow.text = System.Text.RegularExpressions.Regex.Replace(text, "</?color[^>]*>", "");
    }

    /// <summary>Just below the health bar, as wide as the bar.</summary>
    private void Place()
    {
        RectTransform fill = _bar._healthBar;
        Vector3 fullScale = _bar._defaultHealthScale;
        if (fullScale.x == 0f)
            return;
        Vector3 parentScale = fill.parent != null ? fill.parent.lossyScale : Vector3.one;
        float width = fill.rect.width * Mathf.Abs(fullScale.x) * parentScale.x;
        float height = fill.rect.height * Mathf.Abs(fullScale.y) * parentScale.y;
        Vector3 bottom = fill.position + new Vector3((0.5f - fill.pivot.x) * width, -fill.pivot.y * height - height * 0.35f, 0f);
        _rect.position = bottom;
        Vector3 own = _rect.lossyScale;
        float barHeight = height > 1f ? height / own.y : 24f;
        float size = Mathf.Clamp(barHeight * 0.62f, 13f, 22f);
        _rect.sizeDelta = new Vector2(Mathf.Max(width / own.x, 360f), size * 3.2f);
        _text.fontSize = _shadow.fontSize = size;
    }

    private static string TextFor(Character boss)
    {
        if (boss == null)
            return "";
        string prefix = "";
        var count = FightRecorder.CountFor(boss);
        if (count == null && BossAttacks.Covers(boss) && !BossAttacks.HasAi(boss) && FightRecorder.MainBoss() is { } owner)
        {
            // A fight piece's bar (Pope's pillars) shows the boss's count: the boss may have no bar yet.
            count = FightRecorder.CountFor(owner);
            if (CodexTracker.EntryOf(owner) is { } entry)
                prefix = (CodexCatalog.Entries.FirstOrDefault(e => e.Id == entry.id)?.Name ?? entry.key) + ": ";
        }
        if (count == null)
            return "";
        string text = prefix + TextOf(count.Value);
#if DEV
        // Developer/PreferNewMoves on a tree boss: the move it is led to next.
        if (MoveDirector.TargetText(boss) is { } next)
            text += $"\n<size=80%><color=#9FD3FF>{next}</color></size>";
#endif
        return text;
    }

    private static string TextOf(FightRecorder.MoveCount count)
    {
        if (count.Pending)
            return "<color=#B8B8B8>Moves: reading its behaviour tree...</color>";
        string mode = (count.Inside > 0 ? $" <color=#B8B8B8>({count.Inside} inside other moves)</color>" : "") +
                      (count.Dark ? " <color=#C58CFF>(Dark Mirror)</color>" : "");
        if (count.Total == 0)
            return $"Moves seen {count.Seen} · filmed {count.Filmed}{mode}";
        if (count.Seen >= count.Total)
        {
            string done = $"<color=#7CFC7C>ALL {count.Total} MOVES SEEN</color> · filmed {count.Filmed}/{count.Total}{mode}";
            return count.NotFilmed.Count == 0 ? done : done + $"\n<size=80%><color=#D8C8A8>Not on film yet: {List(count.NotFilmed)}</color></size>";
        }
        return $"Moves seen <b>{count.Seen}/{count.Total}</b> · filmed {count.Filmed}{mode}" +
               $"\n<size=80%><color=#D8C8A8>Missing: {List(count.Missing)}</color></size>";
    }

    private static string List(System.Collections.Generic.List<string> names) =>
        names.Count <= 6 ? string.Join(", ", names) : string.Join(", ", names.Take(6)) + $" (+{names.Count - 6})";
}

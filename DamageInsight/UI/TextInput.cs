using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// Whether the player is typing into a text field: the Codex search, the game's developer menu, any other mod's.
/// The mod's keys (K, L) stay quiet then, so "caerleon" types an "l" instead of opening the combat log.
/// Asked only when one of those keys is pressed (it looks through the open text fields).
/// </summary>
internal static class TextInput
{
    private static int _frame = -1;
    private static bool _typing;

    public static bool IsTyping()
    {
        if (Time.frameCount != _frame)
        {
            _frame = Time.frameCount;
            _typing = Check();
        }
        return _typing;
    }

    private static bool Check()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected != null && Focused(selected))
            return true;
        // A field can be focused in another event system than the current one (the game has several).
        foreach (var field in Object.FindObjectsOfType<TMP_InputField>())
            if (field.isFocused)
                return true;
        foreach (var field in Object.FindObjectsOfType<InputField>())
            if (field.isFocused)
                return true;
        return false;
    }

    private static bool Focused(GameObject go)
    {
        var tmp = go.GetComponent<TMP_InputField>();
        if (tmp != null && tmp.isFocused)
            return true;
        var legacy = go.GetComponent<InputField>();
        return legacy != null && legacy.isFocused;
    }
}

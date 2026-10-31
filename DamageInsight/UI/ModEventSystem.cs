using UnityEngine;
using UnityEngine.EventSystems;

namespace DamageInsight.UI;

/// <summary>
/// The mod's windows need an event system for clicks. The game has its own (InControl's input module, which selects
/// a slot when the mouse hovers over it: UIManager.allowMouseInput sets focusOnMouseHover). Unity runs only the first
/// enabled event system (EventSystem.current = the first in its list), so a second one of ours that stayed alive took
/// over once the game's was enabled again after a scene load: clicks still worked, hovering selected nothing any more
/// (Umut 2026-10-28: "my mouse cant hover over items / skulls / dark abilities anymore"; a click on a dark ability buys
/// it at once). Ours is made only while no event system is there at all, and goes away as soon as the game's is back.
/// </summary>
internal static class ModEventSystem
{
    private static GameObject _ours;
    private static float _nextCheck;

    /// <summary>Makes sure clicks reach the mod's windows: only when the game has no event system of its own right now.</summary>
    public static void Ensure()
    {
        if (EventSystem.current != null)
            return;
        if (_ours != null)
            return;
        _ours = new GameObject("DamageInsight_EventSystem");
        Object.DontDestroyOnLoad(_ours);
        _ours.AddComponent<EventSystem>();
        _ours.AddComponent<StandaloneInputModule>();
        Plugin.Log.LogInfo("UI: no event system in the scene; the mod made a stand-in until the game's is back.");
    }

    /// <summary>Twice a second: our stand-in goes away once another event system (the game's) is enabled.</summary>
    public static void Tick()
    {
        if (_ours == null || Time.unscaledTime < _nextCheck)
            return;
        _nextCheck = Time.unscaledTime + 0.5f;
        var mine = _ours.GetComponent<EventSystem>();
        foreach (var system in Object.FindObjectsOfType<EventSystem>())
        {
            if (system == mine || !system.isActiveAndEnabled)
                continue;
            Object.Destroy(_ours);
            _ours = null;
            // Destroy happens at the end of the frame: the game's is put first now, so it runs from this frame on.
            EventSystem.current = system;
            Plugin.Log.LogInfo($"UI: the game's event system ({system.name}) is back; the mod's stand-in is gone.");
            return;
        }
    }

    /// <summary>The event system that runs is one of ours (only while the game has none).</summary>
    public static bool OursRuns => _ours != null && EventSystem.current != null && EventSystem.current.gameObject == _ours;
}

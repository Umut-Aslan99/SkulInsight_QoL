using FX;
using Scenes;
using Singletons;
using UnityEngine;

namespace DamageInsight.UI;

/// <summary>
/// The game's own menu sounds on the mod's buttons (Umut 2026-10-12), taken from its pause menu: the "select" tick when
/// the mouse moves onto a button or clicks it, the close sound when a window is dismissed. Setting General/MenuSounds.
/// </summary>
internal static class MenuSounds
{
    public enum Kind { Select, Close }

    public static void Play(Kind kind)
    {
        if (Plugin.MenuSoundsEnabled == null || !Plugin.MenuSoundsEnabled.Value)
            return;
        var pause = Scene<GameBase>.instance?.uiManager?._pause;
        if (pause == null)
            return;
        var sound = kind == Kind.Close ? pause._closeSound : pause._selectSound;
        if (sound?.audioClip != null)
            PersistentSingleton<SoundManager>.Instance?.PlaySound(sound, Vector3.zero);
    }
}

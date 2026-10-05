using System;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using UnityEngine;

namespace DamageInsight.Codex;

/// <summary>
/// What the player brings into a boss film and a clean film leaves out (setting Codex/CleanFilms): floating numbers,
/// the effects of the player's skills and items (fields, flames, explosions, hit sparks), the player's summons and
/// minions, and status effects on the boss (poison, burn, freeze, stun, wound).
/// The game's effects carry no owner, so the character the game is acting for is noted while it runs operations or
/// spawns hit effects (<see cref="Enter"/>/<see cref="Leave"/>, from Patches/FilmClutterPatches), and every effect
/// started meanwhile is remembered as the player's when that character is the player or one of its minions.
/// Not covered: particles (shared particle systems) and the color tint a status puts on the boss's own sprite.
/// </summary>
public static class FilmClutter
{
    private static readonly List<Character> Acting = new();
    private static readonly HashSet<EffectPoolInstance> FromPlayer = new();
    private static HashSet<RuntimeAnimatorController> _statusLooks;

    /// <summary>The game starts acting for a character (null when unknown): effects started now are its.</summary>
    public static void Enter(Character owner) => Acting.Add(owner);

    public static void Leave()
    {
        if (Acting.Count > 0)
            Acting.RemoveAt(Acting.Count - 1);
    }

    /// <summary>An effect was started (pooled effects are reused, so its owner is decided anew every time).</summary>
    public static void Played(EffectPoolInstance effect)
    {
        if (effect == null)
            return;
        var owner = Acting.Count > 0 ? Acting[Acting.Count - 1] : null;
        if (owner != null && IsPlayerSide(owner))
            FromPlayer.Add(effect);
        else
            FromPlayer.Remove(effect);
    }

    /// <summary>The player or one of its minions (summons fighting for the player).</summary>
    public static bool IsPlayerSide(Character character)
    {
        if (character == null)
            return false;
        var player = Singletons.Singleton<Services.Service>.Instance?.levelManager?.player;
        if (player == null)
            return false;
        if (character == player)
            return true;
        var minion = character.GetComponent<Minion>() ?? character.GetComponentInParent<Minion>();
        return minion != null && minion.leader != null && minion.leader.player == player;
    }

    /// <summary>Calls <paramref name="hide"/> for everything a clean film leaves out (when the setting is on).</summary>
    public static void HideAll(Action<Component> hide)
    {
        if (!Plugin.CodexCleanFilms.Value)
            return;
        foreach (var number in UnityEngine.Object.FindObjectsOfType<FloatingText>())
            hide(number);
        var looks = StatusLooks();
        foreach (var effect in UnityEngine.Object.FindObjectsOfType<EffectPoolInstance>())
            if (FromPlayer.Contains(effect) || effect.animator != null && looks.Contains(effect.animator.runtimeAnimatorController))
                hide(effect);
        foreach (var runner in UnityEngine.Object.FindObjectsOfType<OperationRunner>())
            if (IsPlayerSide(runner.operationInfos?.owner))
                hide(runner);
        foreach (var minion in UnityEngine.Object.FindObjectsOfType<Minion>())
            if (IsPlayerSide(minion.character))
                hide(minion);
    }

    /// <summary>The animations the game plays for status effects (CharacterStatusSetting, stun and freeze looks).</summary>
    private static HashSet<RuntimeAnimatorController> StatusLooks()
    {
        if (_statusLooks != null)
            return _statusLooks;
        var looks = new HashSet<RuntimeAnimatorController>();
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var settings = CharacterStatusSetting.instance;
        if (settings != null)
            foreach (var group in typeof(CharacterStatusSetting).GetFields(fields))
                if (group.GetValue(settings) is { } status && !(status is UnityEngine.Object))
                    foreach (var field in status.GetType().GetFields(fields))
                        if (field.GetValue(status) is RuntimeAnimatorController look)
                            looks.Add(look);
        var common = GameResources.CommonResource.instance;
        if (common != null)
            foreach (var field in common.GetType().GetFields(fields))
            {
                string name = field.Name.ToLowerInvariant();
                if ((name.Contains("freeze") || name.Contains("stun")) && field.GetValue(common) is RuntimeAnimatorController look)
                    looks.Add(look);
            }
        Plugin.Log.LogInfo($"Codex: clean films leave out {looks.Count} status effect looks.");
        return _statusLooks = looks;
    }
}

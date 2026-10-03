using Characters;
using Characters.Abilities;
using Characters.Cooldowns;
using Characters.Player;
using DamageInsight.UI;
using HarmonyLib;
using UI.Hud;
using UnityEngine.UI;

namespace DamageInsight.Patches;

/// <summary>
/// WoW-style cooldown numbers on the HUD's ability icons (items, inscriptions, buffs at the bottom of the
/// screen). AbilityIconDisplay.Update fills its icons in order from the abilities that have an icon; after it
/// ran, walk the same order and put the remaining seconds on each icon.
/// </summary>
[HarmonyPatch(typeof(AbilityIconDisplay), "Update")]
public static class CooldownTickerPatch
{
    private static bool _failed;

    private static void Postfix(AbilityIcon[] ____icons, Character ____character)
    {
        if (_failed || ____icons == null || ____character == null)
            return;
        try
        {
            Update(____icons, ____character);
        }
        catch (System.Exception e)
        {
            // Runs every frame: switch off after the first error instead of spamming the log.
            _failed = true;
            Plugin.Log.LogWarning($"Cooldown ticker switched off after an error: {e}");
        }
    }

    private static void Update(AbilityIcon[] ____icons, Character ____character)
    {
        bool enabled = Plugin.CooldownTicker.Value;
        int index = 0;
        foreach (var icon in ____icons)
        {
            if (icon == null || !icon.gameObject.activeSelf)
                continue;
            IAbilityInstance instance = NextWithIcon(____character, ref index);
            string text = enabled && instance != null ? CooldownFormat.Format(CooldownReader.Remaining(instance)) : "";
            CooldownLabel.Set(icon.transform, text, icon._stackText != null ? icon._stackText.font : null);
        }
    }

    // Same order as AbilityIconDisplay.FindNextAbilityWithIcon.
    private static IAbilityInstance NextWithIcon(Character character, ref int index)
    {
        while (index < character.ability.Count)
        {
            var instance = character.ability[index++];
            if (instance.icon != null)
                return instance;
        }
        return null;
    }
}

/// <summary>
/// The same seconds on the skill icons of both skulls (the small ones next to the swap icon too) and on the
/// quintessence icon; ActionIcon and EssenceIcon both run IconWithCooldown.Update. A time cooldown counts
/// remainTime down by deltaTime x its own speed (skill or quintessence cooldown speed stat), so the real
/// seconds left are remainTime / speed. Streaks and gauge cooldowns show nothing (the game shows no fill).
/// </summary>
[HarmonyPatch(typeof(global::UI.IconWithCooldown), "Update")]
public static class SkillCooldownTickerPatch
{
    private static bool _failed;

    private static void Postfix(global::UI.IconWithCooldown __instance)
    {
        if (_failed || __instance == null || __instance._cooldownMask == null)
            return;
        try
        {
            var cooldown = __instance.cooldown;
            double seconds = 0;
            if (Plugin.CooldownTicker.Value && cooldown != null && cooldown.type == CooldownSerializer.Type.Time
                && cooldown.time != null && cooldown.remainPercent > 0.001f)
                seconds = CooldownFormat.RealSeconds(cooldown.time.remainTime, cooldown.time.GetCooldownSpeed());
            var font = __instance._remainStreaks != null ? __instance._remainStreaks.font : null;
            CooldownLabel.Set(__instance._cooldownMask.transform, CooldownFormat.Format(seconds), font);
        }
        catch (System.Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning($"Skill cooldown ticker switched off after an error: {e}");
        }
    }
}

/// <summary>
/// Seconds on the swap icon. WeaponInventory counts _remainCooldown (8 s) down by deltaTime x the swap
/// cooldown speed stat, so the real seconds left are _remainCooldown / speed.
/// </summary>
[HarmonyPatch(typeof(HeadupDisplay), "Update")]
public static class SwapCooldownTickerPatch
{
    private static bool _failed;

    private static void Postfix(Image ____changeWeaponCooldown, WeaponInventory ____weaponInventory, Character ____character,
        global::UI.ActionIcon[] ____skills)
    {
        if (_failed || ____changeWeaponCooldown == null)
            return;
        try
        {
            double seconds = 0;
            if (Plugin.CooldownTicker.Value && ____character != null && ____weaponInventory != null && ____weaponInventory.next != null)
                seconds = CooldownFormat.RealSeconds(____weaponInventory._remainCooldown, ____character.stat.GetSwapCooldownSpeed());
            var streaks = ____skills != null && ____skills.Length > 0 && ____skills[0] != null ? ____skills[0]._remainStreaks : null;
            CooldownLabel.Set(____changeWeaponCooldown.transform, CooldownFormat.Format(seconds), streaks != null ? streaks.font : null);
        }
        catch (System.Exception e)
        {
            _failed = true;
            Plugin.Log.LogWarning($"Swap cooldown ticker switched off after an error: {e}");
        }
    }
}

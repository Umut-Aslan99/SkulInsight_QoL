using System;
using System.Collections;
using Characters;
using DamageInsight.UI;
using HarmonyLib;
using Scenes;
using UI;
using UnityEngine;

namespace DamageInsight.Patches;

/// <summary>
/// Replaces how damage numbers dealt to enemies are shown:
/// - a small tag in front of the number says where the damage came from ("ITEM 45"),
/// - numbers pop up, stay in place for a while, then fade out slowly.
/// </summary>
/// <remarks>
/// We replace FloatingTextSpawner.SpawnTakingDamage entirely. Colors, sorting and the
/// crit "pop" are copied from the original so the numbers still look like the game's.
/// If anything goes wrong, we fall back to the original method.
/// </remarks>
[HarmonyPatch(typeof(FloatingTextSpawner))]
public static class DamageNumberPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(FloatingTextSpawner.SpawnTakingDamage))]
    private static bool ReplaceSpawnTakingDamage(FloatingTextSpawner __instance, in Damage damage)
    {
        try
        {
            SpawnTakingDamage(__instance, damage);
            return false; // skip the original
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Damage number failed, using the game's default: {e}");
            return true;
        }
    }

    private static void SpawnTakingDamage(FloatingTextSpawner spawner, in Damage damage)
    {
        if (Scene<GameBase>.instance.uiManager.hideOption == UIManager.HideOption.HideAll || damage.amount < 1.0)
            return;

        string text = damage.ToString();
        DamageSource source = DamageSources.Classify(damage);

        FloatingText floatingText = spawner.Spawn(text, damage.hitPoint + new Vector2(0f, 0.5f));

        switch (damage.attribute)
        {
            case Damage.Attribute.Physical:
                floatingText.color = spawner._physicalAttackColor;
                floatingText.sortingOrder = 100;
                break;
            case Damage.Attribute.Magic:
                floatingText.color = spawner._magicAttackColor;
                floatingText.sortingOrder = 200;
                break;
            case Damage.Attribute.Fixed:
                floatingText.color = spawner._fixedAttackColor;
                floatingText.sortingOrder = 100;
                break;
        }
        if (damage.critical)
        {
            floatingText.Modify(GameObjectModifier.LerpScale(1.4f, 1.6f, 0.4f));
            switch (damage.attribute)
            {
                case Damage.Attribute.Physical:
                    floatingText.color = spawner._criticalPhysicalAttackColor;
                    floatingText.sortingOrder = 300;
                    break;
                case Damage.Attribute.Magic:
                    floatingText.color = spawner._criticalMagicAttackColor;
                    floatingText.sortingOrder = 400;
                    break;
            }
        }

        // Mark the source: an icon if we have one, otherwise a small coloured text tag.
        SpriteRenderer icon = null;
        if (Plugin.DamageSourceTags.Value)
        {
            Sprite sprite = Plugin.DamageSourceIcons.Value ? IconLibrary.Get(source) : null;
            if (sprite != null)
            {
                icon = PopupIcon.Show(floatingText, sprite);
            }
            else
            {
                string tag = DamageSources.Label(source);
                if (tag.Length > 0)
                {
                    // The game turns off rich text on damage popups; we need it for the smaller, coloured tag.
                    // (TMP caps a <color> tag's alpha at the text's own alpha, so the tag still fades out.)
                    floatingText._text.richText = true;
                    floatingText.text = $"<color={DamageSources.ColorHex(source)}><size=60%>{tag}</size></color> {text}";
                }
            }
        }

        // FloatingText.Initialize stops all coroutines when the pooled text is reused,
        // so this animation never leaks onto the next number.
        float drift = MMMaths.RandomBool() ? 0.2f : -0.2f;
        floatingText.StartCoroutine(Animate(floatingText, icon, drift));
    }

    /// <summary>Pop up quickly, hold still, fade out, despawn.</summary>
    private static IEnumerator Animate(FloatingText floatingText, SpriteRenderer icon, float driftPerSecond)
    {
        const float riseTime = 0.35f;
        const float riseHeight = 1.2f;
        float holdTime = Mathf.Max(0f, Plugin.DamageNumberHoldTime.Value);
        float fadeTime = Mathf.Max(0.01f, Plugin.DamageNumberFadeTime.Value);

        Transform transform = floatingText.transform;
        Vector3 start = transform.position;
        Color baseColor = floatingText.color;
        float elapsed = 0f;

        while (elapsed < riseTime + holdTime + fadeTime)
        {
            yield return null;
            elapsed += Chronometer.global.deltaTime;

            // Ease-out rise: fast at first, slowing to a stop.
            float riseT = Mathf.Clamp01(elapsed / riseTime);
            float rise = riseHeight * (1f - (1f - riseT) * (1f - riseT));
            transform.position = start + new Vector3(driftPerSecond * Mathf.Min(elapsed, riseTime + holdTime), rise, 0f);

            float fadeT = Mathf.Clamp01((elapsed - riseTime - holdTime) / fadeTime);
            Color c = baseColor;
            c.a = baseColor.a * (1f - fadeT);
            floatingText.color = c;
            if (icon != null)
                icon.color = new Color(1f, 1f, 1f, 1f - fadeT);
        }

        floatingText.Despawn();
    }
}

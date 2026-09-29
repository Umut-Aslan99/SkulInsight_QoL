using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UI.Inventory;

namespace DamageInsight.Patches;

/// <summary>
/// The inscription panels give every step text a fixed maximum height (so the panel fits its frame).
/// Our damage lines can make a step longer than that (e.g. Arms' armaments), and the text then runs into
/// the next step. Before the game measures a step, shrink its font just enough to fit (down to 60%).
/// </summary>
[HarmonyPatch(typeof(InscriptionStepElement), nameof(InscriptionStepElement.preferredHeight), MethodType.Getter)]
public static class InscriptionTextFitPatch
{
    private const float MinScale = 0.6f;

    /// <summary>The font size each step text had before we touched it (the elements are reused).</summary>
    private static readonly Dictionary<int, float> OriginalSizes = new();

    private static void Prefix(TextMeshProUGUI ____description, float ____maxHeight)
    {
        var text = ____description;
        if (text == null || ____maxHeight <= 0)
            return;

        int id = text.GetInstanceID();
        if (!OriginalSizes.TryGetValue(id, out float original))
            OriginalSizes[id] = original = text.fontSize;

        text.fontSize = original;
        float height = text.preferredHeight;
        if (height <= ____maxHeight)
            return;

        // Text height grows roughly with the square of the font size (line height x number of wrapped lines).
        float size = original * UnityEngine.Mathf.Sqrt(____maxHeight / height);
        float min = original * MinScale;
        for (int i = 0; i < 12; i++)
        {
            text.fontSize = UnityEngine.Mathf.Max(size, min);
            if (text.fontSize <= min || text.preferredHeight <= ____maxHeight)
                break;
            size -= original * 0.03f;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Patches;
using DamageInsight.Recording;
using DamageInsight.UI;
using GameResources;

namespace DamageInsight.Tools;

/// <summary>
/// In-game self-check. Writes "[SelfTest] PASS/FAIL ..." lines to BepInEx\LogOutput.log so problems show up
/// as precise messages instead of "something looks wrong":
/// - at startup: was every patch applied?
/// - when a run starts: game resources, UI font, dialogue background, every configured icon.
/// - while playing: the first recorded hit, and the first hit of each damage source (shows classification works).
/// </summary>
public static class SelfTest
{
    private const string Tag = "[SelfTest]";
    private static readonly HashSet<DamageSource> SeenSources = new();
    private static bool _runChecked;

    public static void Pass(string check, string detail = "") =>
        Plugin.Log.LogInfo($"{Tag} PASS  {check}{(detail.Length > 0 ? " — " + detail : "")}");

    public static void Fail(string check, string detail) =>
        Plugin.Log.LogError($"{Tag} FAIL  {check} — {detail}");

    public static void ReportPatches(IReadOnlyList<PatchRegistry.Result> results)
    {
        foreach (var r in results)
        {
            string name = r.PatchClass.FullName?.Replace("DamageInsight.", "");
            if (r.Ok)
                Pass($"patch {name}", $"{r.PatchedMethods} method(s)");
            else
                Fail($"patch {name}", r.Error != null ? r.Error.GetBaseException().Message : "patched no methods");
        }
        int ok = results.Count(r => r.Ok);
        Plugin.Log.LogInfo($"{Tag} Startup: {ok}/{results.Count} patches applied");
    }

    /// <summary>Runs once, the first time the player exists (i.e. a run has started).</summary>
    public static void CheckRun()
    {
        if (_runChecked)
            return;
        _runChecked = true;
        int failures = 0;

        void Check(string name, bool ok, string okDetail, string failDetail)
        {
            if (ok) Pass(name, okDetail);
            else { Fail(name, failDetail); failures++; }
        }

        try
        {
            Check("game resources", CommonResource.instance != null && GearResource.instance != null,
                "CommonResource and GearResource loaded", "CommonResource/GearResource missing; icons won't work");

            var font = UiKit.Font;
            Check("UI font", font != null, font != null ? font.name : "", "no TextMeshPro font found; combat log text will be missing");

            if (Plugin.CombatLogUseDialogueBackground.Value)
            {
                var background = CombatLogWindow.FindDialogueBackground();
                Check("combat log background", background != null,
                    background != null ? $"'{background.sprite.name}'" : "",
                    "NPC dialogue box artwork not found; the log uses a plain panel");
            }

            foreach (DamageSource source in Enum.GetValues(typeof(DamageSource)))
            {
                string spec = IconLibrary.Spec(source);
                if (spec.Equals("none", StringComparison.OrdinalIgnoreCase))
                    continue;
                var sprite = IconLibrary.Get(source);
                Check($"icon {source}", sprite != null, $"{spec} -> '{sprite?.name}'", $"'{spec}' not found; shows the text tag instead");
            }
        }
        catch (Exception e)
        {
            Fail("run checks", e.ToString());
            failures++;
        }
        Plugin.Log.LogInfo($"{Tag} Run checks done: {failures} failure(s)");
    }

    /// <summary>Called for every recorded hit; reports the first one per damage source.</summary>
    public static void Observe(in DamageRecord record)
    {
        if (SeenSources.Count == 0)
            Pass("damage recording", "first hit recorded");
        if (SeenSources.Add(record.Source))
            Pass($"seen source {record.Source}", LogText.Plain(LogText.Line(record)));
    }
}

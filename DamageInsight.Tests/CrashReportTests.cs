using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DamageInsight.Crash;
using DamageInsight.Patches;
using Xunit;

namespace DamageInsight.Tests;

/// <summary>Crash reports (0.12.2): whose code an error is in, the session file, the verdict, the session before.</summary>
public class CrashReportTests
{
    private const string Own = "docrun.skulinsight_qol";

    // A real trace from Umut's Player.log (2026-10-12): game code inside a coroutine the mod patches.
    private const string HookedTrace =
        "  at Characters.Actions.Action.get_running () [0x00000] in <8c23d616e7614c93a0b7a99cd9faad49>:0 \n" +
        "  at (wrapper dynamic-method) CutScenes.Shots.Sequences.RunAction+<CRun>d__1.DMD<CutScenes.Shots.Sequences.RunAction+<CRun>d__1::MoveNext>(CutScenes.Shots.Sequences.RunAction/<CRun>d__1)\n" +
        "  at UnityEngine.SetupCoroutine.InvokeMoveNext (System.Collections.IEnumerator enumerator, System.IntPtr returnValueAddress) [0x00026] in <ce005c3cee8b443589f0426beab49e43>:0 \n";

    private const string GameTrace = "Level.Npc.Witch.OnDisable () (at <8c23d616e7614c93a0b7a99cd9faad49>:0)\n";

    private const string ModTrace =
        "DamageInsight.UI.MiniLog.Update () (at <0000>:0)\n" +
        "UnityEngine.Debug:LogException(Exception)\n";

    private static ErrorClassifier Classifier()
    {
        var c = new ErrorClassifier(Own);
        c.SetHooks(new[]
        {
            new KeyValuePair<string, IEnumerable<string>>("CutScenes.Shots.Sequences.RunAction+<CRun>d__1::MoveNext", new[] { Own + ".attacks" }),
            new KeyValuePair<string, IEnumerable<string>>("Level.Map::Awake", new[] { "someone.else" }),
        });
        c.SetOtherMods(new[] { ("HardMode.Patches", "Hard Mode") });
        return c;
    }

    [Fact]
    public void Our_code_in_the_trace_is_the_mod()
    {
        var c = Classifier().Classify(ModTrace);
        Assert.Equal(Blame.Mod, c.Blame);
        Assert.StartsWith("DamageInsight.UI.MiniLog.Update", c.Detail);
    }

    [Fact]
    public void A_game_method_we_patch_is_hooked_not_the_mod()
    {
        var c = Classifier().Classify(HookedTrace);
        Assert.Equal(Blame.Hooked, c.Blame);
        Assert.Equal("CutScenes.Shots.Sequences.RunAction+<CRun>d__1::MoveNext", c.Detail);
        Assert.Equal("Characters.Actions.Action.get_running ()", c.Where);
    }

    [Fact]
    public void Another_mods_hook_or_code_is_another_mod()
    {
        var hook = Classifier().Classify("(wrapper dynamic-method) Level.Map.DMD<Level.Map::Awake>(Level.Map)\n");
        Assert.Equal(Blame.OtherMod, hook.Blame);
        Assert.Contains("someone.else", hook.Detail);
        var code = Classifier().Classify("at HardMode.Patches.EnemyBuff.Postfix () [0x00010] in <abc>:0\n" + GameTrace);
        Assert.Equal(Blame.OtherMod, code.Blame);
        Assert.StartsWith("Hard Mode:", code.Detail);
    }

    [Fact]
    public void Only_game_code_is_the_game()
    {
        var c = Classifier().Classify(GameTrace);
        Assert.Equal(Blame.Game, c.Blame);
        Assert.Equal("Level.Npc.Witch.OnDisable ()", c.Where);
    }

    [Fact]
    public void Frames_lose_addresses_so_sessions_compare()
    {
        var frames = ErrorClassifier.Frames(HookedTrace);
        Assert.Equal(3, frames.Count);
        Assert.Equal("UnityEngine.SetupCoroutine.InvokeMoveNext (System.Collections.IEnumerator enumerator, System.IntPtr returnValueAddress)", frames[2]);
    }

    private static string TempFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "skulinsight-crash-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private sealed class Clock
    {
        public DateTime Now = new(2026, 10, 15, 20, 0, 0);
        public DateTime Get() => Now;
    }

    [Fact]
    public void A_normal_session_ends_with_its_marker_and_counts_repeats()
    {
        string folder = TempFolder();
        var clock = new Clock();
        var report = new SessionReport(folder, Classifier(), clock.Get);
        report.Open("Mod: test");
        report.Event("scene: gameBase");
        for (int i = 0; i < 5; i++)
        {
            clock.Now = clock.Now.AddSeconds(1);
            report.Error("Exception", "NullReferenceException: Object reference not set", GameTrace);
        }
        report.End();

        string text = File.ReadAllText(report.FilePath);
        Assert.Contains("scene: gameBase", text);
        Assert.Contains("ERROR #1 [GAME] Exception: NullReferenceException", text);
        Assert.Contains("ERROR #1 again (x3)", text);
        Assert.DoesNotContain("again (x4)", text);
        Assert.Contains("#1 [GAME] x5", text);
        Assert.Contains(SessionReport.EndMarker, text);
        Assert.Contains("No freeze was noticed", text);
        Assert.False(File.Exists(SessionReport.SnapshotPath(report.FilePath)), "the snapshot goes when the session ends");
    }

    [Fact]
    public void A_freeze_gets_a_verdict_from_the_errors_right_before_it()
    {
        string folder = TempFolder();
        var clock = new Clock();
        var report = new SessionReport(folder, Classifier(), clock.Get);
        report.Open("Mod: test");
        report.Error("Exception", "old game error", GameTrace);            // long before: doesn't count
        clock.Now = clock.Now.AddMinutes(5);
        var black = clock.Now;
        clock.Now = clock.Now.AddSeconds(1);
        report.Error("Exception", "NullReferenceException", HookedTrace);  // right before
        report.Caught("x", new Blame?(Blame.Mod));                        // caught by us: never the cause
        clock.Now = clock.Now.AddSeconds(20);
        string verdict = report.Incident(SessionReport.StuckTitle, "state", black);
        Assert.StartsWith("POSSIBLY SKULINSIGHT", verdict);
        Assert.Contains("RunAction+<CRun>d__1::MoveNext", verdict);
        report.End();

        string text = File.ReadAllText(report.FilePath);
        Assert.Contains("!!! INCIDENT: the screen stayed black", text);
        Assert.Equal(verdict, SessionReport.LastVerdict(text));
        Assert.Equal(Blame.Hooked, SessionReport.BlameOf(verdict));
        Assert.Equal(1, SessionReport.FinishPrevious(SessionReport.NewestReport(folder), clock.Now).StuckIncidents);
    }

    [Fact]
    public void A_black_screen_that_comes_back_was_a_slow_load()
    {
        string folder = TempFolder();
        var clock = new Clock();
        var report = new SessionReport(folder, Classifier(), clock.Get);
        report.Open("Mod: test");
        clock.Now = clock.Now.AddSeconds(20);
        report.Incident(SessionReport.StuckTitle, "state", clock.Now.AddSeconds(-20));
        Assert.Equal(1, report.OpenIncidents);
        clock.Now = clock.Now.AddSeconds(4);
        report.Resolve(SessionReport.StuckTitle, "the screen came back after 24 s");
        Assert.Equal(0, report.OpenIncidents);
        report.End();
        string text = File.ReadAllText(report.FilePath);
        Assert.Contains("Resolved by itself (the screen stayed black", text);
        var previous = SessionReport.FinishPrevious(SessionReport.NewestReport(folder), clock.Now);
        Assert.Equal(0, previous.StuckIncidents); // nothing to tell at the next start
        Assert.False(previous.EndedUnexpectedly);
    }

    [Fact]
    public void Judge_takes_the_worst_blame_and_says_when_there_is_none()
    {
        ErrorRecord Record(int n, Blame blame) => new() { Number = n, Kind = "Exception", Class = new Classification(blame, "d" + n, "w" + n) };
        Assert.Null(SessionReport.Judge(new List<ErrorRecord>()).Blame);
        Assert.StartsWith("NO ERROR", SessionReport.Judge(new List<ErrorRecord>()).Text);
        Assert.Equal(Blame.Game, SessionReport.Judge(new[] { Record(1, Blame.Game) }).Blame);
        Assert.Equal(Blame.OtherMod, SessionReport.Judge(new[] { Record(1, Blame.Game), Record(2, Blame.OtherMod) }).Blame);
        var mod = SessionReport.Judge(new[] { Record(1, Blame.Hooked), Record(2, Blame.Mod), Record(3, Blame.Game) });
        Assert.Equal(Blame.Mod, mod.Blame);
        Assert.Contains("#2", mod.Text);
        // The notice reads the blame back from the sentence saved in the file.
        foreach (Blame blame in Enum.GetValues(typeof(Blame)))
            Assert.Equal(blame, SessionReport.BlameOf(SessionReport.Judge(new[] { Record(1, blame) }).Text));
        Assert.Null(SessionReport.BlameOf(SessionReport.Judge(new List<ErrorRecord>()).Text));
    }

    [Fact]
    public void A_session_without_its_end_is_marked_at_the_next_start()
    {
        string folder = TempFolder();
        var clock = new Clock();
        var crashed = new SessionReport(folder, Classifier(), clock.Get);
        crashed.Open("Mod: test");
        clock.Now = clock.Now.AddSeconds(30);
        crashed.Error("Exception", "NullReferenceException", ModTrace);
        crashed.Tick();
        crashed.Abandon(); // the game dies here: no End()

        Assert.Equal(crashed.FilePath, SessionReport.NewestReport(folder)); // what the next start looks at, before its own report
        var previous = SessionReport.FinishPrevious(crashed.FilePath, clock.Now.AddMinutes(1), Classifier(), _ =>
        {
            var extras = new SessionReport.Extras();
            extras.Sections.Add(("Unity log of that session (Player-prev.log)", "Crash!!!"));
            return extras;
        });
        Assert.NotNull(previous);
        Assert.True(previous.EndedUnexpectedly);
        Assert.StartsWith("LIKELY SKULINSIGHT", previous.Verdict); // no crash stack: the last minute's errors
        string text = File.ReadAllText(crashed.FilePath);
        Assert.Contains(SessionReport.UnexpectedMarker, text);
        Assert.Contains("Crash!!!", text);
        Assert.False(File.Exists(SessionReport.SnapshotPath(crashed.FilePath)));

        // Looked at again (the next start after that): already finished, nothing added.
        var again = SessionReport.FinishPrevious(crashed.FilePath, clock.Now.AddMinutes(2));
        Assert.False(again.EndedUnexpectedly);
        Assert.Equal(text, File.ReadAllText(crashed.FilePath));
    }

    // Unity's crash handler, Umut's Ctrl+F10 test of 2026-10-15 (Player-prev.log, shortened).
    private static readonly string[] CrashLog =
    {
        "[Info   :SkulInsight QoL] Codex: something",
        "Crash!!!",
        "SymInit: Symbol-SearchPath: '.;C:\\Program Files (x86)\\Steam\\steamapps\\common\\Skul', symOptions: 534, UserName: 'Umut'",
        "OS-Version: 10.0.0",
        "C:\\Windows\\SYSTEM32\\ntdll.dll:ntdll.dll (00007FFE08410000), size: 2064384 (result: 0), SymType: '-deferred-', PDB: ''",
        "",
        "========== OUTPUTTING STACK TRACE ==================",
        "",
        "0x00007FFE05D05369 (KERNELBASE) RaiseException",
        "0x00007FFCF9F38467 (UnityPlayer) UnityMain",
        "0x00000251AC389D8A (Mono JIT Code) (wrapper managed-to-native) UnityEngine.Diagnostics.Utils:ForceCrash (UnityEngine.Diagnostics.ForcedCrashCategory)",
        "0x00000251612FEC6B (Mono JIT Code) DamageInsight.Crash.CrashReporter:TestKeys ()",
        "0x00000251612FD021 (Mono JIT Code) DamageInsight.Patches.Guard:Run (string,System.Action)",
        "0x0000025119ADF430 (Mono JIT Code) (wrapper runtime-invoke) object:runtime_invoke_void__this__ (object,intptr,intptr,intptr)",
        "0x00007FFCFD18BB1F (mono-2.0-bdwgc) mono_runtime_invoke",
        "",
        "========== END OF STACKTRACE ===========",
        "",
        "A crash has been intercepted by the crash handler.",
    };

    [Fact]
    public void A_crash_stack_names_whose_code_crashed()
    {
        var (excerpt, stack) = SessionReport.ReadUnityLog(CrashLog);
        Assert.NotNull(stack);
        Assert.Contains("DamageInsight.Crash.CrashReporter:TestKeys", stack);
        Assert.DoesNotContain("SymType", excerpt); // the crash handler's module list stays out
        Assert.Contains("Crash!!!", excerpt);

        var ours = SessionReport.JudgeCrash(Classifier(), stack);
        Assert.Equal(Blame.Mod, ours.Blame);
        Assert.Contains("DamageInsight.Crash.CrashReporter:TestKeys", ours.Text);

        var game = SessionReport.JudgeCrash(Classifier(), "0x0001 (Mono JIT Code) Level.Chapter:ChangeMap (Level.PathNode)\n0x0002 (UnityPlayer) UnityMain");
        Assert.Equal(Blame.Game, game.Blame);
        var native = SessionReport.JudgeCrash(Classifier(), "0x00007FFD (nvwgf2umx) OpenAdapter12\n0x00007FFC (UnityPlayer) UnityMain");
        Assert.Equal(Blame.Game, native.Blame);
        Assert.Contains("nvwgf2umx", native.Text);
        foreach (var v in new[] { ours, game, native })
            Assert.Equal(v.Blame, SessionReport.BlameOf(v.Text));
    }

    [Fact]
    public void A_crashed_session_gets_the_crash_stack_verdict()
    {
        string folder = TempFolder();
        var clock = new Clock();
        var crashed = new SessionReport(folder, Classifier(), clock.Get);
        crashed.Open("Mod: test");
        crashed.Error("Error", "Check for something", "");   // a game line in the last minute: the stack decides instead
        crashed.Abandon();
        var previous = SessionReport.FinishPrevious(crashed.FilePath, clock.Now.AddMinutes(1), Classifier(), _ =>
        {
            var extras = new SessionReport.Extras();
            var (excerpt, stack) = SessionReport.ReadUnityLog(CrashLog);
            extras.CrashStack = stack;
            extras.Sections.Add(("Unity log of that session (Player-prev.log)", excerpt));
            return extras;
        });
        Assert.StartsWith("LIKELY SKULINSIGHT: its own code is on the crash's stack", previous.Verdict);
        string text = File.ReadAllText(crashed.FilePath);
        Assert.Contains("VERDICT (the crash, from its stack): LIKELY SKULINSIGHT", text);
        Assert.Contains("The game crashed", text);
    }

    [Fact]
    public void A_patched_method_of_unknown_owner_may_be_ours()
    {
        var c = Classifier().Classify("(wrapper dynamic-method) BehaviorDesigner.Runtime.Tasks.Task.DMD<BehaviorDesigner.Runtime.Tasks.Task::OnStart>(BehaviorDesigner.Runtime.Tasks.Task)\n");
        Assert.Equal(Blame.Hooked, c.Blame);
        Assert.StartsWith("BehaviorDesigner.Runtime.Tasks.Task::OnStart", c.Detail);
    }

    [Fact]
    public void Only_the_newest_reports_are_kept()
    {
        string folder = TempFolder();
        for (int i = 0; i < SessionReport.KeepReports + 5; i++)
            File.WriteAllText(Path.Combine(folder, SessionReport.FileName(new DateTime(2026, 1, 1).AddHours(i))), SessionReport.EndMarker);
        var clock = new Clock();
        var report = new SessionReport(folder, Classifier(), clock.Get);
        report.Open("Mod: test");
        report.End();
        var left = Directory.GetFiles(folder, "*.txt").Where(f => !f.EndsWith(".summary.txt")).ToList();
        Assert.Equal(SessionReport.KeepReports, left.Count);
        Assert.Contains(left, f => f == report.FilePath);
    }

    [Fact]
    public void Report_file_names_give_back_the_start()
    {
        var start = new DateTime(2026, 10, 15, 20, 14, 3);
        Assert.Equal(start, SessionReport.StartOf(SessionReport.FileName(start)));
        Assert.Null(SessionReport.StartOf("notes.txt"));
    }

    [Fact]
    public void Details_go_beside_the_popup_where_there_is_room()
    {
        // Screen 0..648 wide (the popup canvas), popup 217 wide, gap 4, boxes 158 (skill / inscription).
        float[] Place(float popupLeft, params float[] widths) => BesideLayout.Place(popupLeft, popupLeft + 217, 0, 648, 4, widths);

        // One box: on the right where it fits (the popup in the middle: 215.5), else on the left.
        Assert.Equal(new[] { 215.5f + 217 + 4 }, Place(215.5f, 158));
        Assert.Equal(new[] { 400f - 4 - 158 }, Place(400f, 158));
        // Two boxes: one on each side when both fit, else both on one side.
        Assert.Equal(new[] { 215.5f - 4 - 158, 215.5f + 217 + 4 }, Place(215.5f, 158, 158));
        Assert.Equal(new[] { 5f + 217 + 4, 5f + 217 + 4 + 158 + 4 }, Place(5f, 158, 158));
        Assert.Equal(new[] { 426f - 4 - 158 - 4 - 158, 426f - 4 - 158 }, Place(426f, 158, 158));
        // A narrow screen: no room, the game's own place.
        Assert.Null(BesideLayout.Place(100, 317, 0, 420, 4, new[] { 158f, 158f }));
    }
}

internal static class SessionReportTestExtensions
{
    /// <summary>An error SkulInsight's Guard caught (what CrashReporter.Caught writes).</summary>
    public static void Caught(this SessionReport report, string where, Blame? blame) =>
        report.Error("Caught", where + ": boom", "DamageInsight.Patches.Guard.Run ()\n", blame, where);
}

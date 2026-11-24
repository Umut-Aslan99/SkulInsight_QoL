using System.Linq;
using DamageInsight.Coop.Net;
using DamageInsight.UI;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// `--skulinsight-window-test` (launcher copies only, COOP_LOG 66): the Co-op window opens once the session has two
/// players in a loaded room; the client asks for more enemy health, says OK, the host raises it, the client asks for
/// less. Every game takes a picture after each step (CoopShots.Extra, so `--skulinsight-shots` must be on) and logs
/// what its notes say about everyone, so the pictures and the logs of the copies prove the new PlayerNote message and
/// the window together without a Steam lobby.
/// </summary>
internal sealed class CoopWindowTest : MonoBehaviour
{
    private float _start = -1f;
    private int _step;

    /// <summary>(seconds after the start, host or client or both, what to do). Press: 1 = up, -1 = down, 0 = OK.</summary>
    private static readonly (float At, string Who, string What)[] Steps =
    {
        (3f, "client", "up"),
        (6f, "both", "shot 1-request"),
        (8f, "client", "ok"),
        (11f, "both", "shot 2-ok"),
        (13f, "host", "up"),
        (17f, "both", "shot 3-raised"),
        (18f, "client", "down"),
        (21f, "both", "shot 4-less"),
        (22f, "both", "notice"),
        (24f, "both", "shot 5-notice"),
        (25f, "client", "update"),
        (27f, "client", "shot 6-update"),
        (29f, "host", "report"),
        (29f, "client", "runlogs"),
        (32f, "both", "shot 7-forms"),
    };

    private void Awake() => CoopWindow.LauncherTest = true;

    private void Update()
    {
        Patches.Guard.Run("Co-op window test", () =>
        {
            var session = CoopRuntime.Instance?.Session;
            if (session == null || session.State != SessionState.Active || _step >= Steps.Length)
                return;
            if (_start < 0f)
            {
                // 3-4 copies: everyone in (start-instances passes --skulinsight-autorun-players, COOP_LOG 70)
                if (session.Roster.Count < CoopLaunch.Current.AutoRunPlayers || CoopWorld.Current?.RoomLoaded != true)
                    return;
                _start = Time.unscaledTime;
                CoopWindow.Open();
                Plugin.Log.LogInfo("[CoopTest] window test: the Co-op window is open");
                return;
            }
            var (at, who, what) = Steps[_step];
            if (Time.unscaledTime - _start < at)
                return;
            _step++;
            string side = session.IsHost ? "host" : "client";
            if (who != "both" && who != side)
                return;
            if (what.StartsWith("shot "))
                CoopShots.Extra("window-" + what.Substring(5));
            else if (what == "runlogs")
                Crash.RunLogs.TestAsk();
            else if (what == "report")
                ReportWindow.TestShow();
            else if (what == "update")
                UpdateNotice.TestShow("0.17.9");
            else if (what == "notice")
                CoopWindow.TestMatchInfo = Lang.Loc.T("Couldn't match with the players searching now: your mod lists differ. Consider running only BepInEx and SkulInsight QoL.");
            else
                CoopWindow.TestPress(what == "up" ? 1 : what == "down" ? -1 : 0);
            Report(session, what);
        });
    }

    private static void Report(CoopSession session, string what)
    {
        var notes = CoopPlayerNotes.Current;
        if (notes == null)
        {
            Plugin.Log.LogWarning("[CoopTest] window test: " + what + ": no notes");
            return;
        }
        var slots = session.Roster.Select(p => p.Slot).ToList();
        var least = notes.Notes.LeastProgress(slots, session.LocalSlot);
        string players = string.Join("; ", session.Roster.Select(p =>
        {
            var note = notes.Notes.NoteOf(p.Slot, session.LocalSlot);
            return $"slot {p.Slot} {(p.IsHost ? "host" : "client")}: " + (note is { } n
                ? $"cleared {n.ClearedLevel}, request {notes.Notes.RequestOf(p.Slot, session.LocalSlot)}, ok {notes.Notes.IsOk(p.Slot, session.LocalSlot)}, voted on {n.VotedOn}"
                : "no note") + (least.Contains(p.Slot) ? ", least progress" : "");
        }));
        Plugin.Log.LogInfo($"[CoopTest] window test: {what}: host value {notes.Notes.HostValue}; {players}");
    }
}

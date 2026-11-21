using System;
using System.Collections.Generic;
using System.Linq;
using DamageInsight.Coop.Net;
using HarmonyLib;

namespace DamageInsight.Coop;

/// <summary>
/// The host's world for everyone (Umut 2026-11-16 / 11-20, COOP_LOG 61 (2), 62 findings 6, 8, 9: "the save state
/// differences matter a lot"). Much of a room is built from the player's own save: whether a first-time scene plays
/// (<c>Progress.cutscene</c>: which adventurer party comes, the Veteran Hero the first time; Chimera's and the Pope's
/// first intros; Arachne; the Black Market cat; the Dark Mirror machines' intros), which Darktech machines stand in the
/// Black Market (<c>HardmodeProgress.unlocked / activated</c>), which special rooms and road NPCs were met, which castle
/// NPCs were rescued. Two saves that differ built two different rooms: the host fought the Veteran Hero while the
/// client's same enemy id was the Veteran Warrior (Kerkopi 11-20: ~200 host moves the client couldn't play).
/// <para>
/// [DECISION] 2026-11-20 (until the co-op slot of COOP_PLAN 4.9): a client borrows the host's flags in memory for the
/// session, like <see cref="DarkMirrorLevel"/>. The host sends its set when a player joins and whenever it changes
/// (MessageType.HostWorld). The client's save never gets them: around every save of these flags its own values are
/// put back for the moment of writing (<see cref="OwnForSave"/>), and a flag the game changed here during the session
/// (a scene this player saw for the first time) counts as its own. Everything is the player's own again when the
/// session ends.
/// </para>
/// </summary>
// REVIEW(fable): host -> client message + save data borrowed in memory (REVIEW_QUEUE: Steam / save data trigger).
internal sealed class HostWorld
{
    public static HostWorld Current { get; private set; }

    /// <summary>How often the host looks whether its flags changed (a scene seen, a machine unlocked).</summary>
    public const float CheckSeconds = 3f;

    private readonly CoopSession _session;
    private readonly NetWriter _writer = new(2048);
    private string _sentKey = "";
    private double _nextCheck;

    /// <summary>Client: each borrowed flag's own value and the value it was given here (the host's).</summary>
    private static readonly Dictionary<Data.BoolData, (bool own, bool given)> Borrowed = new();

    /// <summary>Client: how many flags the host's world changed here (for the session report / the window).</summary>
    public static int Differing { get; private set; }

    public HostWorld(CoopSession session)
    {
        _session = session;
        Current = this;
        session.RegisterHandler(MessageType.HostWorld, OnMessage);
        if (session.IsHost)
            session.PlayerJoined += OnJoined;
    }

    public void Dispose()
    {
        _session.PlayerJoined -= OnJoined;
        Restore();
        if (Current == this)
            Current = null;
    }

    // ------------------------------------------------------------------ the flags

    /// <summary>The world flags of this game's save, by a name that is the same on every game.</summary>
    internal static IEnumerable<(string name, Data.BoolData data)> Flags()
    {
        foreach (var pair in Data.GameData.Progress.cutscene)
            yield return ("cutscene/" + pair.Key, pair.Value);
        foreach (var pair in Data.GameData.Progress.specialMapEncountered)
            yield return ("special-map/" + pair.Key, pair.Value);
        foreach (var pair in Data.GameData.Progress.fieldNpcEncountered)
            yield return ("field-npc/" + pair.Key, pair.Value);
        foreach (var pair in Data.GameData.HardmodeProgress.unlocked)
            yield return ("darktech-unlocked/" + pair.Key, pair.Value);
        foreach (var pair in Data.GameData.HardmodeProgress.activated)
            yield return ("darktech-activated/" + pair.Key, pair.Value);
        var progress = Data.GameData.Progress.instance;
        yield return ("rescued/fox", progress._foxRescued);
        yield return ("rescued/ogre", progress._ogreRescued);
        yield return ("rescued/druid", progress._druidRescued);
        yield return ("rescued/deathknight", progress._deathknightRescued);
        yield return ("arachne-tutorial", progress._arachneTutorial);
    }

    /// <summary>The names of the flags that are set on this game (what the host sends).</summary>
    internal static List<string> SetNames() =>
        Flags().Where(f => f.data != null && f.data.value).Select(f => f.name).OrderBy(n => n, StringComparer.Ordinal).ToList();

    // ------------------------------------------------------------------ host

    private void OnJoined(RosterEntry player) => Send(player.Slot, SetNames());

    /// <summary>Host: every few seconds, whether a flag changed (sent to everyone then).</summary>
    public void Tick(double now)
    {
        if (!_session.IsHost || now < _nextCheck)
            return;
        _nextCheck = now + CheckSeconds;
        var names = SetNames();
        string key = string.Join("|", names);
        if (key == _sentKey)
            return;
        foreach (var player in _session.Roster)
            if (player.Slot != _session.LocalSlot)
                Send(player.Slot, names);
    }

    private void Send(int slot, List<string> names)
    {
        _sentKey = string.Join("|", names);
        _writer.Reset();
        HostWorldMessages.Write(_writer, names);
        _session.Send(slot, MessageType.HostWorld, _writer, Delivery.Reliable);
        CoopTrace.Write("host-world-sent", what: names.Count + " set", slot: slot);
    }

    // ------------------------------------------------------------------ client

    private void OnMessage(GameMessage message, NetReader payload)
    {
        if (_session.IsHost || message.FromSlot != 0)
            return;
        if (!HostWorldMessages.TryRead(payload, out var names))
        {
            Plugin.Log.LogWarning("[Coop] the host's world flags could not be read");
            return;
        }
        _hostSet = new HashSet<string>(names, StringComparer.Ordinal);
        Apply(_hostSet);
    }

    /// <summary>Client: the host's last set (applied again when the game may have read its save anew).</summary>
    private static HashSet<string> _hostSet;

    /// <summary>
    /// Client: the host's flags again, before a run starts, a catch-up jump or in a castle. A session can start on the
    /// title screen (the test copies, a Steam join before the save is loaded): loading the game reads the save's
    /// own flags back over the borrowed ones (bot run 2026-11-20 22:24: the client's Dark Mirror adventurer room was
    /// the first-time one, the host's the normal one).
    /// </summary>
    internal static void Reapply()
    {
        if (_hostSet == null || Current == null || Current._session.IsHost)
            return;
        Apply(_hostSet);
    }

    /// <summary>Client: the host's flags in this game's memory (its own kept for saves and the end).</summary>
    internal static void Apply(HashSet<string> hostSet)
    {
        var changed = new List<string>();
        int differing = 0;
        foreach (var (name, data) in Flags())
        {
            if (data == null)
                continue;
            bool host = hostSet.Contains(name);
            bool own;
            if (Borrowed.TryGetValue(data, out var b))
                own = data.value != b.given ? data.value : b.own; // the game set it here meanwhile: this player's own
            else
                own = data.value;
            Borrowed[data] = (own, host);
            if (own != host)
                differing++;
            if (data.value != host)
            {
                changed.Add(name + (host ? "+" : "-"));
                data.value = host;
            }
        }
        Differing = differing;
        CoopTrace.Write("host-world", what: $"{differing} differ from this save; changed now {changed.Count}: "
                                           + string.Join(", ", changed.Take(12)) + (changed.Count > 12 ? ", ..." : ""));
        if (changed.Count > 0)
            Plugin.Log.LogInfo($"[Coop] the host's world: {changed.Count} flag(s) of this save differ for the session ({string.Join(", ", changed.Take(8))})");
    }

    /// <summary>Client: this game's own flags again (the session ended).</summary>
    internal static void Restore()
    {
        _hostSet = null;
        if (Borrowed.Count == 0)
            return;
        int back = 0;
        foreach (var pair in Borrowed)
        {
            var data = pair.Key;
            if (data == null)
                continue;
            bool own = data.value != pair.Value.given ? data.value : pair.Value.own;
            if (data.value != own)
            {
                data.value = own;
                back++;
            }
        }
        Borrowed.Clear();
        Differing = 0;
        CoopTrace.Write("host-world-restored", amount: back);
        if (back > 0)
            Plugin.Log.LogInfo($"[Coop] this save's own world flags again ({back} back)");
    }

    // ------------------------------------------------------------------ saves write the player's own flags

    /// <summary>
    /// Around a save of these flags: before, each borrowed flag holds this player's own value (a flag the game changed
    /// during the session is its own now); after, the host's again. True if anything was swapped.
    /// </summary>
    internal static bool OwnForSave()
    {
        if (Borrowed.Count == 0)
            return false;
        foreach (var data in Borrowed.Keys.ToList())
        {
            if (data == null)
                continue;
            var b = Borrowed[data];
            bool own = data.value != b.given ? data.value : b.own;
            bool given = data.value; // what this game shows now (the host's, or what the game set since)
            Borrowed[data] = (own, given);
            data.value = own;
        }
        return true;
    }

    internal static void HostsAfterSave()
    {
        foreach (var pair in Borrowed)
            if (pair.Key != null)
                pair.Key.value = pair.Value.given;
    }

    [HarmonyPatch(typeof(Data.GameData.Progress), nameof(Data.GameData.Progress.SaveAll))]
    public static class ProgressSave
    {
        private static void Prefix(out bool __state) => __state = OwnForSave();

        private static void Finalizer(bool __state)
        {
            if (__state)
                HostsAfterSave();
        }
    }

    [HarmonyPatch(typeof(Data.GameData.HardmodeProgress), nameof(Data.GameData.HardmodeProgress.SaveAll))]
    public static class HardmodeSave
    {
        private static void Prefix(out bool __state) => __state = OwnForSave();

        private static void Finalizer(bool __state)
        {
            if (__state)
                HostsAfterSave();
        }
    }

    /// <summary>The story ending's guide saves the scenes directly (cutscene.SaveAll).</summary>
    [HarmonyPatch(typeof(CutScenes.Shots.Events.Customs.OpenStoryEndingGuide), nameof(CutScenes.Shots.Events.Customs.OpenStoryEndingGuide.Run))]
    public static class StoryEndingSave
    {
        private static void Prefix(out bool __state) => __state = OwnForSave();

        private static void Finalizer(bool __state)
        {
            if (__state)
                HostsAfterSave();
        }
    }
}

/// <summary>The host's world flags on the wire: the names of the set ones.</summary>
public static class HostWorldMessages
{
    public const int MaxNames = 1024, MaxName = 96;

    public static void Write(NetWriter w, IReadOnlyCollection<string> names)
    {
        int count = Math.Min(names.Count, MaxNames);
        w.WriteUInt16((ushort)count);
        foreach (var name in names.Take(count))
            w.WriteString(name ?? "", MaxName);
    }

    public static bool TryRead(NetReader r, out List<string> names)
    {
        names = new List<string>();
        try
        {
            int count = r.ReadUInt16();
            if (count > MaxNames)
                return false;
            for (int i = 0; i < count; i++)
                names.Add(r.ReadString(MaxName));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

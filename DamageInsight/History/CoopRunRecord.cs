using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DamageInsight.Describe;
using DamageInsight.Recording;

namespace DamageInsight.History;

/// <summary>
/// The kinds of <see cref="CoopEvent"/> (docs/COOP_PLAN.md §4.11). Stored in the run files: never rename. Players are
/// slots 0-3 (the session roster's; a slot names one player for the whole run), -1 = nobody.
/// </summary>
public static class CoopEvents
{
    public const string Room = "room";        // the party entered a room (Id = map name, Name = stage name, Other = chapter, OtherName = map type, Value = stage)
    public const string Door = "door";        // a door vote resolved: Votes (by slot), Door, Flip, TimedOut, Chance, Doors
    public const string Reward = "reward";    // Player took their room reward (Id/Name = gear, How = its kind, Value = gold or bones)
    public const string Shop = "shop";        // Player bought Id/Name for Value gold (How = "shop", "blackmarket")
    public const string Upgrade = "upgrade";  // Player upgraded skull Other into Id (Value = bones spent)
    public const string Down = "down";        // Player went down (Id/Name = who hit last; StaysDown = was revived in this room already)
    public const string Revive = "revive";    // By revived Player (Health = health fraction back)
    public const string Back = "back";        // the room was cleared: Player came back (Health)
    public const string Boss = "boss";        // a boss fight won: Id/Name = boss, Value = fight seconds, Shares per player
    public const string Join = "join";        // Player joined the running run (late join)
    public const string Left = "left";        // Player left the run (How = "quit", "timeout", "kicked")
    public const string Rejoin = "rejoin";    // Player came back to the run (reconnect)
    public const string End = "end";          // the run ended for the party (How = the RunResult name)
}

/// <summary>A player's numbers in a boss fight (the boss event): damage dealt to the boss, damage taken, flawless.</summary>
public sealed class CoopShare
{
    public int Player = -1;
    public double Dealt, Taken;
    public int Hits;
    /// <summary>Lost no health in the fight.</summary>
    public bool Flawless;
}

/// <summary>
/// Something that happened to the party (docs/COOP_PLAN.md §4.11): a room, a door vote and its coin flip, a reward, a
/// shop buy, a skull upgrade, a down, a revive, a boss fight, a player leaving or coming back, the end. Each game
/// records the ones it knows; <see cref="Key"/> is the same on every game, so merged copies count once. The thing it
/// is about uses the solo timeline's fields (<see cref="RunEvent"/>: Id, Name, Other, OtherName, Value).
/// </summary>
public sealed class CoopEvent
{
    /// <summary>The stable id merges dedupe by: kind, room, player and a count ("revive-4-p1-1", "door-3-1").</summary>
    public string Key = "";
    /// <summary>Whole seconds since the co-op run started (the party's clock, the same on every game).</summary>
    public int T;
    /// <summary>The room's index in the run (0 = the first room after the castle).</summary>
    public int Room;
    /// <summary>One of <see cref="CoopEvents"/>.</summary>
    public string Kind = "";
    /// <summary>The slot it is about (who voted, took, went down, was revived, left); -1 for the party.</summary>
    public int Player = -1;
    /// <summary>A second slot: who revived <see cref="Player"/>; -1 when none.</summary>
    public int By = -1;
    public string Id = "", Name = "", Other = "", OtherName = "";
    public int Value;
    /// <summary>A kind of its own: the reward's kind, the shop, why a player left, the run's result.</summary>
    public string How = "";

    /// <summary>Doors: the door the party went through (0 = the game's first gate), each slot's vote (-1 = none: didn't
    /// vote, down or gone), a coin flip between different votes, the 10 s ran out, the chosen door's odds, what each
    /// door offered (optional).</summary>
    public int Door = -1;
    public int[] Votes;
    public bool Flip, TimedOut;
    public double Chance;
    public string[] Doors;

    /// <summary>Downs: revived in this room already, so the player stays down until the room is cleared.</summary>
    public bool StaysDown;
    /// <summary>Revives and coming back: the health given back (a fraction of the maximum).</summary>
    public double Health;

    /// <summary>Boss fights: each player's numbers.</summary>
    public List<CoopShare> Shares;

    /// <summary>The slot's vote, or -1.</summary>
    public int VoteOf(int slot) => Votes != null && slot >= 0 && slot < Votes.Length ? Votes[slot] : -1;

    /// <summary>The key a newly recorded event gets: kind, room, player and its number among the same (1 = the first).</summary>
    public static string KeyFor(string kind, int room, int player, int number) =>
        kind + "-" + room.ToString(CultureInfo.InvariantCulture) +
        (player >= 0 ? "-p" + player.ToString(CultureInfo.InvariantCulture) : "") + "-" + number.ToString(CultureInfo.InvariantCulture);

    // ------------------------------------------------------------------ made from the rules' outputs (Coop/Rules)

    /// <summary>A resolved door vote (the rules' DoorVoteResult).</summary>
    public static CoopEvent DoorResult(int room, int t, int[] votes, int door, bool flip, bool timedOut, double chance, string[] doors = null) =>
        new() { Kind = CoopEvents.Door, Room = room, T = t, Votes = votes?.ToArray(), Door = door, Flip = flip, TimedOut = timedOut, Chance = chance, Doors = doors?.ToArray() };

    /// <summary>A player went down (the rules' ReviveEvent Downed; staysDown = !CanBeRevived).</summary>
    public static CoopEvent Downed(int room, int t, int slot, bool staysDown, string killerId = "", string killer = "") =>
        new() { Kind = CoopEvents.Down, Room = room, T = t, Player = slot, StaysDown = staysDown, Id = killerId ?? "", Name = killer ?? "" };

    /// <summary>A teammate revived a player (ReviveEvent Revived: Slot, BySlot, HealthFraction).</summary>
    public static CoopEvent Revived(int room, int t, int slot, int by, double health) =>
        new() { Kind = CoopEvents.Revive, Room = room, T = t, Player = slot, By = by, Health = health };

    /// <summary>The room was cleared and a down player came back (ReviveEvent ReturnedAtClear).</summary>
    public static CoopEvent Returned(int room, int t, int slot, double health) =>
        new() { Kind = CoopEvents.Back, Room = room, T = t, Player = slot, Health = health };

    /// <summary>The run ended for the party (ReviveEvent RunOver: everyone down = a death).</summary>
    public static CoopEvent RunEnd(int room, int t, RunResult result) =>
        new() { Kind = CoopEvents.End, Room = room, T = t, How = result.ToString() };

    /// <summary>The party entered a room.</summary>
    public static CoopEvent Entered(int room, int t, string chapter, int stage, string stageName, string map, string type) =>
        new() { Kind = CoopEvents.Room, Room = room, T = t, Id = map ?? "", Name = stageName ?? "", Other = chapter ?? "", OtherName = type ?? "", Value = stage };

    // ------------------------------------------------------------------ JSON

    internal string ToJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"key\":").Append(Q(Key)).Append(",\"t\":").Append(T).Append(",\"room\":").Append(Room).Append(",\"kind\":").Append(Q(Kind));
        if (Player >= 0)
            sb.Append(",\"p\":").Append(Player);
        if (By >= 0)
            sb.Append(",\"by\":").Append(By);
        if (Id.Length > 0)
            sb.Append(",\"id\":").Append(Q(Id));
        if (Name.Length > 0)
            sb.Append(",\"name\":").Append(Q(Name));
        if (Other.Length > 0)
            sb.Append(",\"other\":").Append(Q(Other));
        if (OtherName.Length > 0)
            sb.Append(",\"otherName\":").Append(Q(OtherName));
        if (Value != 0)
            sb.Append(",\"value\":").Append(Value);
        if (How.Length > 0)
            sb.Append(",\"how\":").Append(Q(How));
        if (Door >= 0)
            sb.Append(",\"door\":").Append(Door);
        if (Votes != null)
            sb.Append(",\"votes\":[").Append(string.Join(",", Votes.Select(v => v.ToString(CultureInfo.InvariantCulture)))).Append(']');
        if (Flip)
            sb.Append(",\"flip\":true");
        if (TimedOut)
            sb.Append(",\"timedOut\":true");
        if (Chance != 0)
            sb.Append(",\"chance\":").Append(N(Chance));
        if (Doors != null)
            sb.Append(",\"doors\":[").Append(string.Join(",", Doors.Select(Q))).Append(']');
        if (StaysDown)
            sb.Append(",\"staysDown\":true");
        if (Health != 0)
            sb.Append(",\"health\":").Append(N(Health));
        if (Shares != null)
            sb.Append(",\"shares\":[").Append(string.Join(",", Shares.OrderBy(s => s.Player).Select(s =>
                "{\"p\":" + s.Player + ",\"dealt\":" + N(s.Dealt) + ",\"taken\":" + N(s.Taken) + ",\"hits\":" + s.Hits +
                ",\"flawless\":" + (s.Flawless ? "true" : "false") + "}"))).Append(']');
        return sb.Append('}').ToString();
    }

    internal static CoopEvent FromNode(Node n)
    {
        var e = new CoopEvent
        {
            Key = n.Str("key") ?? "", T = (int)n.Num("t"), Room = (int)n.Num("room"), Kind = n.Str("kind") ?? "",
            Player = (int)n.Num("p", -1), By = (int)n.Num("by", -1),
            Id = n.Str("id") ?? "", Name = n.Str("name") ?? "", Other = n.Str("other") ?? "", OtherName = n.Str("otherName") ?? "",
            Value = (int)n.Num("value"), How = n.Str("how") ?? "", Door = (int)n.Num("door", -1),
            Flip = n.Bool("flip"), TimedOut = n.Bool("timedOut"), Chance = n.Num("chance"),
            StaysDown = n.Bool("staysDown"), Health = n.Num("health"),
        };
        if (n.Raw("votes") is List<object> votes)
            e.Votes = votes.Select(v => v is double d ? (int)d : -1).ToArray();
        if (n.Raw("doors") is List<object> doors)
            e.Doors = doors.Select(v => v as string ?? "").ToArray();
        if (n.Has("shares"))
            e.Shares = n.List("shares").Select(s => new CoopShare
            {
                Player = (int)s.Num("p", -1), Dealt = s.Num("dealt"), Taken = s.Num("taken"), Hits = (int)s.Num("hits"), Flawless = s.Bool("flawless"),
            }).ToList();
        return e;
    }

    private static string Q(string s) => LogJson.Quote(s ?? "");
    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// A player of a co-op run: their slot (0-3, the host is usually 0), the random player id (the daily runs' one), the
/// name and colour the others saw, when they joined and left (seconds since the run started; -1 = never left), and the
/// skulls they held at the end (held first: what the run list shows without reading the parts).
/// </summary>
public sealed class CoopPlayer
{
    public int Slot;
    public string PlayerId = "";
    public string Name = "";
    /// <summary>The player's colour in the party (0-3; the UI's colour list).</summary>
    public int Color;
    public bool Host;
    public int Joined;
    /// <summary>Left the run for good (seconds since its start, and the room), -1 while in (or back after a rejoin).</summary>
    public int Left = -1, LeftRoom = -1;
    /// <summary>The skulls at the end (or when they left), held first: Codex ids.</summary>
    public readonly List<string> Skulls = new();

    public bool HasLeft => Left >= 0;

    internal string ToJson() =>
        "{\"slot\":" + Slot + ",\"id\":" + Q(PlayerId) + ",\"name\":" + Q(Name) + ",\"color\":" + Color +
        ",\"host\":" + (Host ? "true" : "false") + ",\"joined\":" + Joined + ",\"left\":" + Left + ",\"leftRoom\":" + LeftRoom +
        ",\"skulls\":[" + string.Join(",", Skulls.Select(Q)) + "]}";

    internal static CoopPlayer FromNode(Node n)
    {
        var p = new CoopPlayer
        {
            Slot = (int)n.Num("slot"), PlayerId = n.Str("id") ?? "", Name = n.Str("name") ?? "", Color = (int)n.Num("color"),
            Host = n.Bool("host"), Joined = (int)n.Num("joined"), Left = (int)n.Num("left", -1), LeftRoom = (int)n.Num("leftRoom", -1),
        };
        if (n.Raw("skulls") is List<object> skulls)
            p.Skulls.AddRange(skulls.OfType<string>());
        return p;
    }

    private static string Q(string s) => LogJson.Quote(s ?? "");
}

/// <summary>A player's numbers in one room of the party (by room index): what their own game counted.</summary>
public sealed class CoopRoom
{
    public int Room;
    /// <summary>The health their hits took from enemies, the health they lost, hits taken, kills.</summary>
    public double Dealt, Taken;
    public int Hits, Kills;
}

/// <summary>
/// One player's own part of a co-op run, as their game recorded it: the solo run shape (<see cref="RunRecord"/>: build,
/// damage, timeline, numbers) plus their numbers per party room. <see cref="Version"/> grows every time their game
/// sends it (at each room change and at the end); a merge keeps the newest. A player who left keeps the part they sent
/// last (the run up to that room).
/// </summary>
public sealed class CoopPart
{
    public int Slot;
    public string PlayerId = "";
    public string Name = "";
    public int Version;
    public RunRecord Run = new();
    public readonly List<CoopRoom> Rooms = new();

    /// <summary>The player's numbers in a room, made when missing.</summary>
    public CoopRoom RoomOf(int room)
    {
        var r = Rooms.FirstOrDefault(x => x.Room == room);
        if (r == null)
        {
            Rooms.Add(r = new CoopRoom { Room = room });
            Rooms.Sort((a, b) => a.Room.CompareTo(b.Room));
        }
        return r;
    }

    /// <summary>The part for the network (and for teammates' files): the run without its co-op data.</summary>
    public string ToJson() => ToJson(withRun: true);

    internal string ToJson(bool withRun)
    {
        var sb = new StringBuilder();
        sb.Append("{\"slot\":").Append(Slot).Append(",\"id\":").Append(Q(PlayerId)).Append(",\"name\":").Append(Q(Name))
          .Append(",\"version\":").Append(Version);
        sb.Append(",\"rooms\":[").Append(string.Join(",", Rooms.OrderBy(r => r.Room).Select(r =>
            "{\"room\":" + r.Room + ",\"dealt\":" + N(r.Dealt) + ",\"taken\":" + N(r.Taken) + ",\"hits\":" + r.Hits + ",\"kills\":" + r.Kills + "}"))).Append(']');
        if (withRun && Run != null)
            sb.Append(",\"run\":").Append(Run.ToJson(withCoop: false).Trim());
        return sb.Append('}').ToString();
    }

    public static CoopPart FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try { return FromNode(GearDoc.ParseNode(json)); }
        catch (Exception) { return null; }
    }

    internal static CoopPart FromNode(Node n)
    {
        if (n.IsNull)
            return null;
        var part = new CoopPart
        {
            Slot = (int)n.Num("slot"), PlayerId = n.Str("id") ?? "", Name = n.Str("name") ?? "", Version = (int)n.Num("version"),
            Run = RunRecord.FromNode(n.Child("run")),
        };
        foreach (var r in n.List("rooms"))
            part.Rooms.Add(new CoopRoom { Room = (int)r.Num("room"), Dealt = r.Num("dealt"), Taken = r.Num("taken"), Hits = (int)r.Num("hits"), Kills = (int)r.Num("kills") });
        if (part.Run != null)
            part.Run.Coop = null;
        return part;
    }

    private static string Q(string s) => LogJson.Quote(s ?? "");
    private static string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// A co-op run (docs/COOP_PLAN.md §4.11, "a new multiplayer look"): the party, every player's own part and the party
/// events in time order. Saved in the run history like a solo run: the file is the local player's own
/// <see cref="RunRecord"/> (so every solo page, the index and the records keep working) with this in its "coop" field;
/// the local part's run is that record itself (<see cref="Self"/>) and isn't written twice. The games send each other
/// their parts and events at each room change and at the end; <see cref="Merge"/> and <see cref="MergePart"/> join
/// them: idempotent, in any order, the newest part of each player, every event once. Plain data, no Unity.
/// </summary>
public sealed class CoopRunRecord
{
    public const int FormatVersion = 1;

    /// <summary>The co-op run's id, the same on every game (the host's start: <see cref="RunRecord.IdFor"/>).</summary>
    public string Id = "";
    public int Seed;
    public DateTime Started, Ended;
    /// <summary>The party's result (from the end event); each part keeps its player's own.</summary>
    public RunResult Result;
    public bool DarkMirror;
    public int Level;
    /// <summary>This game's slot (its part is the run file itself); -1 for a record that isn't any game's own.</summary>
    public int Self = -1;
    /// <summary>The highest room index the party reached.</summary>
    public int Rooms;

    public readonly List<CoopPlayer> Players = new();
    /// <summary>Every player's part by slot (the local one too: its run is the run file's record).</summary>
    public readonly Dictionary<int, CoopPart> Parts = new();
    /// <summary>In time order (then room, then key).</summary>
    public readonly List<CoopEvent> Events = new();

    /// <summary>A shrunk or summarised record: the details (parts' timelines, events) are gone.</summary>
    public bool Shrunk;

    public CoopPlayer Player(int slot) => Players.FirstOrDefault(p => p.Slot == slot);

    public CoopPart Part(int slot) => Parts.TryGetValue(slot, out var p) ? p : null;

    /// <summary>A player's name for the pages: the party's, else their part's, else "Player 2".</summary>
    public string NameOf(int slot)
    {
        string name = Player(slot)?.Name;
        if (string.IsNullOrEmpty(name))
            name = Part(slot)?.Name;
        return string.IsNullOrEmpty(name) ? "Player " + (slot + 1).ToString(CultureInfo.InvariantCulture) : name;
    }

    // ------------------------------------------------------------------ recording

    /// <summary>
    /// Records an event this game knows: it gets its key (kind, room, player, its number among the same) unless it has
    /// one. An event with a key already known is not added twice. Returns the event kept.
    /// </summary>
    public CoopEvent Add(CoopEvent e)
    {
        if (e.Key.Length == 0)
        {
            int number = 1 + Events.Count(x => x.Kind == e.Kind && x.Room == e.Room && x.Player == e.Player);
            e.Key = CoopEvent.KeyFor(e.Kind, e.Room, e.Player, number);
        }
        MergeEvents(new[] { e });
        return Events.First(x => x.Key == e.Key);
    }

    /// <summary>A player joins the party (the host's roster); an existing slot is updated.</summary>
    public CoopPlayer AddPlayer(int slot, string playerId, string name, int color, bool host, int joined = 0)
    {
        MergePlayers(new[] { new CoopPlayer { Slot = slot, PlayerId = playerId ?? "", Name = name ?? "", Color = color, Host = host, Joined = joined } });
        return Player(slot);
    }

    /// <summary>
    /// The local part to send: a copy of <paramref name="own"/> (the run being recorded) without its co-op data, with
    /// the next version. The local part in <see cref="Parts"/> takes the same version.
    /// </summary>
    public CoopPart OwnPart(RunRecord own)
    {
        var mine = Part(Self) ?? (Parts[Self] = new CoopPart { Slot = Self, PlayerId = Player(Self)?.PlayerId ?? "", Name = Player(Self)?.Name ?? "" });
        mine.Run = own;
        mine.Version++;
        var copy = CoopPart.FromJson(mine.ToJson());
        return copy;
    }

    // ------------------------------------------------------------------ merging

    /// <summary>
    /// Takes another game's part: kept when newer (higher version; the same version: the same choice on every game).
    /// The local part is never replaced (this game records it). A part of a player the party doesn't list yet adds them.
    /// Returns whether anything changed.
    /// </summary>
    public bool MergePart(CoopPart part)
    {
        if (part == null || (Self >= 0 && part.Slot == Self))
            return false;
        // A stub for a player the party doesn't list yet (join time unknown: the roster's entry wins over it).
        bool changed = MergePlayers(new[] { new CoopPlayer { Slot = part.Slot, PlayerId = part.PlayerId, Name = part.Name, Color = part.Slot, Joined = -1 } });
        var old = Part(part.Slot);
        if (old != null && !Newer(part, old))
            return changed;
        var copy = CoopPart.FromJson(part.ToJson()); // our own copy: the sender's object stays theirs
        if (copy == null)
            return changed;
        Parts[part.Slot] = copy;
        Normalize();
        return true;
    }

    /// <summary>Takes events: each key once; when two games kept different versions of one, the fuller one wins.</summary>
    public bool MergeEvents(IEnumerable<CoopEvent> events)
    {
        bool changed = false;
        foreach (var e in events ?? Enumerable.Empty<CoopEvent>())
        {
            if (e == null || e.Key.Length == 0)
                continue;
            int at = Events.FindIndex(x => x.Key == e.Key);
            if (at >= 0)
            {
                if (!BetterEvent(e, Events[at]))
                    continue;
                Events[at] = CoopEvent.FromNode(GearDoc.ParseNode(e.ToJson()));
            }
            else
            {
                Events.Add(CoopEvent.FromNode(GearDoc.ParseNode(e.ToJson())));
            }
            changed = true;
        }
        if (changed)
            Normalize();
        return changed;
    }

    /// <summary>Takes players: by slot; the earliest join; the fuller entry's name, id and colour.</summary>
    public bool MergePlayers(IEnumerable<CoopPlayer> players)
    {
        bool changed = false;
        foreach (var p in players ?? Enumerable.Empty<CoopPlayer>())
        {
            var old = Player(p.Slot);
            if (old == null)
            {
                var copy = CoopPlayer.FromNode(GearDoc.ParseNode(p.ToJson()));
                Players.Add(copy);
                Players.Sort((a, b) => a.Slot.CompareTo(b.Slot));
                changed = true;
                continue;
            }
            // The fuller entry's facts, the earliest join, host if either says so: the same whichever came first.
            var keep = Fuller(p, old) ? p : old;
            string before = old.ToJson();
            var skulls = (keep.Skulls.Count > 0 ? keep.Skulls : (ReferenceEquals(keep, p) ? old : p).Skulls).ToList();
            int joined = p.Joined < 0 ? old.Joined : old.Joined < 0 ? p.Joined : Math.Min(p.Joined, old.Joined);
            bool host = p.Host || old.Host;
            (old.PlayerId, old.Name, old.Color, old.Left, old.LeftRoom) = (keep.PlayerId, keep.Name, keep.Color, keep.Left, keep.LeftRoom);
            old.Skulls.Clear();
            old.Skulls.AddRange(skulls);
            old.Joined = joined;
            old.Host = host;
            changed |= old.ToJson() != before;
        }
        return changed;
    }

    /// <summary>
    /// Takes everything another game's record knows (the same run only: false for another id): run facts, players,
    /// parts (theirs too: their own part is the run of their file) and events. Idempotent, in any order.
    /// </summary>
    public bool Merge(CoopRunRecord other, RunRecord otherOwn = null)
    {
        if (other == null || (Id.Length > 0 && other.Id.Length > 0 && other.Id != Id))
            return false;
        if (Id.Length == 0)
            Id = other.Id;
        Seed = Seed == 0 ? other.Seed : other.Seed == 0 ? Seed : Math.Min(Seed, other.Seed);
        if (other.Started != default && (Started == default || other.Started < Started))
            Started = other.Started;
        if (other.Ended > Ended)
            Ended = other.Ended;
        DarkMirror |= other.DarkMirror;
        Level = Math.Max(Level, other.Level);
        Rooms = Math.Max(Rooms, other.Rooms);
        if (Rank(other.Result) > Rank(Result))
            Result = other.Result;
        MergePlayers(other.Players);
        foreach (var part in other.Parts.Values.ToList())
        {
            if (part.Run == null && other.Self == part.Slot && otherOwn != null)
                MergePart(new CoopPart { Slot = part.Slot, PlayerId = part.PlayerId, Name = part.Name, Version = part.Version, Run = otherOwn });
            else
                MergePart(part);
        }
        MergeEvents(other.Events);
        Normalize();
        return true;
    }

    /// <summary>Puts the derived facts in step: event order, the result and rooms reached, who left, everyone's skulls.</summary>
    public void Normalize()
    {
        Events.Sort((a, b) => a.T != b.T ? a.T.CompareTo(b.T) : a.Room != b.Room ? a.Room.CompareTo(b.Room) : string.CompareOrdinal(a.Key, b.Key));
        if (Events.Count > 0)
            Rooms = Math.Max(Rooms, Events.Max(e => e.Room));
        var end = Events.LastOrDefault(e => e.Kind == CoopEvents.End);
        if (end != null && Enum.TryParse(end.How, out RunResult result))
            Result = result;
        foreach (var p in Players)
        {
            var moves = Events.Where(e => e.Player == p.Slot && e.Kind is CoopEvents.Left or CoopEvents.Rejoin).ToList();
            if (moves.Count > 0)
            {
                var last = moves[moves.Count - 1];
                (p.Left, p.LeftRoom) = last.Kind == CoopEvents.Left ? (last.T, last.Room) : (-1, -1);
            }
            if (Part(p.Slot)?.Run is { } run && run.Build.Any(g => g.Kind == "skull"))
            {
                p.Skulls.Clear();
                p.Skulls.AddRange(run.Build.Where(g => g.Kind == "skull").OrderBy(g => g.Extra.StartsWith("held", StringComparison.Ordinal) ? 0 : 1).Select(g => g.Id));
            }
        }
    }

    private static int Rank(RunResult r) => r == RunResult.InProgress ? 0 : 1 + (int)r;

    private static bool Newer(CoopPart a, CoopPart b) =>
        a.Version != b.Version ? a.Version > b.Version : string.CompareOrdinal(a.ToJson(), b.ToJson()) > 0;

    private static bool BetterEvent(CoopEvent a, CoopEvent b)
    {
        int fa = Facts(a), fb = Facts(b);
        return fa != fb ? fa > fb : string.CompareOrdinal(a.ToJson(), b.ToJson()) > 0;
    }

    private static int Facts(CoopEvent e) => (e.Shares?.Count ?? 0) * 4 + (e.Votes?.Count(v => v >= 0) ?? 0) * 2 + (e.Name.Length > 0 ? 1 : 0);

    private static bool Fuller(CoopPlayer a, CoopPlayer b)
    {
        // A roster entry (join time known) beats a stub made from a part; then a name, then an id.
        static int Facts(CoopPlayer p) => (p.Joined >= 0 ? 4 : 0) + (p.Name.Length > 0 ? 2 : 0) + (p.PlayerId.Length > 0 ? 1 : 0);
        int fa = Facts(a), fb = Facts(b);
        return fa != fb ? fa > fb : string.CompareOrdinal(a.ToJson(), b.ToJson()) > 0;
    }

    // ------------------------------------------------------------------ JSON

    /// <summary>
    /// The record as JSON. With <paramref name="owner"/> (the run file's record) the local part's run is left out: it is
    /// the file itself. Without, every part is written whole (for the network).
    /// </summary>
    public string ToJson(RunRecord owner = null)
    {
        var sb = new StringBuilder();
        sb.Append("{\"format\":").Append(FormatVersion)
          .Append(",\"id\":").Append(Q(Id))
          .Append(",\"seed\":").Append(Seed)
          .Append(",\"started\":").Append(Q(Started == default ? "" : Date(Started)))
          .Append(",\"ended\":").Append(Q(Ended == default ? "" : Date(Ended)))
          .Append(",\"result\":").Append(Q(Result.ToString()))
          .Append(",\"darkMirror\":").Append(DarkMirror ? "true" : "false")
          .Append(",\"level\":").Append(Level)
          .Append(",\"self\":").Append(Self)
          .Append(",\"rooms\":").Append(Rooms)
          .Append(",\"shrunk\":").Append(Shrunk ? "true" : "false");
        sb.Append(",\n\"players\":[").Append(string.Join(",", Players.OrderBy(p => p.Slot).Select(p => "\n " + p.ToJson()))).Append(']');
        sb.Append(",\n\"parts\":[").Append(string.Join(",", Parts.OrderBy(p => p.Key).Select(p =>
            "\n " + p.Value.ToJson(withRun: p.Value.Run != null && !(owner != null && ReferenceEquals(p.Value.Run, owner)))))).Append(']');
        sb.Append(",\n\"events\":[").Append(string.Join(",", Events.Select(e => "\n " + e.ToJson()))).Append("]\n}");
        return sb.ToString();
    }

    /// <summary>Reads a record sent by another game (or written by <see cref="ToJson"/>); null when it isn't one.</summary>
    public static CoopRunRecord FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try { return FromNode(GearDoc.ParseNode(json), null); }
        catch (Exception) { return null; }
    }

    /// <summary>A record from its JSON object; <paramref name="owner"/>: the run file's record (the local part's run).</summary>
    internal static CoopRunRecord FromNode(Node n, RunRecord owner)
    {
        if (n.IsNull || !n.Has("players"))
            return null;
        var c = new CoopRunRecord
        {
            Id = n.Str("id") ?? "",
            Seed = (int)n.Num("seed"),
            Started = ParseDate(n.Str("started")),
            Ended = ParseDate(n.Str("ended")),
            Result = Enum.TryParse(n.Str("result"), out RunResult result) ? result : RunResult.Abandoned,
            DarkMirror = n.Bool("darkMirror"),
            Level = (int)n.Num("level"),
            Self = (int)n.Num("self", -1),
            Rooms = (int)n.Num("rooms"),
            Shrunk = n.Bool("shrunk"),
        };
        foreach (var p in n.List("players"))
            c.Players.Add(CoopPlayer.FromNode(p));
        foreach (var p in n.List("parts"))
            if (CoopPart.FromNode(p) is { } part)
            {
                if (part.Run == null && owner != null && part.Slot == c.Self)
                    part.Run = owner;
                c.Parts[part.Slot] = part;
            }
        foreach (var e in n.List("events"))
            c.Events.Add(CoopEvent.FromNode(e));
        if (owner != null && c.Self >= 0 && !c.Parts.ContainsKey(c.Self))
            c.Parts[c.Self] = new CoopPart { Slot = c.Self, Run = owner };
        return c;
    }

    /// <summary>Makes this the co-op data of the local player's run (the run being recorded is the local part).</summary>
    public void Attach(RunRecord own)
    {
        own.Coop = this;
        if (Self < 0)
            return;
        var mine = Part(Self) ?? (Parts[Self] = new CoopPart { Slot = Self, PlayerId = Player(Self)?.PlayerId ?? "", Name = Player(Self)?.Name ?? "" });
        mine.Run = own;
        Normalize();
    }

    /// <summary>The shrunk record (an old run without a star): players and each part's numbers and build, no timelines or events.</summary>
    internal CoopRunRecord Shrink(RunRecord owner)
    {
        var copy = FromNode(GearDoc.ParseNode(ToJson(owner)), owner);
        foreach (var part in copy.Parts.Values)
        {
            if (part.Run != null && !ReferenceEquals(part.Run, owner))
                part.Run = part.Run.Shrink();
            part.Rooms.Clear();
        }
        copy.Events.Clear();
        copy.Shrunk = true;
        return copy;
    }

    /// <summary>What the index keeps for the run list: the party (names, colours, skulls, who left), no parts or events.</summary>
    internal CoopRunRecord Summary()
    {
        Normalize(); // everyone's skulls from their parts, before the parts go
        var copy = FromNode(GearDoc.ParseNode(ToJson(null)), null);
        copy.Parts.Clear();
        copy.Events.Clear();
        copy.Shrunk = true;
        return copy;
    }

    private static string Q(string s) => LogJson.Quote(s ?? "");
    private static string Date(DateTime d) => d.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

    private static DateTime ParseDate(string s) =>
        DateTime.TryParseExact(s ?? "", "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : default;
}

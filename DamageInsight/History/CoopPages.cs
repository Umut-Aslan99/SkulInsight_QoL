using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DamageInsight.History;

/// <summary>
/// The English texts of the party timeline's lines (format strings, {0} = the first argument). Kept as data, not
/// looked up here: the Codex Runs pages translate them when they draw (each through its own literal Loc call), so this
/// model needs no game and no language. Stored nowhere: they may change.
/// </summary>
public static class CoopTexts
{
    public const string Voted = "{0} voted for door {1}";
    public const string DoorAgreed = "Everyone agreed: door {0}";
    public const string DoorFlip = "Coin flip: door {0} won ({1} chance)";
    public const string DoorTimedOut = "Time ran out: door {0} by the votes in";
    public const string Took = "{0} took {1}";
    public const string TookGold = "{0} took {1} gold";
    public const string TookBones = "{0} took {1} bones";
    public const string Bought = "{0} bought {1} for {2} gold";
    public const string Upgraded = "{0} upgraded {1} into {2}";
    public const string Down = "{0} went down";
    public const string DownBy = "{0} went down ({1})";
    public const string DownStays = "{0} went down again and stays down until the room is cleared";
    public const string Revived = "{0} revived {1}";
    public const string Back = "{0} came back (room cleared)";
    public const string Boss = "{0} defeated in {1}";
    public const string Joined = "{0} joined the run";
    public const string Left = "{0} left the run";
    public const string Rejoined = "{0} came back to the run";
    public const string Ended = "The run ended: {0}";
}

/// <summary>
/// The co-op run's pages as plain data (docs/COOP_PLAN.md §4.11, "a new multiplayer look"): the run list's party, the
/// overview's party side by side, the party timeline room by room, and the damage share per room and per boss. No
/// drawing and no translation: the Codex Runs pages draw these (and each player's own Build, Damage and Timeline tabs
/// are the solo pages of their part's <see cref="RunRecord"/>). Plain logic, no Unity.
/// </summary>
public static class CoopPages
{
    /// <summary>One player in the run list's row: name, colour, skulls (held first), left early.</summary>
    public sealed class ListEntry
    {
        public int Slot, Color;
        public string Name = "";
        public bool Host, Left;
        public List<string> Skulls = new();
    }

    /// <summary>A player's column in the overview.</summary>
    public sealed class PartyRow
    {
        public int Slot, Color;
        public string Name = "";
        public bool Host;
        /// <summary>Left for good in room <see cref="LeftRoom"/> (their part ends there).</summary>
        public bool Left;
        public int LeftRoom = -1;
        /// <summary>Their part is missing (never arrived): only the party facts are known.</summary>
        public bool NoPart;
        /// <summary>Their own result (a player who left: abandoned).</summary>
        public RunResult Result;
        public List<RunGear> Skulls = new(), Essences = new(), Inscriptions = new();
        public int Items;
        public double Dealt, Taken;
        public int Kills, Downs;
        /// <summary>Teammates they revived, and times they were revived.</summary>
        public int Revives, Revived;
        /// <summary>Their part of the party's damage (0-1) and as a whole percent (the party's sum to 100).</summary>
        public double Share;
        public int Percent;
    }

    /// <summary>A player's damage share in a room or a boss fight.</summary>
    public sealed class Share
    {
        public int Slot;
        public double Dealt, Taken;
        /// <summary>0-1, and a whole percent: the shares of one room sum to 100 (largest remainders).</summary>
        public double Fraction;
        public int Percent;
        /// <summary>Boss fights: lost no health.</summary>
        public bool Flawless;
    }

    /// <summary>A line of the party timeline: its text (a <see cref="CoopTexts"/> key and arguments) and who it is about.</summary>
    public sealed class Line
    {
        public int T, Room;
        public string Kind = "";
        public string Key = "";
        public string[] Args = Array.Empty<string>();
        /// <summary>The players named (their colours), in the arguments' order.</summary>
        public int[] Slots = Array.Empty<int>();
        /// <summary>The Codex ids behind the arguments ("" for none): links and icons.</summary>
        public string[] Ids = Array.Empty<string>();
        public CoopEvent Event;
    }

    /// <summary>A room of the party timeline: where, when, what happened, who did how much damage.</summary>
    public sealed class RoomBlock
    {
        public int Room, T, Stage;
        public string Chapter = "", StageName = "", Map = "", Type = "";
        public readonly List<Line> Lines = new();
        public List<Share> Shares = new();
    }

    /// <summary>A boss fight: the boss, how long, each player's share.</summary>
    public sealed class BossFight
    {
        public int Room, T, Seconds;
        public string Id = "", Name = "";
        public List<Share> Shares = new();
    }

    public static bool IsCoop(RunRecord r) => r?.Coop != null;

    /// <summary>The run list's party (a summary is enough: the index keeps names, colours and skulls).</summary>
    public static List<ListEntry> ListRow(RunRecord r) =>
        r?.Coop == null
            ? new List<ListEntry>()
            : r.Coop.Players.OrderBy(p => p.Slot).Select(p => new ListEntry
            {
                Slot = p.Slot, Color = p.Color, Name = r.Coop.NameOf(p.Slot), Host = p.Host, Left = p.HasLeft, Skulls = p.Skulls.ToList(),
            }).ToList();

    /// <summary>The overview: the party side by side, in slot order.</summary>
    public static List<PartyRow> Party(CoopRunRecord c)
    {
        var rows = new List<PartyRow>();
        foreach (int slot in Slots(c))
        {
            var p = c.Player(slot);
            var part = c.Part(slot);
            var run = part?.Run;
            var row = new PartyRow
            {
                Slot = slot, Color = p?.Color ?? slot, Name = c.NameOf(slot), Host = p?.Host ?? false,
                Left = p?.HasLeft ?? false, LeftRoom = p?.LeftRoom ?? -1, NoPart = run == null,
                Result = run?.Result ?? RunResult.Abandoned,
                Downs = c.Events.Count(e => e.Kind == CoopEvents.Down && e.Player == slot),
                Revives = c.Events.Count(e => e.Kind == CoopEvents.Revive && e.By == slot),
                Revived = c.Events.Count(e => e.Kind == CoopEvents.Revive && e.Player == slot),
            };
            if (row.Left && row.Result == RunResult.InProgress)
                row.Result = RunResult.Abandoned;
            if (run != null)
            {
                row.Skulls = run.Build.Where(g => g.Kind == "skull").OrderBy(g => g.Extra.StartsWith("held", StringComparison.Ordinal) ? 0 : 1).ToList();
                row.Essences = run.Build.Where(g => g.Kind == "essence").ToList();
                row.Inscriptions = run.Build.Where(g => g.Kind == "insc" && g.Step > 0).ToList();
                row.Items = run.Build.Count(g => g.Kind == "item");
                row.Dealt = Dealt(part);
                row.Taken = Taken(part);
                row.Kills = Kills(part);
            }
            rows.Add(row);
        }
        var shares = Shares(rows.Select(r => (r.Slot, r.Dealt, r.Taken, false)));
        foreach (var row in rows)
        {
            var s = shares.First(x => x.Slot == row.Slot);
            row.Share = s.Fraction;
            row.Percent = s.Percent;
        }
        return rows;
    }

    /// <summary>The party timeline: room by room (in order), each with its lines and its damage shares.</summary>
    public static List<RoomBlock> Timeline(CoopRunRecord c)
    {
        var blocks = new SortedDictionary<int, RoomBlock>();
        RoomBlock BlockOf(int room, int t)
        {
            if (!blocks.TryGetValue(room, out var b))
                blocks[room] = b = new RoomBlock { Room = room, T = t };
            return b;
        }
        foreach (var e in c.Events)
        {
            var block = BlockOf(e.Room, e.T);
            if (e.Kind == CoopEvents.Room)
            {
                (block.T, block.Map, block.StageName, block.Chapter, block.Type, block.Stage) = (e.T, e.Id, e.Name, e.Other, e.OtherName, e.Value);
                continue;
            }
            block.Lines.AddRange(LinesOf(e, c));
        }
        foreach (var part in c.Parts.Values)
            foreach (var room in part.Rooms)
                BlockOf(room.Room, 0);
        foreach (var block in blocks.Values)
            block.Shares = RoomShares(c, block.Room);
        return blocks.Values.ToList();
    }

    /// <summary>Who did how much damage in a room (from each player's own numbers for it).</summary>
    public static List<Share> RoomShares(CoopRunRecord c, int room) =>
        Shares(Slots(c).Select(slot =>
        {
            var r = c.Part(slot)?.Rooms.FirstOrDefault(x => x.Room == room);
            return (slot, r?.Dealt ?? 0, r?.Taken ?? 0, false);
        }));

    /// <summary>The boss fights won, in order, with each player's share (the boss event's, else their numbers for its room).</summary>
    public static List<BossFight> Bosses(CoopRunRecord c) =>
        c.Events.Where(e => e.Kind == CoopEvents.Boss).Select(e => new BossFight
        {
            Room = e.Room, T = e.T, Seconds = e.Value, Id = e.Id, Name = e.Name,
            Shares = e.Shares is { Count: > 0 }
                ? Shares(Slots(c).Select(slot =>
                {
                    var s = e.Shares.FirstOrDefault(x => x.Player == slot);
                    return (slot, s?.Dealt ?? 0, s?.Taken ?? 0, s?.Flawless ?? false);
                }))
                : RoomShares(c, e.Room),
        }).ToList();

    /// <summary>Shares of a total: fractions summing to 1 and whole percents summing to 100 (largest remainders; 0 without damage).</summary>
    public static List<Share> Shares(IEnumerable<(int slot, double dealt, double taken, bool flawless)> values)
    {
        var list = values.Select(v => new Share { Slot = v.slot, Dealt = Math.Max(0, v.dealt), Taken = v.taken, Flawless = v.flawless }).ToList();
        double total = list.Sum(s => s.Dealt);
        if (total <= 0)
            return list;
        foreach (var s in list)
        {
            s.Fraction = s.Dealt / total;
            s.Percent = (int)Math.Floor(s.Fraction * 100);
        }
        int left = 100 - list.Sum(s => s.Percent);
        foreach (var s in list.OrderByDescending(s => s.Fraction * 100 - s.Percent).ThenBy(s => s.Slot).Take(left))
            s.Percent++;
        return list;
    }

    /// <summary>A line in English (the keys filled in): for logs, tests and before the pages translate.</summary>
    public static string English(Line line) => string.Format(CultureInfo.InvariantCulture, line.Key, line.Args.Cast<object>().ToArray());

    /// <summary>The timeline lines of one party event (a door vote gives a line per vote, then its result).</summary>
    public static List<Line> LinesOf(CoopEvent e, CoopRunRecord c)
    {
        var lines = new List<Line>();
        string Who(int slot) => c.NameOf(slot);
        string Named(string id, string name) => name.Length > 0 ? name : id;
        Line Make(string key, string[] args, int[] slots, string[] ids = null) =>
            new() { T = e.T, Room = e.Room, Kind = e.Kind, Key = key, Args = args, Slots = slots, Ids = ids ?? args.Select(_ => "").ToArray(), Event = e };
        string DoorName(int door) => e.Doors != null && door >= 0 && door < e.Doors.Length && e.Doors[door].Length > 0
            ? e.Doors[door] : (door + 1).ToString(CultureInfo.InvariantCulture);
        switch (e.Kind)
        {
            case CoopEvents.Door:
                if (e.Votes != null)
                    for (int slot = 0; slot < e.Votes.Length; slot++)
                        if (e.Votes[slot] >= 0)
                            lines.Add(Make(CoopTexts.Voted, new[] { Who(slot), DoorName(e.Votes[slot]) }, new[] { slot }));
                string key = e.Flip ? CoopTexts.DoorFlip : e.TimedOut ? CoopTexts.DoorTimedOut : CoopTexts.DoorAgreed;
                var args = e.Flip
                    ? new[] { DoorName(e.Door), Math.Round(e.Chance * 100).ToString("0", CultureInfo.InvariantCulture) + "%" }
                    : new[] { DoorName(e.Door) };
                lines.Add(Make(key, args, Array.Empty<int>()));
                break;
            case CoopEvents.Reward:
                if (e.How == "gold" || e.How == "bones")
                    lines.Add(Make(e.How == "gold" ? CoopTexts.TookGold : CoopTexts.TookBones,
                        new[] { Who(e.Player), e.Value.ToString(CultureInfo.InvariantCulture) }, new[] { e.Player }));
                else
                    lines.Add(Make(CoopTexts.Took, new[] { Who(e.Player), Named(e.Id, e.Name) }, new[] { e.Player }, new[] { "", e.Id }));
                break;
            case CoopEvents.Shop:
                lines.Add(Make(CoopTexts.Bought, new[] { Who(e.Player), Named(e.Id, e.Name), e.Value.ToString(CultureInfo.InvariantCulture) },
                    new[] { e.Player }, new[] { "", e.Id, "" }));
                break;
            case CoopEvents.Upgrade:
                lines.Add(Make(CoopTexts.Upgraded, new[] { Who(e.Player), Named(e.Other, e.OtherName), Named(e.Id, e.Name) },
                    new[] { e.Player }, new[] { "", e.Other, e.Id }));
                break;
            case CoopEvents.Down:
                if (e.StaysDown)
                    lines.Add(Make(CoopTexts.DownStays, new[] { Who(e.Player) }, new[] { e.Player }));
                else if (e.Name.Length > 0 || e.Id.Length > 0)
                    lines.Add(Make(CoopTexts.DownBy, new[] { Who(e.Player), Named(e.Id, e.Name) }, new[] { e.Player }, new[] { "", e.Id }));
                else
                    lines.Add(Make(CoopTexts.Down, new[] { Who(e.Player) }, new[] { e.Player }));
                break;
            case CoopEvents.Revive:
                lines.Add(Make(CoopTexts.Revived, new[] { Who(e.By), Who(e.Player) }, new[] { e.By, e.Player }));
                break;
            case CoopEvents.Back:
                lines.Add(Make(CoopTexts.Back, new[] { Who(e.Player) }, new[] { e.Player }));
                break;
            case CoopEvents.Boss:
                lines.Add(Make(CoopTexts.Boss, new[] { Named(e.Id, e.Name), RunPages.Time(e.Value) }, Array.Empty<int>(), new[] { e.Id, "" }));
                break;
            case CoopEvents.Join:
                lines.Add(Make(CoopTexts.Joined, new[] { Who(e.Player) }, new[] { e.Player }));
                break;
            case CoopEvents.Left:
                lines.Add(Make(CoopTexts.Left, new[] { Who(e.Player) }, new[] { e.Player }));
                break;
            case CoopEvents.Rejoin:
                lines.Add(Make(CoopTexts.Rejoined, new[] { Who(e.Player) }, new[] { e.Player }));
                break;
            case CoopEvents.End:
                lines.Add(Make(CoopTexts.Ended, new[] { e.How }, Array.Empty<int>()));
                break;
        }
        return lines;
    }

    // ------------------------------------------------------------------ a player's numbers (with fallbacks for parts sent early)

    /// <summary>The health their hits took: their room numbers, else their damage table, else the end screen's total.</summary>
    public static double Dealt(CoopPart part)
    {
        if (part.Rooms.Any(r => r.Dealt > 0))
            return part.Rooms.Sum(r => r.Dealt);
        if (part.Run.Damage.Count > 0)
            return part.Run.Damage.Sum(d => d.Amount);
        return part.Run.Number(RunNumbers.TotalDamage);
    }

    /// <summary>The health they lost: their room numbers, else their damage taken table, else the end screen's.</summary>
    public static double Taken(CoopPart part)
    {
        if (part.Rooms.Any(r => r.Taken > 0))
            return part.Rooms.Sum(r => r.Taken);
        if (part.Run.DamageTaken.Count > 0)
            return part.Run.DamageTaken.Sum(p => p.Value.amount);
        return part.Run.Number(RunNumbers.DamageTaken);
    }

    /// <summary>Kills: the end screen's count, else their room numbers, else their maps'.</summary>
    public static int Kills(CoopPart part)
    {
        if (part.Run.Number(RunNumbers.Kills) > 0)
            return (int)part.Run.Number(RunNumbers.Kills);
        if (part.Rooms.Count > 0)
            return part.Rooms.Sum(r => r.Kills);
        return part.Run.Maps.Sum(m => m.Kills);
    }

    /// <summary>Every slot the run knows (players and parts), in order.</summary>
    private static List<int> Slots(CoopRunRecord c) =>
        c.Players.Select(p => p.Slot).Concat(c.Parts.Keys).Distinct().OrderBy(s => s).ToList();
}

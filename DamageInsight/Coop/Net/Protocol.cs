using System;
using System.Collections.Generic;
using System.Text;

namespace DamageInsight.Coop.Net;

/// <summary>
/// The co-op wire protocol (docs/COOP_PLAN.md §5): its version, the message ids and the rule for the mod list hash.
/// Every message starts with its <see cref="MessageType"/> byte. Game messages (ids from <see cref="FirstGameMessage"/>)
/// have a second byte, the slot of the player who wrote them, then the payload. The layouts of Hello (the protocol
/// version first) and Reject (the reason, then the host's versions) never change, so games of different versions can
/// always tell each other why they don't fit.
/// </summary>
public static class Protocol
{
    /// <summary>Raised whenever any message layout changes; games with different versions refuse each other.</summary>
    public const ushort Version = 1;

    /// <summary>Host + 3 (docs/COOP_PLAN.md §12.2: built for 4, tested for 2 first).</summary>
    public const int MaxPlayers = 4;

    /// <summary>What a Steam game takes until co-op phase 4 (3-4 players, after the release; ROADMAP "Not before the
    /// release"): 2, whatever the hidden setting Coop/MaxPlayers says. Without it a second friend could join from the
    /// friends list into a 3-player game nobody tested (2026-11-16, Umut's Co-op window showed "Players 1 / 4").</summary>
    public const int PlayersForNow = 2;

    /// <summary>Ids from here on belong to game packages (states, snapshots, events); below are the session's own.</summary>
    public const byte FirstGameMessage = 32;

    /// <summary>A game message's header: its type and the writer's slot.</summary>
    public const int GameHeaderBytes = 2;

    /// <summary>Player names are cut to this many characters (32 UTF-16 characters are at most 96 UTF-8 bytes).</summary>
    public const int MaxNameChars = 32;
    public const int MaxNameBytes = 96;

    /// <summary>Mod version and mod list hash strings in Hello and Reject.</summary>
    public const int MaxVersionBytes = 128;

    public static bool IsGameMessage(MessageType type) => (byte)type >= FirstGameMessage;

    /// <summary>
    /// The hash of a mod list that every game of a party must share, as 16 lowercase hex digits. The rule (the
    /// matchmaking server computes the same): each entry (e.g. "guid@version") is trimmed and lowercased (invariant),
    /// empty ones are dropped, duplicates removed, the rest sorted ordinally, joined with '\n', encoded as UTF-8 and
    /// hashed with 64-bit FNV-1a (offset 0xcbf29ce484222325, prime 0x100000001b3). Use ASCII entries.
    /// </summary>
    public static string HashModList(IEnumerable<string> entries)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        if (entries != null)
            foreach (var entry in entries)
            {
                string e = entry?.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(e))
                    set.Add(e);
            }
        ulong hash = 0xcbf29ce484222325;
        foreach (byte b in Encoding.UTF8.GetBytes(string.Join("\n", set)))
        {
            hash ^= b;
            hash *= 0x100000001b3;
        }
        return hash.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>A name fit for the roster: no control characters, trimmed, at most <see cref="MaxNameChars"/>
    /// (never splitting a surrogate pair); empty becomes "Player N".</summary>
    public static string CleanName(string name, int slot)
    {
        var sb = new StringBuilder(MaxNameChars);
        if (name != null)
            foreach (char c in name)
                if (!char.IsControl(c))
                    sb.Append(c);
        string s = sb.ToString().Trim();
        if (s.Length > MaxNameChars)
        {
            int cut = MaxNameChars;
            if (char.IsHighSurrogate(s[cut - 1]))
                cut--;
            s = s.Substring(0, cut).TrimEnd();
        }
        return s.Length > 0 ? s : FormattableString.Invariant($"Player {slot + 1}");
    }
}

/// <summary>The first byte of every co-op message.</summary>
public enum MessageType : byte
{
    None = 0,
    /// <summary>Client → host: [protocol u16][mod version str][mod list hash str][name str][player id u64].</summary>
    Hello = 1,
    /// <summary>Host → client: [your slot u8][roster].</summary>
    Welcome = 2,
    /// <summary>Host → client: [RejectReason u8][host protocol u16][host mod version str].</summary>
    Reject = 3,
    /// <summary>Host → clients after a join or leave: [LeaveReason of the change u8][roster].
    /// A roster is [count u8] then per player [slot u8][player id u64][peer u64][name str].</summary>
    Roster = 4,
    /// <summary>Both ways every 0.5 s: [sender's time f64].</summary>
    Ping = 5,
    /// <summary>The answer: [the ping's time f64][answerer's time f64].</summary>
    Pong = 6,
    /// <summary>Leaving on purpose: [LeaveReason u8] (a client: Goodbye; the host: HostLeft or Kicked).</summary>
    Goodbye = 7,

    // 8-31: reserved for the session.

    /// <summary>The first game message (<see cref="Protocol.FirstGameMessage"/>); game packages take ids from here.</summary>
    PlayerState = 32,
    EnemySnapshot = 33,
    TimedEvent = 34,
    /// <summary>Host → all: the run leaves the castle (seed, chapter). CoopWorld.</summary>
    RunStart = 35,
    /// <summary>Host → all: a stage's generated path, so every game has the same rooms. CoopWorld.</summary>
    StagePath = 36,
    /// <summary>Host → all: go through door N (node 1/2) of the room at path index P. CoopWorld.</summary>
    NextMap = 37,
    /// <summary>Everyone (relayed): this game is in room R (the loading barrier). CoopWorld.</summary>
    RoomReady = 38,
    /// <summary>Host → all: an enemy moved (a motion), died, or a wave appeared. EnemySync.</summary>
    EnemyEvent = 39,
    /// <summary>A client → the host: its player hit an enemy (the host applies it). EnemySync.</summary>
    EnemyHit = 40,
    /// <summary>Host → all: the host's room (castle, or seed/chapter/stage/path/node), on each room and every 3 s; a
    /// client out of step goes there (CatchUp). CoopWorld.</summary>
    HostRoom = 41,
    /// <summary>A client → the host: its player stunned or froze an enemy (the host gives it to the real one). EnemySync.</summary>
    EnemyStatus = 42,
    /// <summary>A client → the host: its player pushed an enemy (knockback or smash; the host pushes the real one). EnemySync.</summary>
    EnemyPush = 43,
    /// <summary>Host → all: a story scene of the room starts; a client → the host: its player set one off (the host
    /// starts it for everyone). SharedScenes.</summary>
    CutScene = 44,
    /// <summary>Going down and revive (ReviveMessages; players to the host, the host to everyone).</summary>
    Revive = 45,
    /// <summary>The shared world's loot (LootMessages): host → all: what an enemy dropped; a player → the host →
    /// the others: a plain prop broke, with what it dropped. SharedLoot.</summary>
    Loot = 46,
    /// <summary>The door vote (DoorVoteMessages): a player → the host: "I pressed this door"; host → all: how the
    /// room's vote stands, and its result. CoopDoors.</summary>
    DoorVote = 47,
    /// <summary>Host → a client: the host's numbers for the rules (the enemy health table, the revive numbers:
    /// HostRulesMessages), on joining and when the host changes them. CoopHostRules.</summary>
    HostRules = 48,
    /// <summary>A player's slow motion that changes the fight (TimeEffectMessages: room, key, scale, seconds): its
    /// game → everyone (a client's through the host's relay); every game slows its room's enemies. CoopTimeEffects.</summary>
    TimeEffect = 49,
    /// <summary>Host → a client: the host's world flags (HostWorldMessages: the names of the set ones: scenes seen,
    /// Darktech unlocked, NPCs met / rescued), on joining and when one changes. HostWorld.</summary>
    HostWorld = 50,
    /// <summary>Anyone → everyone (relayed): this player's damage summary for the co-op combat log (DamageShare,
    /// CoopDamageBoard), every 2 s while it changes.</summary>
    DamageShare = 51,
    /// <summary>Anyone → everyone (relayed): this player's note for the Co-op window (Rules.PlayerNote: Dark Mirror
    /// progress, a request for more or less enemy health, OK), when it changes, when someone joins and every 15 s.
    /// CoopPlayerNotes. A 0.17.0 game has no handler for it and drops it.</summary>
    PlayerNote = 52,
}

/// <summary>Why the host refused a Hello (sent in Reject).</summary>
public enum RejectReason : byte
{
    None = 0,
    /// <summary>Another protocol version or mod version.</summary>
    VersionMismatch = 1,
    /// <summary>Another set of mods (<see cref="Protocol.HashModList"/>).</summary>
    ModListMismatch = 2,
    /// <summary>All slots are taken.</summary>
    Full = 3,
    /// <summary>This player was kicked and banned from the session.</summary>
    Kicked = 4,
    /// <summary>The host is closing the session.</summary>
    HostLeaving = 5,
    /// <summary>The host doesn't take new players now (a run in progress).</summary>
    NotAccepting = 6,
    /// <summary>A player with the same player id is already in the session.</summary>
    DuplicatePlayer = 7,
}

/// <summary>Why a player left or a session ended.</summary>
public enum LeaveReason : byte
{
    /// <summary>Left on purpose (said goodbye).</summary>
    Goodbye = 0,
    /// <summary>Nothing heard for <see cref="SessionOptions.TimeoutSeconds"/>.</summary>
    Timeout = 1,
    /// <summary>The transport lost the connection.</summary>
    ConnectionLost = 2,
    /// <summary>Data that makes no sense (a broken Hello or roster).</summary>
    ProtocolError = 3,
    /// <summary>The host kicked the player.</summary>
    Kicked = 4,
    /// <summary>The host ended the session.</summary>
    HostLeft = 5,
    /// <summary>The host refused to let this game in (<see cref="CoopSession.Rejection"/> says why).</summary>
    Rejected = 6,
    /// <summary>This game ended the session itself (<see cref="CoopSession.Leave"/>).</summary>
    LocalLeave = 7,
}

public static class LeaveReasons
{
    /// <summary>The line broke (nothing heard, or the transport lost it): the player may come back (package DC).
    /// Everything else (goodbye, kick, the host ending it, a reject) was meant.</summary>
    public static bool IsDrop(LeaveReason reason) => reason is LeaveReason.Timeout or LeaveReason.ConnectionLost;
}

using System;

namespace DamageInsight.Coop.Rules;

/// <summary>Why a vote was or wasn't taken.</summary>
public enum VoteResponse : byte
{
    Accepted = 0,
    /// <summary>The same door again: nothing changed.</summary>
    Unchanged,
    /// <summary>The room rewards still wait (§4.3.4: the doors open for votes only after them).</summary>
    NotOpenYet,
    AlreadyResolved,
    /// <summary>Down or disconnected players don't vote.</summary>
    NotAllowed,
    InvalidDoor,
}

/// <summary>
/// The door vote of one room (§4.3, Umut: "a coin flip, no long waits"). A player who walks into a door votes for
/// it; a vote can change until the vote is resolved. It resolves when every living, connected player voted: the same
/// door -> that door; different doors -> a coin flip at once between the voted doors, weighted by votes. After the
/// first vote the others have <see cref="DoorVoteSettings.VoteSeconds"/> (10 s); then the votes that exist decide.
/// Down players don't vote. The doors open for votes only once the room's <see cref="RoomRewardGate"/> released.
/// Runs on the host, seeded from the run seed + room index; the result goes to everyone and into the party timeline.
/// Call <see cref="Update"/> every frame (players leaving or going down can complete the vote).
/// </summary>
public sealed class DoorVote
{
    public const int MaxDoors = 8;

    private readonly PartyState _party;
    private readonly DoorVoteSettings _settings;
    private readonly ICoopRandom _random;
    private readonly RoomRewardGate _gate;
    private readonly int[] _votes = new int[PartyState.MaxPlayers];
    private double _deadline = -1;

    /// <param name="gate">The room's reward gate; null = the doors are open at once (a room without rewards).</param>
    public DoorVote(int roomIndex, int doorCount, PartyState party, DoorVoteSettings settings, ICoopRandom random,
        RoomRewardGate gate)
    {
        if (doorCount < 1 || doorCount > MaxDoors)
            throw new ArgumentOutOfRangeException(nameof(doorCount), doorCount, "1-8 doors");
        RoomIndex = roomIndex;
        DoorCount = doorCount;
        _party = party ?? throw new ArgumentNullException(nameof(party));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _gate = gate;
        _settings.Clamp();
        for (int i = 0; i < _votes.Length; i++)
            _votes[i] = -1;
    }

    public int RoomIndex { get; }
    public int DoorCount { get; }

    /// <summary>Null until resolved.</summary>
    public DoorVoteResult Result { get; private set; }

    public bool IsResolved => Result != null;
    public bool IsOpen => _gate == null || _gate.IsReleased;

    /// <summary>The player's current vote, -1 = none.</summary>
    public int VoteOf(int slot) => PartyState.IsSlot(slot) ? _votes[slot] : -1;

    /// <summary>A player walked into a door (or changed their mind).</summary>
    public VoteResponse Vote(int slot, int door, double now)
    {
        if (IsResolved)
            return VoteResponse.AlreadyResolved;
        if (door < 0 || door >= DoorCount)
            return VoteResponse.InvalidDoor;
        if (!_party.IsActive(slot))
            return VoteResponse.NotAllowed;
        _gate?.Update(now);
        if (!IsOpen)
            return VoteResponse.NotOpenYet;
        if (_votes[slot] == door)
            return VoteResponse.Unchanged;
        _votes[slot] = door;
        // [DECISION] The countdown starts with the room's first vote and isn't restarted by later votes or changes.
        if (_deadline < 0)
            _deadline = now + _settings.VoteSeconds;
        Update(now);
        return VoteResponse.Accepted;
    }

    /// <summary>A player stepped back out of the door before the vote resolved.</summary>
    public void Withdraw(int slot, double now)
    {
        if (IsResolved || !PartyState.IsSlot(slot))
            return;
        _votes[slot] = -1;
        Update(now);
    }

    /// <summary>Seconds left for the others after the first vote; -1 while nobody voted or once resolved.</summary>
    public double SecondsLeft(double now) => IsResolved || _deadline < 0 ? -1 : Math.Max(0, _deadline - now);

    /// <summary>Resolves the vote when everyone voted or the time ran out. True once, on the resolving call.</summary>
    public bool Update(double now)
    {
        if (IsResolved || !IsOpen)
            return false;
        int active = 0, voted = 0;
        for (int i = 0; i < PartyState.MaxPlayers; i++)
        {
            if (!_party.IsActive(i))
                continue; // [DECISION] Votes of players who are down or gone don't count (they come back if the player does).
            active++;
            if (_votes[i] >= 0)
                voted++;
        }
        if (voted == 0)
        {
            // [DECISION] Every vote left with its player (or was withdrawn): nothing to decide, wait for a new first vote.
            if (_deadline >= 0 && now >= _deadline)
                _deadline = -1;
            return false;
        }
        bool everyone = voted == active;
        bool timedOut = !everyone && _deadline >= 0 && now >= _deadline;
        if (!everyone && !timedOut)
            return false;
        Resolve(now, timedOut);
        return true;
    }

    private void Resolve(double now, bool timedOut)
    {
        var counted = new int[PartyState.MaxPlayers];
        var perDoor = new int[DoorCount];
        int total = 0, doorsVoted = 0, onlyDoor = -1;
        for (int i = 0; i < PartyState.MaxPlayers; i++)
        {
            int door = _party.IsActive(i) ? _votes[i] : -1;
            counted[i] = door;
            if (door < 0)
                continue;
            if (perDoor[door]++ == 0)
            {
                doorsVoted++;
                onlyDoor = door;
            }
            total++;
        }
        int chosen = onlyDoor;
        bool flip = doorsVoted > 1;
        if (flip)
        {
            // Weighted by votes: a door with 2 of 3 votes wins 2 times in 3.
            double roll = _random.NextDouble() * total;
            chosen = DoorCount - 1;
            for (int d = 0; d < DoorCount; d++)
            {
                if (perDoor[d] == 0)
                    continue;
                chosen = d;
                if (roll < perDoor[d])
                    break;
                roll -= perDoor[d];
            }
        }
        Result = new DoorVoteResult(RoomIndex, chosen, flip, timedOut, now, counted, perDoor, total);
    }
}

/// <summary>
/// A resolved door vote, for the party (where to go, whether to show the coin flip) and the run history's party
/// timeline (§4.11: "which door each player voted and the coin flips"). Maps 1:1 to the history's "door" event.
/// </summary>
public sealed class DoorVoteResult
{
    private readonly int[] _votes;
    private readonly int[] _perDoor;
    private readonly int _total;

    internal DoorVoteResult(int roomIndex, int door, bool coinFlip, bool timedOut, double resolvedAt, int[] votes,
        int[] perDoor, int total)
    {
        RoomIndex = roomIndex;
        Door = door;
        CoinFlip = coinFlip;
        TimedOut = timedOut;
        ResolvedAt = resolvedAt;
        _votes = votes;
        _perDoor = perDoor;
        _total = total;
    }

    public int RoomIndex { get; }

    /// <summary>The door the party goes through.</summary>
    public int Door { get; }

    /// <summary>The votes differed and a weighted coin flip picked the door (false = agreed, or only one door had votes).</summary>
    public bool CoinFlip { get; }

    /// <summary>Someone didn't vote in time; the votes that existed decided.</summary>
    public bool TimedOut { get; }

    public double ResolvedAt { get; }

    /// <summary>The door each player voted for (-1 = no vote, down or gone), by slot. A copy.</summary>
    public int[] Votes => (int[])_votes.Clone();

    public int VoteOf(int slot) => PartyState.IsSlot(slot) ? _votes[slot] : -1;

    public int VotesFor(int door) => door >= 0 && door < _perDoor.Length ? _perDoor[door] : 0;

    /// <summary>The chance the flip gave this door (1 when everyone agreed).</summary>
    public double ChanceOf(int door) => _total == 0 ? 0 : (double)VotesFor(door) / _total;
}

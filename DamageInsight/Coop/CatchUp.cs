namespace DamageInsight.Coop;

/// <summary>Where a game is in a run: the castle, or seed + chapter + stage + path step + node (the game's own save
/// position, `LevelManager.Save`), which is enough to load that exact room (`Chapter.Enter`).</summary>
public readonly struct RunSpot
{
    public readonly bool Castle;
    public readonly int Seed;
    public readonly int Chapter;
    public readonly int Stage;
    public readonly int Path;
    public readonly int Node;

    public RunSpot(bool castle, int seed, int chapter, int stage, int path, int node)
    {
        Castle = castle;
        Seed = seed;
        Chapter = chapter;
        Stage = stage;
        Path = path;
        Node = node;
    }

    public static RunSpot InCastle(int chapter) => new(true, 0, chapter, 0, 0, 0);

    /// <summary>The same room. Two castles are the same place only in the same dimension: the normal castle (or the
    /// tutorial) and the Dark Mirror castle are not (Umut 2026-11-16: one player in each, both could start a run in
    /// another world; the host leads, a client goes to the host's castle).</summary>
    public bool SameRoom(RunSpot other) =>
        Castle || other.Castle
            ? Castle && other.Castle && Dark == other.Dark
            : Seed == other.Seed && Chapter == other.Chapter && Stage == other.Stage && Path == other.Path && Node == other.Node;

    /// <summary>The Dark Mirror: its castle or one of its chapters (Chapter.Type HardmodeCastle = 8 and up).</summary>
    public bool Dark => Chapter >= DarkMirrorCastle;

    public const int DarkMirrorCastle = 8;

    /// <summary>The same chapter of the same run (a jump inside it needs no chapter load).</summary>
    public bool SameChapter(RunSpot other) => !Castle && !other.Castle && Seed == other.Seed && Chapter == other.Chapter;

    public override string ToString() => Castle ? (Dark ? "Dark Mirror castle" : "castle") : $"chapter {Chapter} stage {Stage} room {Path} node {Node} (seed {Seed})";
}

public enum CatchUpStep
{
    /// <summary>Same room as the host, or nothing known yet.</summary>
    InStep,
    /// <summary>Elsewhere, but loading or about to follow the host's door: give it time.</summary>
    Wait,
    /// <summary>Elsewhere for longer than <see cref="CatchUp.Grace"/>: load the host's room.</summary>
    Jump,
    /// <summary>Jumped to this spot <see cref="CatchUp.MaxJumps"/> times without arriving: stop trying (logged).</summary>
    GiveUp,
}

/// <summary>
/// A client that fell out of step with the host (the host used the game's dev menu: a stage skip or a reload; a lost
/// message; any glitch) goes to the host's room by itself (COOP_LOG 29c: before, "out of step" errors forever).
/// Normal room changes still follow the host's doors; this only acts after <see cref="Grace"/> seconds elsewhere
/// without anything on the way. Plain logic, tested.
/// </summary>
public sealed class CatchUp
{
    public double Grace = 3;
    /// <summary>The wait before a client follows the host into its other castle (the Dark Mirror and back).</summary>
    public double CastleGrace = 0.5;
    public int MaxJumps = 3;
    private double _outSince = -1;
    private RunSpot? _jumpedTo;
    private int _jumps;

    /// <param name="host">The host's last reported spot (null: none yet).</param>
    /// <param name="own">Where this game is.</param>
    /// <param name="busy">Loading, at the loading barrier, starting the run.</param>
    /// <param name="doorFromHere">A host door is queued that starts in this game's room (normal following).</param>
    public CatchUpStep Decide(RunSpot? host, RunSpot own, bool busy, bool doorFromHere, double now)
    {
        if (host == null || host.Value.SameRoom(own))
        {
            _outSince = -1;
            _jumpedTo = null;
            _jumps = 0;
            return CatchUpStep.InStep;
        }
        if (busy || doorFromHere)
        {
            _outSince = -1;
            return CatchUpStep.Wait;
        }
        if (_outSince < 0)
            _outSince = now;
        // between the two castles the host's mirror took everyone along at once (the castles have nothing to wait for)
        if (now - _outSince < (host.Value.Castle && own.Castle ? CastleGrace : Grace))
            return CatchUpStep.Wait;
        if (_jumpedTo.HasValue && _jumpedTo.Value.SameRoom(host.Value) && _jumps >= MaxJumps)
            return CatchUpStep.GiveUp;
        return CatchUpStep.Jump;
    }

    /// <summary>A jump to <paramref name="spot"/> was started (the next one waits <see cref="Grace"/> again).</summary>
    public void Jumped(RunSpot spot)
    {
        _jumps = _jumpedTo.HasValue && _jumpedTo.Value.SameRoom(spot) ? _jumps + 1 : 1;
        _jumpedTo = spot;
        _outSince = -1;
    }
}

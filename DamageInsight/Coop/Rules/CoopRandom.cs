namespace DamageInsight.Coop.Rules;

/// <summary>A source of random numbers for the rules (tests can script one).</summary>
public interface ICoopRandom
{
    /// <summary>A number in [0, 1).</summary>
    double NextDouble();
}

/// <summary>
/// The rules' seeded random: SplitMix64, the same numbers on every PC and runtime (System.Random's algorithm is not
/// promised to stay the same). The host seeds it from the run seed and the room index, so a room's coin flip can be
/// replayed exactly (tests, desync checks); the host decides and sends the result, the others never roll.
/// </summary>
public sealed class CoopRandom : ICoopRandom
{
    /// <summary>What the numbers are for, so two rules in one room don't share a sequence.</summary>
    public enum Purpose : uint
    {
        DoorFlip = 1,
    }

    private ulong _state;

    public CoopRandom(ulong seed) => _state = seed;

    /// <summary>The random for one room of a run: the run's seed (the game's randomSeed) + the room index.</summary>
    public static CoopRandom ForRoom(int runSeed, int roomIndex, Purpose purpose)
    {
        ulong seed = (ulong)(uint)runSeed;
        seed = Mix(seed ^ ((ulong)(uint)roomIndex << 32));
        seed = Mix(seed ^ (uint)purpose);
        return new CoopRandom(seed);
    }

    public ulong NextULong()
    {
        _state += 0x9E3779B97F4A7C15UL;
        return Mix(_state);
    }

    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    private static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }
}

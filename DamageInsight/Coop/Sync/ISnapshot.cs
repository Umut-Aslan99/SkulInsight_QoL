namespace DamageInsight.Coop.Sync;

/// <summary>
/// What a state needs so a <see cref="SnapshotBuffer{T}"/> can draw it between updates (docs/COOP_PLAN.md §5.3):
/// where it is, how to blend two states and how to move on with its own velocity. Implemented by
/// <see cref="PlayerState"/> and <see cref="EnemyState"/>; the buffer calls these through the generic constraint, so
/// nothing is boxed.
/// </summary>
public interface ISnapshot<T> where T : struct
{
    Vec2 Position { get; }

    /// <summary>This state at t = 0, <paramref name="next"/> at t = 1; <paramref name="span"/> = the seconds between
    /// the two (lets a motion that started in between begin at the right moment).</summary>
    T Lerp(in T next, float t, float span);

    /// <summary>This state <paramref name="seconds"/> later, moving on with its velocity (motion time runs on).</summary>
    T Extrapolate(float seconds);

    /// <summary>This state drawn <paramref name="offset"/> away (a correction being blended out).</summary>
    T Moved(Vec2 offset);
}

/// <summary>How a <see cref="SnapshotBuffer{T}"/> got the state it drew.</summary>
public enum SampleKind : byte
{
    /// <summary>No state yet: draw nothing (or keep the entity hidden).</summary>
    Empty = 0,
    /// <summary>The render time is still before the first state: the first state is shown.</summary>
    Waiting = 1,
    /// <summary>Between two known states (the normal case).</summary>
    Interpolated = 2,
    /// <summary>Past the newest state: moved on with its velocity (at most MaxExtrapolation).</summary>
    Extrapolated = 3,
    /// <summary>Past the newest state for longer than MaxExtrapolation: standing where extrapolation stopped.</summary>
    Held = 4,
}

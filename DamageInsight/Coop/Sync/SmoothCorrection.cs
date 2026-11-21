using System;

namespace DamageInsight.Coop.Sync;

/// <summary>
/// Blends a position error away instead of snapping (docs/COOP_PLAN.md §5.3: "corrections blend in over a few
/// frames"). Example: an enemy was drawn moving on with its last velocity, then the real update says it had stopped;
/// or a predicted position turns out wrong. The drawn position becomes <c>true position + Offset</c>, and the offset
/// shrinks exponentially (95 % gone after <see cref="BlendSeconds"/>). An error bigger than
/// <see cref="SnapDistance"/> is a teleport: the offset is dropped and the entity snaps.
/// </summary>
public sealed class SmoothCorrection
{
    /// <summary>Seconds until 95 % of an error is gone (three time constants of the exponential decay). 0.15 s =
    /// about 9 frames: fast enough to stay accurate, slow enough not to look like a jump.</summary>
    public float BlendSeconds = 0.15f;

    /// <summary>A bigger error (world units) snaps instead of sliding: sliding across half a room looks worse.</summary>
    public float SnapDistance = 5f;

    /// <summary>An offset below this (world units, about a tenth of a pixel) counts as gone.</summary>
    public float RestDistance = 0.002f;

    /// <summary>Add this to the true position to get the drawn one.</summary>
    public Vec2 Offset { get; private set; }

    public bool IsActive => Offset.X != 0 || Offset.Y != 0;

    /// <summary>The entity is drawn at <paramref name="drawn"/> but should be at <paramref name="target"/>: keep
    /// drawing where it is and blend toward the target from now on. Returns true when it snapped instead.</summary>
    public bool Blend(Vec2 drawn, Vec2 target) => Add(drawn - target);

    /// <summary>The true position jumped by -<paramref name="error"/> (error = old drawn - new true): blend it.
    /// Returns true when the total error was too big and it snapped instead.</summary>
    public bool Add(Vec2 error)
    {
        var total = Offset + error;
        if (total.LengthSquared > SnapDistance * SnapDistance)
        {
            Offset = Vec2.Zero;
            return true;
        }
        Offset = total;
        return false;
    }

    /// <summary>Lets <paramref name="seconds"/> of blending pass; returns the offset left.</summary>
    public Vec2 Step(float seconds)
    {
        if (!IsActive || seconds <= 0)
            return Offset;
        float tau = Math.Max(1e-4f, BlendSeconds / 3f);
        var left = Offset * (float)Math.Exp(-seconds / tau);
        Offset = left.LengthSquared < RestDistance * RestDistance ? Vec2.Zero : left;
        return Offset;
    }

    public void Reset() => Offset = Vec2.Zero;
}

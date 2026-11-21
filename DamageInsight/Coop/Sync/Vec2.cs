using System;

namespace DamageInsight.Coop.Sync;

/// <summary>
/// A 2D point or movement in world units. The sync code uses this instead of Unity's Vector2 so it stays plain C#
/// and testable; the integrator converts (<c>new Vector2(v.X, v.Y)</c>).
/// </summary>
public readonly struct Vec2 : IEquatable<Vec2>
{
    public readonly float X, Y;

    public Vec2(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static readonly Vec2 Zero = new(0, 0);

    public float Length => (float)Math.Sqrt(X * X + Y * Y);
    public float LengthSquared => X * X + Y * Y;

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, float s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(float s, Vec2 a) => new(a.X * s, a.Y * s);

    /// <summary>a at t = 0, b at t = 1.</summary>
    public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

    public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;

    public bool Equals(Vec2 other) => X == other.X && Y == other.Y;
    public override bool Equals(object obj) => obj is Vec2 other && Equals(other);
    public override int GetHashCode() => X.GetHashCode() * 397 ^ Y.GetHashCode();
    public static bool operator ==(Vec2 a, Vec2 b) => a.Equals(b);
    public static bool operator !=(Vec2 a, Vec2 b) => !a.Equals(b);
    public override string ToString() => FormattableString.Invariant($"({X:0.###}, {Y:0.###})");
}

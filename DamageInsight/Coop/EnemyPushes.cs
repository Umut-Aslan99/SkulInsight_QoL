using Characters;
using Characters.Movements;
using DamageInsight.Coop.Net;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Knockback and smash of a client's hits (T.md Open 10): before, the client's puppet flew back for a moment and the
/// host's enemy, which decides where it is, didn't move. Now the push a client's player (or its minion) puts on a
/// puppet goes to the host as it was set (Characters.Movements.Push: two forces with their curves, smash and its
/// flags), and the host puts the same push on its enemy; the host's snapshots carry the movement back. The game's own
/// rule stays: a smash still running on the host's enemy isn't replaced.
/// </summary>
internal sealed partial class EnemySync
{
    /// <summary>Client: this player's push on a puppet goes to the host.</summary>
    internal void OnLocalPush(Push push)
    {
        var target = push._owner;
        var from = push._from;
        if (!IsClient || target == null || from == null || _lifeOnly.Contains(target) || !_ids.TryGetValue(target, out ushort id))
            return;
        if (CoopCopies.BelongsToCopy(from) || !CoopCopies.IsPlayerSide(from))
            return;
        var message = new EnemyPush
        {
            Id = id, Smash = push.smash, IgnoreOtherForce = push.ignoreOtherForce, ExpireOnGround = push.expireOnGround,
        };
        Fill(message.Parts[0], push._force.Item1, push._curve.Item1);
        Fill(message.Parts[1], push._force.Item2, push._curve.Item2);
        _writer.Reset();
        EnemyMessages.WritePush(_writer, _room, message);
        _session.Send(0, MessageType.EnemyPush, _writer, Delivery.Reliable);
        CoopTrace.Write("push-sent", _room, id, CoopTrace.NameOf(target), push.smash ? "smash" : "knockback",
            amount: (push._force.Item1 + push._force.Item2).magnitude);
    }

    private static void Fill(PushPart part, Vector2 force, Curve curve)
    {
        part.X = force.x;
        part.Y = force.y;
        if (curve == null)
            return;
        part.ValueMultiplier = curve._valueMultiplier;
        part.Duration = curve._durationMultiplier;
        var keys = curve._curve != null ? curve._curve.keys : System.Array.Empty<Keyframe>();
        int n = System.Math.Min(keys.Length, EnemyPush.MaxKeys);
        part.Keys = new float[n * 4];
        for (int i = 0; i < n; i++)
        {
            part.Keys[i * 4] = keys[i].time;
            part.Keys[i * 4 + 1] = keys[i].value;
            part.Keys[i * 4 + 2] = keys[i].inTangent;
            part.Keys[i * 4 + 3] = keys[i].outTangent;
        }
    }

    private static Curve CurveOf(PushPart part)
    {
        if (part.Keys.Length == 0 && part.Duration == 0f && part.ValueMultiplier == 0f)
            return Curve.empty;
        var keys = new Keyframe[part.Keys.Length / 4];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = new Keyframe(part.Keys[i * 4], part.Keys[i * 4 + 1], part.Keys[i * 4 + 2], part.Keys[i * 4 + 3]);
        return new Curve(new AnimationCurve(keys), part.ValueMultiplier, part.Duration);
    }

    /// <summary>Push's smash-end event field (the compiler names it like the event).</summary>
    private static readonly System.Reflection.FieldInfo OnEndField = AccessTools.Field(typeof(Push), "onEnd");

    /// <summary>Host: a client's push on the real enemy (from its copy, like its hits and statuses).</summary>
    private void OnPush(GameMessage message, NetReader payload)
    {
        var p = EnemyMessages.ReadPush(payload, out uint room);
        if (!IsHost || room != _room)
            return;
        var enemy = EnemyOf(p.Id);
        var from = _avatarOf(message.FromSlot);
        string result = "push-refused";
        if (enemy != null && enemy.movement != null && enemy.health != null && !enemy.health.dead
            && enemy.gameObject.activeInHierarchy && from != null)
        {
            var push = enemy.movement.push;
            if (push.smash && !push.expired)
                result = "push-kept"; // the game's rule: a running smash isn't replaced
            else
            {
                push._from = from;
                push._force = (new Vector2(p.Parts[0].X, p.Parts[0].Y), new Vector2(p.Parts[1].X, p.Parts[1].Y));
                push._curve = (CurveOf(p.Parts[0]), CurveOf(p.Parts[1]));
                push.ignoreOtherForce = p.IgnoreOtherForce;
                push.expireOnGround = p.ExpireOnGround;
                OnEndField?.SetValue(push, null); // no smash-end callback of an earlier push
                push.smash = p.Smash;
                push.Intialize();
                result = "push-applied";
            }
        }
        CoopTrace.Write(result, room, p.Id, CoopTrace.NameOf(enemy), p.Smash ? "smash" : "knockback", slot: message.FromSlot);
    }

    /// <summary>Every push of the game ends in Push.Intialize (all six Apply methods).</summary>
    [HarmonyPatch(typeof(Push), "Intialize")]
    public static class PushGiven
    {
        private static void Postfix(Push __instance) => Current?.OnLocalPush(__instance);
    }
}

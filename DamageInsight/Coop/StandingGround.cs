using Characters;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// "The platform it last stood on" (<c>collisionState.lastStandingCollider</c>) for characters whose movement code is
/// off on this game: teammate copies and enemy puppets. The movement code is what keeps it up to date, so it stayed
/// empty, and boss attacks read it from their target and from themselves: Leiana's homing orbs and meteors
/// (`base.target...lastStandingCollider.bounds`) would stop the host's boss with an error when it aimed at a client's
/// copy, and the Dark sister's thunder on a client (BossSync calls) needs her own platform.
/// </summary>
internal static class StandingGround
{
    private const float Above = 0.2f;   // the ray starts a little above the feet (a character's position is its feet)
    private const float Near = 0.35f;   // standing: ground this close below the feet
    private const float Far = 30f;      // nothing known yet: the first ground below, wherever it is

    /// <summary>Called once per drawn frame; <paramref name="grounded"/> = what the owner's game says.</summary>
    public static void Update(Character character, bool grounded)
    {
        var controller = character != null && character.movement != null ? character.movement.controller : null;
        if (controller == null || controller.collisionState == null)
            return;
        var current = controller.collisionState.lastStandingCollider;
        if (current != null && !grounded)
            return; // in the air: the last one stays, like the movement code keeps it
        int mask = controller.terrainMask | controller.oneWayPlatformMask;
        Vector2 feet = character.transform.position;
        float reach = current == null ? Far : Near;
        var hit = Physics2D.Raycast(feet + Vector2.up * Above, Vector2.down, reach + Above, mask);
        if (hit.collider != null && hit.collider != current)
            controller.ForceUpdateLastStandingCollier(hit.collider);
    }
}

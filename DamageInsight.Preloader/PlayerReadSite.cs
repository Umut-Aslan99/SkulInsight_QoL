using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DamageInsight.Preloader;

/// <summary>
/// What a read of "the player" (LevelManager.player) means in co-op, where a game has more than one player.
/// </summary>
public enum PlayerReadKind
{
    /// <summary>Always the player of this game (HUD, UI, camera, input, menus, saves, achievements): left unchanged.</summary>
    Local,
    /// <summary>The character whose code runs (gear, items, skulls, abilities, shared operations and projectiles).</summary>
    Owner,
    /// <summary>Enemy code (AI, bosses, enemy-only operations): that enemy's target.</summary>
    Target,
    /// <summary>Level, map, NPC, cutscene and runnable scripts: the plugin decides (the host's player, the nearest, ...).</summary>
    World,
}

/// <summary>
/// One read of LevelManager.player in the game's code: where it is, its stable id (the index in the sorted list of
/// keys) and what it was classified as, by which rule.
/// </summary>
public sealed class PlayerReadSite
{
    public PlayerReadSite(MethodDefinition method, Instruction instruction, int index)
    {
        Method = method;
        Instruction = instruction;
        Index = index;
        Key = $"{method.DeclaringType.FullName}::{method.Name}({string.Join(",", ParameterNames(method))})#{index}";
    }

    public MethodDefinition Method { get; }
    public Instruction Instruction { get; }

    /// <summary>The n-th read in its method (IL order, from 0).</summary>
    public int Index { get; }

    /// <summary>"Type::Method(Params)#n": stable as long as the game's code doesn't change.</summary>
    public string Key { get; }

    public int Id { get; internal set; }
    public PlayerReadKind Kind { get; internal set; }

    /// <summary>The rule that decided the kind (shown in docs/coop/PLAYER_READS.md); null when no rule matched.</summary>
    public string Rule { get; internal set; }

    public bool Classified => Rule != null;

    /// <summary>The type the rules look at: compiler-made types (lambdas, coroutines) count as their outer type.</summary>
    public TypeDefinition ClassType => PlayerReadRules.ClassType(Method.DeclaringType);

    /// <summary>The method in the source code (a coroutine's or lambda's own method name).</summary>
    public string SourceMethod => PlayerReadRules.SourceMethod(Method);

    /// <summary>"coroutine", "lambda", "local function" or "" (a read that runs later than its method was called).</summary>
    public string Deferred => PlayerReadRules.Deferred(Method);

    private static string[] ParameterNames(MethodDefinition method)
    {
        var names = new string[method.Parameters.Count];
        for (int i = 0; i < names.Length; i++)
            names[i] = method.Parameters[i].ParameterType.Name;
        return names;
    }
}

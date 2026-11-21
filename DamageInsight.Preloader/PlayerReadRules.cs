using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace DamageInsight.Preloader;

/// <summary>
/// Decides the kind of every read of LevelManager.player. Order: a method override ("Type::Method"), a code pattern
/// (the read only feeds the camera or a cinematic lock = Local), a type override, then the longest matching namespace.
/// Lambdas and coroutines count as their outer type and method. No rule = unclassified: left unchanged (Local) and
/// counted, so a game update with new reads is noticed (the test fails, the plugin can refuse co-op).
/// Sources: the decompiled game and the game flow research (docs/coop/GAME_FLOW.md).
/// </summary>
public static class PlayerReadRules
{
    private const PlayerReadKind L = PlayerReadKind.Local, O = PlayerReadKind.Owner, T = PlayerReadKind.Target, W = PlayerReadKind.World;

    /// <summary>Namespaces (with their sub-namespaces); the longest match wins.</summary>
    public static readonly (string ns, PlayerReadKind kind, string why)[] Namespaces =
    {
        ("UI", L, "HUD, menus and popups of this game"),
        ("AchievementTrackers", L, "this game's achievements"),
        ("Tutorial", L, "tutorial of this game"),
        ("Tutorials", L, "tutorial of this game"),
        ("Services", L, "this game's services (gear pools filter what you own, QTE input)"),
        ("Hardmode.Darktech", L, "Dark Mirror machines: private services and sounds on each game"),
        ("Level.BlackMarket", L, "private shop (each player their own)"),
        ("Level.Npc.FieldNpcs", L, "private NPC services (never block the others)"),
        ("Level.Altars", L, "altars buff whoever uses them on that game"),
        ("Characters.Gear.Upgrades", L, "the upgrade shop (private)"),
        ("Characters.AI", T, "enemy and boss AI: that enemy's target"),
        ("Characters.AI.Mercenarys", O, "allied summons (mercenaries, soulmate) follow their owner"),
        ("BT", T, "enemy behaviour trees"),
        ("BehaviorDesigner.Runtime.Tasks", T, "enemy behaviour trees (BehaviorDesigner)"),
        ("Characters", O, "character code shared by players and enemies: the running character (an enemy: its target)"),
        ("Characters.Abilities", O, "abilities: their owner"),
        ("Characters.Gear", O, "gear (items, skulls, quintessences, inscriptions): their owner"),
        ("Characters.Operations", O, "operations shared by gear and enemies: the running character (an enemy: its target)"),
        ("Characters.Operations.Customs.Emperor", T, "the Emperor boss's operations"),
        ("Characters.Operations.Customs.DarkHero", T, "the dark hero's operations"),
        ("Characters.Projectiles", O, "projectiles: their owner (an enemy's: its target)"),
        ("Characters.Movements", O, "movements: the running character"),
        ("DCDefense", O, "defense mode objects that act for the player who uses them"),
        ("FX", L, "effects and sounds heard by this game's player"),
        ("Level", W, "level and map scripts: the plugin decides"),
        ("Runnables", W, "runnable map scripts and triggers: the plugin decides"),
        ("CutScenes", W, "cutscene glue: each game runs it for its own player unless the plugin decides otherwise"),
    };

    /// <summary>Types and "Type::Method" (source method names) that don't follow their namespace.</summary>
    public static readonly Dictionary<string, (PlayerReadKind kind, string why)> Overrides = new()
    {
        // The global namespace (CameraTargetChanger is caught by the camera pattern).
        ["CastleCameraController"] = (L, "camera"),
        ["SynergyDisplay"] = (L, "HUD"),
        ["CharacterNeverDie"] = (L, "test helper on the local player"),
        ["CloseToPlayer"] = (L, "effect that follows this game's player"),
        ["FollowGameObejct"] = (L, "effect that follows this game's player"),
        ["StickPlayerPosition"] = (L, "effect that follows this game's player"),
        ["SpriteLaser"] = (O, "a laser run for its owner"),
        // The player's own life cycle: spawn, save, load, reset (the drops are World).
        ["Level.LevelManager"] = (L, "this game's player: spawn, save, load, reset"),
        ["Level.LevelManager::DropGold"] = (W, "drops at a player's position: the plugin decides whose"),
        ["Level.LevelManager::DropBone"] = (W, "drops at a player's position: the plugin decides whose"),
        ["Level.LevelManager::DropDarkQuartz"] = (W, "drops at a player's position: the plugin decides whose"),
        ["Level.LevelManager::DropPotion"] = (W, "drops at a player's position: the plugin decides whose"),
        ["Level.Chapter"] = (L, "map change: input block, start position of this game's player"),
        // Per-player pickups and rewards: each game gives its own player.
        ["Level.Potion"] = (L, "per-player pickup"),
        ["Level.DroppedCentauros"] = (L, "per-player pickup"),
        ["Level.DroppedManatechPart"] = (L, "per-player pickup"),
        ["Level.DroppedSuperManatechPart"] = (L, "per-player pickup"),
        ["Level.DroppedSkulHead"] = (L, "per-player pickup"),
        ["Level.DroppedRandomInscriptionItemBox"] = (L, "per-player pickup"),
        ["Level.AdventurerGearReward"] = (L, "per-player reward"),
        ["Level.HardmodeChest"] = (L, "per-player reward"),
        ["Level.RewardLight"] = (L, "reward effect for this game's player"),
        ["Level.MagicMirrorEffect"] = (L, "effect for this game's player"),
        ["Characters.Gear.Upgrades.RebornRecovery"] = (O, "an upgrade's effect on its owner"),
        ["Characters.Gear.Items.Item::OnDestroy"] = (L, "a destroyed gear checks this game's player"),
        ["Characters.Gear.Weapons.Weapon::OnDestroy"] = (L, "a destroyed gear checks this game's player"),
        ["Characters.Gear.Quintessences.Quintessence::OnDestroy"] = (L, "a destroyed gear checks this game's player"),
        ["FX.ThunderLineEffect"] = (O, "the effect runs on its owner's clock"),
        ["Runnables.Triggers.InteractionInput"] = (L, "input of this game"),
        ["Level.Adventurer.AdventurerCameraZone"] = (L, "camera"),
        // Boss scripts: their cinematic locks, intros, outros and cleanups act on each game's own player.
        ["Characters.AI.Chapter3Script/Intro"] = (L, "a boss intro moves this game's player"),
        ["Characters.AI.Chapter3Script::OnDestroy"] = (L, "a boss script's cleanup undoes its locks on this game's player"),
        ["Characters.AI.Chimera.Chimera::OnDestroy"] = (L, "a boss script's cleanup undoes its locks on this game's player"),
        ["Characters.AI.Chimera.DarkAlchemist::OnDestroy"] = (L, "a boss script's cleanup undoes its locks on this game's player"),
        ["Characters.AI.Chimera.EAHero::OnDestroy"] = (L, "a boss script's cleanup undoes its locks on this game's player"),
        ["Characters.AI.TwinSister.Chapter2Script::OnDestroy"] = (L, "a boss script's cleanup undoes its locks on this game's player"),
        ["Characters.AI.TwinSister.Chapter2Script/Intro"] = (L, "a boss intro moves this game's player"),
        ["Characters.AI.TwinSister.Chapter2Script/Outro"] = (L, "a boss outro makes this game's player invulnerable"),
        ["Characters.AI.YggdrasillElderEnt.Chapter1Script::OnDestroy"] = (L, "a boss script's cleanup undoes its locks on this game's player"),
        ["Characters.AI.TwinSister.TwinSisterMasterAI::Start"] = (L, "subscribes to the player's hits; the unsubscribe must find the same player"),
        ["Characters.AI.TwinSister.TwinSisterMasterAI::RemovePlayerHitReaction"] = (L, "subscribes to the player's hits; the unsubscribe must find the same player"),
        ["Characters.AI.Pope.DarkCrystal::CProcess"] = (L, "a boss cinematic on this game's player"),
        // Enemy-only code in shared namespaces.
        ["Characters.EnemyCharacterBehaviorOption"] = (T, "an enemy's behaviour tree target"),
        ["Characters.Movements.AttachToPlayer"] = (T, "an enemy grabs its target"),
        ["Characters.Projectiles.Operations.SpawnRandomEnemy"] = (T, "spawned enemies get the spawner's target"),
        ["Characters.Operations.KnockbackPlayer"] = (T, "enemy operation"),
        ["Characters.Operations.ShiftToPlayer"] = (T, "enemy operation"),
        ["Characters.Operations.TakeAimContinuously"] = (T, "enemy operation"),
        ["Characters.Operations.TakeAimObject"] = (T, "enemy operation"),
        ["Characters.Operations.SpawnCharacter"] = (T, "spawned enemies get the spawner's target"),
        ["Characters.Operations.SpawnRandomCharacter"] = (T, "spawned enemies get the spawner's target"),
        ["Characters.Operations.Customs.AimPlayer"] = (T, "enemy operation"),
        ["Characters.Operations.Customs.BuryPlayer"] = (T, "enemy operation"),
        ["Characters.Operations.Customs.FlipByPlayerPosition"] = (T, "enemy operation"),
        ["Characters.Operations.Customs.MultidimensionalPrism"] = (T, "enemy operation"),
        ["Characters.Operations.Attack.BreakPlayerShield"] = (T, "enemy attack"),
        ["Characters.Operations.Attack.PlayerAttack"] = (T, "enemy attack straight on its target (a client: only the local player)"),
    };

    /// <summary>Classifies one read; false when no rule matched (then it stays Local).</summary>
    public static bool Classify(PlayerReadSite site, out PlayerReadKind kind, out string rule)
    {
        TypeDefinition type = site.ClassType;
        string typeName = type.FullName;
        if (Overrides.TryGetValue(typeName + "::" + site.SourceMethod, out var m))
            return Done(m.kind, $"override {typeName}::{site.SourceMethod}", out kind, out rule);
        if (FeedsCamera(site.Instruction))
            return Done(L, "pattern: camera follows the player", out kind, out rule);
        if (FeedsField(site.Instruction, "cinematic"))
            return Done(L, "pattern: cinematic lock of the player", out kind, out rule);
        if (Overrides.TryGetValue(typeName, out var t))
            return Done(t.kind, $"override {typeName}", out kind, out rule);

        string ns = TopLevel(type).Namespace;
        var best = Namespaces.Where(n => ns == n.ns || ns.StartsWith(n.ns + ".", StringComparison.Ordinal))
            .OrderByDescending(n => n.ns.Length).FirstOrDefault();
        if (best.ns != null)
            return Done(best.kind, $"namespace {best.ns}", out kind, out rule);
        kind = L;
        rule = null;
        return false;
    }

    private static bool Done(PlayerReadKind k, string r, out PlayerReadKind kind, out string rule)
    {
        kind = k;
        rule = r;
        return true;
    }

    /// <summary>The why of a rule, for the list's header.</summary>
    public static string Why(string rule)
    {
        if (rule == null)
            return "no rule";
        if (rule.StartsWith("override "))
            return Overrides.TryGetValue(rule.Substring(9), out var o) ? o.why : "";
        if (rule.StartsWith("namespace "))
            return Namespaces.FirstOrDefault(n => n.ns == rule.Substring(10)).why ?? "";
        return "";
    }

    /// <summary>levelManager.player.transform → cameraController.StartTrack(...).</summary>
    private static bool FeedsCamera(Instruction ins)
    {
        var next = ins.Next;
        if (next == null || !(next.Operand is MethodReference get) || get.Name != "get_transform")
            return false;
        return next.Next?.Operand is MethodReference track && track.Name == "StartTrack";
    }

    private static bool FeedsField(Instruction ins, string field) =>
        ins.Next?.OpCode == OpCodes.Ldfld && ins.Next.Operand is FieldReference f && f.Name == field;

    public static bool IsCompilerGenerated(TypeDefinition type) =>
        type.Name.StartsWith("<") || type.CustomAttributes.Any(a => a.AttributeType.Name == "CompilerGeneratedAttribute");

    /// <summary>The first type outward that isn't made by the compiler (a lambda's or coroutine's class).</summary>
    public static TypeDefinition ClassType(TypeDefinition type)
    {
        while (type.DeclaringType != null && IsCompilerGenerated(type))
            type = type.DeclaringType;
        return type;
    }

    public static TypeDefinition TopLevel(TypeDefinition type)
    {
        while (type.DeclaringType != null)
            type = type.DeclaringType;
        return type;
    }

    /// <summary>"&lt;Awake&gt;b__5_0" → Awake; a coroutine's MoveNext in "&lt;CRun&gt;d__12" → CRun.</summary>
    public static string SourceMethod(MethodDefinition method)
    {
        if (Inner(method.Name) is { } fromMethod)
            return fromMethod;
        for (var t = method.DeclaringType; t != null && IsCompilerGenerated(t); t = t.DeclaringType)
            if (Inner(t.Name) is { } fromType)
                return fromType;
        return method.Name;
    }

    public static string Deferred(MethodDefinition method)
    {
        if (method.Name.Contains(">g__"))
            return "local function";
        if (method.Name.Contains(">b__"))
            return "lambda";
        for (var t = method.DeclaringType; t != null && IsCompilerGenerated(t); t = t.DeclaringType)
            if (t.Name.Contains(">d__"))
                return "coroutine";
        return "";
    }

    private static string Inner(string name)
    {
        int end = name.IndexOf('>');
        return name.StartsWith("<") && end > 1 ? name.Substring(1, end - 1) : null;
    }
}

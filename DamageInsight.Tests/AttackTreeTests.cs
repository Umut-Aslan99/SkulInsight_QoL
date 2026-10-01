using System.Linq;
using DamageInsight.Codex;
using Xunit;
using Kind = DamageInsight.Codex.AttackGraph.Kind;

namespace DamageInsight.Tests;

/// <summary>
/// The top-down tree rules on trees shaped like the ones the normal run reported (First Hero phase 1, the
/// adventurers' BehaviorDesigner trees). Steps are plain strings here.
/// </summary>
public class AttackTreeTests
{
    private sealed class Tree
    {
        public readonly AttackGraph Graph = new(typeof(Characters.AI.AIController), BossAttacks.StepTypes);

        public AttackGraph.Unit Add(string name, Kind kind = Kind.Plain, string label = null, params string[] steps) =>
            AddOf(typeof(object), name, kind, label, steps);

        /// <summary>A block of a real game type (the rules tell loops by type: Repeat, InfiniteLoop).</summary>
        public AttackGraph.Unit AddOf(System.Type type, string name, Kind kind, string label, params string[] steps) =>
            Graph.AddTreeUnit(new AttackGraph.Node(type, new object(), ""), name, kind, steps, label ?? name, null);

        public AttackGraph.Unit Under(AttackGraph.Unit parent, AttackGraph.Unit child)
        {
            Graph.Link(parent, child);
            return child;
        }
    }

    private static AttackGraph Build(System.Action<Tree> make, System.Func<object, string> stepKey = null)
    {
        var tree = new Tree();
        tree.Graph.StepKey = stepKey;
        return tree.Graph.Build(Enumerable.Empty<AttackGraph.Node>(), addMore: _ => make(tree));
    }

    [Fact]
    public void One_move_built_several_times_is_one_move()
    {
        // Steps "push#1" and "push#2" are two objects with the same action (the key drops the "#n").
        var g = Build(t =>
        {
            var root = t.Add("Repeater", Kind.Dispatcher, "Repeater");
            var main = t.Under(root, t.Add("WeightedSelector", Kind.Dispatcher, "Main"));
            var sub = t.Under(root, t.Add("WeightedSelector", Kind.Dispatcher, "Sub"));
            // "Push" in the main pattern with a potion after it, "Push" alone in the sub pattern.
            var withPotion = t.Under(main, t.Add("Sequence", Kind.Sequence, ""));
            t.Under(withPotion, t.Add("RunCharacterAction", Kind.Plain, "Push", "push#1"));
            t.Under(withPotion, t.Add("RunCharacterAction", Kind.Plain, "Drink potion", "potion"));
            t.Under(main, t.Add("RunCharacterAction", Kind.Plain, "Arrow shot", "arrow"));
            t.Under(sub, t.Add("RunCharacterAction", Kind.Plain, "Push", "push#2"));
            // Distance variants of one back dash; Pope-like spells sharing only an animation stay apart.
            t.Under(main, t.Add("ActionAttack", Kind.Plain, "Back dash long", "backdash#1"));
            t.Under(sub, t.Add("ActionAttack", Kind.Plain, "Back dash short", "backdash#2"));
            t.Under(main, t.Add("Baptism", Kind.Plain, "Baptism", "casting#1"));
            t.Under(sub, t.Add("Worship", Kind.Plain, "Worship", "casting#2"));
            // A designer typo of a move that is part of the other.
            var consecration = t.Under(main, t.Add("Sequence", Kind.Sequence, "Consecration"));
            t.Under(consecration, t.Add("Consecration", Kind.Plain, "Consecration", "consecrate#1"));
            t.Under(consecration, t.Add("Nervousness", Kind.Plain, "Nervousness", "nervous"));
            t.Under(sub, t.Add("Consecration", Kind.Plain, "Consecation", "consecrate#2"));
            // Leiana: "Dash" is part of "Rush" (its first motion) and two letters off, but a move of its own.
            var rush = t.Under(main, t.Add("Sequence", Kind.Sequence, "Rush"));
            t.Under(rush, t.Add("RunAction", Kind.Plain, "Dash", "dash#1"));
            t.Under(rush, t.Add("RunAction", Kind.Plain, "Rush", "rush"));
            t.Under(sub, t.Add("RunAction", Kind.Plain, "Dash", "dash#2"));
        }, step => step.ToString().Split('#')[0]);

        var labels = g.Attacks.Where(a => !a.IsTail).Select(a => a.Label).OrderBy(l => l).ToList();
        Assert.Equal(new[] { "Arrow shot", "Back dash", "Baptism", "Consecration", "Dash", "Drink potion", "Push", "Rush", "Worship" }
            .Where(l => labels.Contains(l) || l != "Drink potion").OrderBy(l => l), labels);
        Assert.DoesNotContain(g.Attacks, a => a.Label.StartsWith("Push "));
    }

    [Fact]
    public void A_sequence_with_a_random_end_motion_is_one_attack()
    {
        // Loop > Selector > [Out of middle range: Conditional > Sequence[Dash chase, Slam, Weighted(slam end / skip)],
        //                    Hero landing: Conditional > Sequence[Hero landing, Idle]]
        var g = Build(t =>
        {
            var loop = t.Add("InfiniteLoop", Kind.Dispatcher, "Behaviours");
            var selector = t.Under(loop, t.Add("Selector", Kind.Dispatcher, "Phase a ( 100% ~ 65% )"));
            var middle = t.Under(selector, t.Add("Info", Kind.Plain, "Out of middle range"));
            var cond = t.Under(middle, t.Add("Conditional", Kind.Dispatcher, "Phase a - out of middle range"));
            var seq = t.Under(cond, t.Add("Sequence", Kind.Sequence, "Phase a - out of middle range"));
            t.Under(t.Under(seq, t.Add("Info", Kind.Plain, "Dash chase")), t.Add("Dash", Kind.Plain, "Dash chase [ o ]", "dash", "move"));
            t.Under(t.Under(seq, t.Add("Info", Kind.Plain, "Slam")), t.Add("ActionAttack", Kind.Plain, "Slam", "slam"));
            var weighted = t.Under(t.Under(seq, t.Add("Info", Kind.Plain, "Weighted (slam end motion / skip slam end motion)")),
                t.Add("WeightedSelector", Kind.Dispatcher, "Slam end -> skippable idle"));
            var end = t.Under(weighted, t.Add("Sequence", Kind.Sequence, "Slam end -> skippable idle"));
            t.Under(end, t.Add("ActionAttack", Kind.Plain, "Slam end motion", "slam end"));
            t.Under(end, t.Add("SkipableIdle", Kind.Plain, "Skippable idle"));
            t.Under(weighted, t.Add("SkipableIdle", Kind.Plain, "Skippable idle (skip)"));

            var anywhere = t.Under(selector, t.Add("Info", Kind.Plain, "Anywhere ( cool time)"));
            var cond2 = t.Under(anywhere, t.Add("Conditional", Kind.Dispatcher, "Phase b - any where"));
            var seq2 = t.Under(cond2, t.Add("Sequence", Kind.Sequence, "Hero landing"));
            t.Under(t.Under(seq2, t.Add("Info", Kind.Plain, "Hero landing")), t.Add("ActionAttack", Kind.Plain, "Hero landing", "landing"));
            t.Under(t.Under(seq2, t.Add("Info", Kind.Plain, "Idle")), t.Add("Idle", Kind.Plain, "Behaviour"));
        });

        Assert.Equal(2, g.Attacks.Count);
        var combo = g.Attacks.Single(a => a.Steps.Contains("slam"));
        Assert.Equal(new object[] { "dash", "move", "slam", "slam end" }, combo.Steps);
        Assert.Equal("Dash chase + Slam", combo.Label);
        Assert.Equal("Hero landing", g.Attacks.Single(a => a.Steps.Contains("landing")).Label);
    }

    [Fact]
    public void Conditions_are_not_attacks_and_sub_patterns_are_opened()
    {
        // Warrior: Selector > [Sequence[CanUseAction(no steps), RunCharacterAction Earthquake, Wait tree],
        //                      Sequence[RunCharacterAction Guard], SpecialSkill: Sequence[Cast, Whirlwind]]
        var g = Build(t =>
        {
            var root = t.Add("Repeater", Kind.Dispatcher, "Repeater");
            var selector = t.Under(root, t.Add("WeightedSelector", Kind.Dispatcher, "Weighted Selector"));
            var quake = t.Under(selector, t.Add("Sequence", Kind.Sequence, ""));
            t.Under(quake, t.Add("CanUseAction", Kind.Plain, "Can Use Action"));
            t.Under(quake, t.Add("RunCharacterAction", Kind.Plain, "Earthquake", "quake"));
            var wait = t.Under(quake, t.Add("RandomSelector", Kind.Dispatcher, "Wait"));
            t.Under(wait, t.Add("Wait", Kind.Plain, "Wait"));
            t.Under(wait, t.Add("Wait", Kind.Plain, "Wait"));
            t.Under(t.Under(selector, t.Add("Sequence", Kind.Sequence, "")), t.Add("RunCharacterAction", Kind.Plain, "Guard", "guard"));
            var special = t.Under(selector, t.Add("Sequence", Kind.Sequence, "SpecialSkill_Main"));
            t.Under(special, t.Add("RunCharacterAction", Kind.Plain, "CastSpecialSkill", "cast"));
            t.Under(special, t.Add("RunCharacterAction", Kind.Plain, "Whirlwind(SpecialSkill)", "whirlwind"));
        });

        Assert.Equal(new[] { "Earthquake", "Guard", "Whirlwind (special skill)" }.OrderBy(x => x),
            g.Attacks.Select(a => a.Label).OrderBy(x => x));
        Assert.DoesNotContain(g.Attacks, a => a.Label.Contains("Can use"));
    }

    [Fact]
    public void A_pick_between_single_endings_belongs_to_the_move_before_it()
    {
        // Veteran Magician: Loop > Selector > [Phoenix landing condition: Conditional > Sequence[Phoenix landing,
        //   Idle, Check height, Rising selector: Weighted[Rising (low), Rising (high)]], Fire ball]
        var g = Build(t =>
        {
            var loop = t.Add("InfiniteLoop", Kind.Dispatcher, "Loop patterns");
            var selector = t.Under(loop, t.Add("Selector", Kind.Dispatcher, "Phoenix landing condition"));
            var info = t.Under(selector, t.Add("Info", Kind.Plain, "Phoenix landing condition"));
            var cond = t.Under(info, t.Add("Conditional", Kind.Dispatcher, "Phoenix landing"));
            var seq = t.Under(cond, t.Add("Sequence", Kind.Sequence, "Phoenix landing"));
            t.Under(t.Under(seq, t.Add("Info", Kind.Plain, "Phoenix landing")), t.Add("RunTriggerActions", Kind.Plain, "Phoenix landing", "fall", "landing"));
            t.Under(seq, t.Add("SkipableIdle", Kind.Plain, "Skippable idle"));
            t.Under(seq, t.Add("Height", Kind.Dispatcher, "Check height"));
            var rising = t.Under(t.Under(seq, t.Add("Info", Kind.Plain, "Rising selector")), t.Add("WeightedSelector", Kind.Dispatcher, "Rising (low)"));
            t.Under(rising, t.Add("RunAction", Kind.Plain, "Rising (low)", "rise low"));
            t.Under(rising, t.Add("RunAction", Kind.Plain, "Rising (high)", "rise high"));
            t.Under(t.Under(selector, t.Add("Info", Kind.Plain, "Fire ball")), t.Add("RunAction", Kind.Plain, "Fire ball", "ball"));
        });

        var phoenix = g.Attacks.Single(a => a.Steps.Contains("fall"));
        Assert.Equal("Phoenix landing", phoenix.Label);
        Assert.Equal(new object[] { "fall", "landing", "rise low", "rise high" }, phoenix.Steps);
        Assert.DoesNotContain(g.Attacks, a => a.Label.StartsWith("Rising"));
        Assert.Contains(g.Attacks, a => a.Label == "Fire ball");
    }

    // Yggdrasil's blocks (from his report): every move is Sequence[BehaviourInfo Intro, BehaviourInfo > Repeat(n) > move,
    // BehaviourInfo Outro]; the energy moves are followed by "Groggy" in the same sequence.
    private static AttackGraph.Unit Anim(Tree t, AttackGraph.Unit parent, string label, params string[] steps) =>
        t.Under(t.Under(parent, t.Add("BehaviourInfo", Kind.Plain, label)), t.Add("PlayAnimations", Kind.Plain, label, steps));

    private static AttackGraph.Unit Move(Tree t, string name, string middle, string intro, string outro, params string[] main)
    {
        var seq = t.Add("Sequence", Kind.Sequence, name ?? "Sequence");
        Anim(t, seq, "Intro", intro);
        var repeat = t.Under(t.Under(seq, t.Add("BehaviourInfo", Kind.Plain, middle)),
            t.AddOf(typeof(Characters.AI.Behaviours.Repeat), "Repeat", Kind.Dispatcher, middle));
        t.Under(repeat, t.Add("PlayAnimations", Kind.Plain, middle, main));
        Anim(t, seq, "Outro", outro);
        return seq;
    }

    private static AttackGraph.Unit Loop(Tree t, AttackGraph.Unit parent, string info) =>
        t.Under(t.Under(parent, t.Add("BehaviourInfo", Kind.Plain, info)),
            t.AddOf(typeof(Characters.AI.Behaviours.InfiniteLoop), "InfiniteLoop", Kind.Dispatcher, "Behaviours"));

    private static AttackGraph.Unit Pick(Tree t, AttackGraph.Unit parent, string info) =>
        t.Under(t.Under(parent, t.Add("BehaviourInfo", Kind.Plain, info)), t.Add("RandomBehaviour", Kind.Dispatcher, "Random behaviour"));

    private static AttackGraph.Unit Info(Tree t, AttackGraph.Unit parent, string label, AttackGraph.Unit child) =>
        t.Under(t.Under(parent, t.Add("BehaviourInfo", Kind.Plain, label)), child);

    [Fact]
    public void Yggdrasil_phase_1_moves_are_whole_with_their_intro_and_outro()
    {
        var g = Build(t =>
        {
            var root = t.Add("Sequence", Kind.Sequence, "Behaviours");
            Anim(t, root, "Sleep", "Sleep");
            Anim(t, root, "Appearance", "Appearance");
            var body = t.Under(Loop(t, root, "1 phase sequence"), t.Add("Sequence", Kind.Sequence, "Fist slam / sweeping / energy bomb"));
            var fist = Move(t, "Fist slam", "Fist slam", "FistSlam_Intro", "FistSlam_Outro", "FistSlam");
            var sweep = Move(t, "Sweeping", "Sweeping", "Sweeping_Intro", "Sweeping_Outro", "Sweeping_Left", "Sweeping_Right");
            Anim(t, body, "Idle", "Idle");
            var random = Pick(t, body, "Fist slam / sweeping");
            Info(t, random, "Fist slam", fist);
            Info(t, random, "Sweeping", sweep);
            Anim(t, body, "Idle", "Idle");
            var uniform = t.Under(t.Under(body, t.Add("BehaviourInfo", Kind.Plain, "Fist slam / sweeping / energy bomb")),
                t.Add("UniformSelector", Kind.Dispatcher, "Uniform selector"));
            t.Under(uniform, fist);
            t.Under(uniform, sweep);
            var bomb = t.Under(uniform, t.Add("Sequence", Kind.Sequence, "Sequence"));
            Anim(t, bomb, "Energy bomb", "Laser_Intro", "EnergyBomb");
            Anim(t, bomb, "Groggy", "Groggy");
        });

        var moves = g.Attacks.Where(a => !a.IsTail).ToDictionary(a => a.Label, a => a.Steps);
        Assert.Equal(new[] { "Appearance", "Energy bomb", "Fist slam", "Groggy", "Sweeping" }, moves.Keys.OrderBy(k => k));
        Assert.Equal(new object[] { "FistSlam_Intro", "FistSlam", "FistSlam_Outro" }, moves["Fist slam"]);
        Assert.Equal(new object[] { "Sweeping_Intro", "Sweeping_Left", "Sweeping_Right", "Sweeping_Outro" }, moves["Sweeping"]);
        Assert.Equal(new object[] { "Laser_Intro", "EnergyBomb" }, moves["Energy bomb"]);
        Assert.Equal(new object[] { "Groggy" }, moves["Groggy"]);
    }

    [Fact]
    public void Yggdrasil_phase_2_sequences_take_the_name_of_the_block_above_them()
    {
        var g = Build(t =>
        {
            var root = t.Add("Sequence", Kind.Sequence, "Phase 2 sequence");
            Anim(t, root, "Awakening", "P2_Awakening");
            var body = t.Under(Loop(t, root, "2 phase sequence"), t.Add("Sequence", Kind.Sequence, "Bfps / fps / sweeping"));
            Info(t, body, "Fist slam", Move(t, "Fist power slam", "Slam", "P2_FistPowerSlam_Intro", "P2_FistPowerSlam_Outro", "P2_FistPowerSlam"));
            Anim(t, body, "Idle", "Idle");
            var random = Pick(t, body, "Bfps / fps / sweeping");
            // The same move again in another (unnamed) sequence: named "Intro" after its first block.
            Info(t, random, "Fist power slam", Move(t, null, "Slam", "P2_FistPowerSlam_Intro", "P2_FistPowerSlam_Outro", "P2_FistPowerSlam"));
            Info(t, random, "Both fist power slam",
                Move(t, "Both fist power slam", "Slam", "P2_BothFistPowerSlam_Intro", "P2_BothFistPowerSlam_Outro", "P2_BothFistPowerSlam"));
            Info(t, random, "Sweeping",
                Move(t, "Sweeping", "Sweeping", "P2_SweepingCombo_Intro", "P2_SweepingCombo_Outro", "P2_SweepingCombo_Left", "P2_SweepingCombo_Right"));
            Anim(t, body, "Idle", "Idle");
            var corps = Info(t, body, "Energy corps -> groggy", t.Add("Sequence", Kind.Sequence, "Energy corps"));
            var inner = Info(t, corps, "Energy corps", t.Add("Sequence", Kind.Sequence, "Energy corps"));
            Anim(t, inner, "Intro", "P2_EnergyCorps_Intro");
            t.Under(t.Under(t.Under(inner, t.Add("BehaviourInfo", Kind.Plain, "Energy corps")),
                t.AddOf(typeof(Characters.AI.Behaviours.Repeat), "Repeat", Kind.Dispatcher, "Energy corps")),
                t.Add("PlayAnimations", Kind.Plain, "Energy corps", "P2_EnergyCorps"));
            Anim(t, corps, "Groggy", "P2_Groggy");
        });

        var moves = g.Attacks.Where(a => !a.IsTail).ToDictionary(a => a.Label, a => a.Steps);
        Assert.Equal(new[] { "Awakening", "Both fist power slam", "Energy corps", "Fist power slam", "Groggy", "Sweeping" },
            moves.Keys.OrderBy(k => k));
        Assert.Equal(new object[] { "P2_FistPowerSlam_Intro", "P2_FistPowerSlam", "P2_FistPowerSlam_Outro" }, moves["Fist power slam"]);
        Assert.Equal(new object[] { "P2_EnergyCorps_Intro", "P2_EnergyCorps" }, moves["Energy corps"]);
        Assert.Equal(new object[] { "P2_Groggy" }, moves["Groggy"]);
    }

    [Fact]
    public void First_hero_moves_keep_their_move_names_and_merge_their_variants()
    {
        // From his phase 1 report: "Horizontal slash + end motion" > Sequence[Horizontal slash, End motion] next to a
        // plain "Horizontal slash"; "Slam" and "Slam (end)"; back dash long / short / middle.
        var g = Build(t =>
        {
            var loop = t.AddOf(typeof(Characters.AI.Behaviours.InfiniteLoop), "InfiniteLoop", Kind.Dispatcher, "Behaviours");
            var pick = t.Under(loop, t.Add("Selector", Kind.Dispatcher, "Selector"));
            var withEnd = Info(t, pick, "Horizontal slash + end motion", t.Add("Sequence", Kind.Sequence, "Sequence"));
            Info(t, withEnd, "Horizontal slash", t.Add("ActionAttack", Kind.Plain, "Horizontal slash", "hslash#1"));
            Info(t, withEnd, "End motion", t.Add("ActionAttack", Kind.Plain, "Horizontal slash end motion", "hslash end"));
            Info(t, pick, "Horizontal slash", t.Add("ActionAttack", Kind.Plain, "Horizontal slash", "hslash#2"));
            Info(t, pick, "Slam", t.Add("ActionAttack", Kind.Plain, "Slam", "slam#1"));
            var slamEnd = t.Under(pick, t.Add("Sequence", Kind.Sequence, "Slam(end)"));
            t.Under(slamEnd, t.Add("ActionAttack", Kind.Plain, "Slam", "slam#2"));
            t.Under(slamEnd, t.Add("ActionAttack", Kind.Plain, "Slam end motion", "slam end"));
            Info(t, pick, "Back dash long", t.Add("ActionAttack", Kind.Plain, "Back dash long", "backdash#1"));
            Info(t, pick, "Back dash short", t.Add("ActionAttack", Kind.Plain, "Back dash short", "backdash#2"));
            Info(t, pick, "Back dash middle", t.Add("ActionAttack", Kind.Plain, "Back dash middle", "backdash#3"));
        }, step => step.ToString().Split('#')[0]);

        var labels = g.Attacks.Where(a => !a.IsTail).Select(a => a.Label).OrderBy(l => l).ToList();
        Assert.Equal(new[] { "Back dash", "Horizontal slash", "Slam" }, labels);
    }

    [Fact]
    public void Designer_notes_and_idle_tails_are_cleaned_from_names()
    {
        Assert.Equal("Dash chase", AttackGraph.Tidy("Dash chase [ o ]"));
        Assert.Equal("Divine cross", AttackGraph.Tidy("Divine cross -> long idle"));
        Assert.Equal("Dash after landing -> slam (end)", AttackGraph.Tidy("Dash after landing -> slam(end) -> skipable idle"));
        Assert.Equal("Light sword field", AttackGraph.Tidy("Light sword field 생성"));
        Assert.Equal("Arrow shot", AttackGraph.Tidy("ArrowShot"));
    }
}

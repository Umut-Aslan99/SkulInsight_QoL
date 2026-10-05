using System;
using System.Collections.Generic;
using System.Linq;

using DamageInsight.Lang;

namespace DamageInsight.Describe;

/// <summary>
/// Turns a gear scan into a Breakdown: which hits each part of the gear deals, with base damage,
/// multipliers, hit counts and chances. Plain logic, no Unity, so it runs on saved scans in tests.
/// </summary>
public static class GearAnalyzer
{
    /// <summary>
    /// Category-specific post-processing (labels, triggers, notes), each in its own file
    /// (WeaponRefiner, ItemRefiner, EssenceRefiner). They run in this order after the generic analysis.
    /// </summary>
    private static readonly Action<GearDoc, Breakdown>[] Refiners =
    {
        WeaponRefiner.Refine,
        ItemRefiner.Refine,
        EssenceRefiner.Refine,
        InscriptionRefiner.Refine,
    };

    // Fields that point "outwards" (back to the owner, to other actions, to visuals...), never followed.
    private static readonly HashSet<string> SkipFields = new()
    {
        "_action", "_owner", "_container", "nextLevelReference", "_dropped", "_equipped", "_constraints",
        "_effect", "_effects", "_chronoToGlobe", "_chronoToOwner", "_chronoToTarget", "_layer", "_terrainLayer",
        "_animationInfo", "_sound", "_setItemKeys", "_groupItemKeys", "_stat", "actionsByType",
    };

    // Fields holding a hit's base damage directly (instead of using the nearest AttackDamage).
    private static readonly string[] AmountFields =
    {
        "_additionalDamageAmount", "_baseDamage", "_additionalHitDamage", "_damageAmount", "attackDamage", "_damage", "_attackDamage",
    };

    // Abilities attached to the enemy that compute damage with the enemy's own stats.
    private static readonly HashSet<string> EnemyStatOwners = new() { "HitBomb", "DotDamage" };

    public static Breakdown Analyze(GearDoc doc)
    {
        var b = new Breakdown
        {
            Name = doc.Meta.TryGetValue("name", out var n) ? n : "",
            Category = doc.Meta.TryGetValue("category", out var c) ? c : "",
        };
        var damageSources = doc.Components.Where(x => x.Is("AttackDamage")).ToList();
        var root = damageSources.OrderBy(x => (x.Path ?? "").Length).FirstOrDefault();
        if (!root.IsNull)
        {
            b.BaseMin = root.Num("_minAttackDamage");
            b.BaseMax = root.Num("_maxAttackDamage");
        }
        var walker = new Walker(damageSources);

        switch (b.Category)
        {
            case "weapons":
                AnalyzeWeapon(doc, b, walker);
                break;
            case "items":
                AnalyzeRoot(doc, b, walker, "Item", "Effect");
                break;
            case "essences":
                AnalyzeRoot(doc, b, walker, "Quintessence", "Active");
                break;
            case "inscriptions":
                AnalyzeInscription(doc, b, walker);
                break;
            case "characters":
                AnalyzeCharacter(doc, b, walker);
                break;
            case "linked":
                AnalyzeLinked(doc, b, walker);
                break;
        }
        foreach (var refiner in Refiners)
            refiner(doc, b);
        return b;
    }

    private static void AnalyzeWeapon(GearDoc doc, Breakdown b, Walker walker)
    {
        var skillKeys = doc.Components.Where(x => x.Is("SkillInfo") && x.Path != null)
            .GroupBy(x => x.Path).ToDictionary(g => g.Key, g => g.First().Str("_key") ?? "");

        var actions = doc.Components.Where(x => x.Has("_inputMethod") && x.Has("_type") && x.Has("_priority")).ToList();
        foreach (var kind in new[] { "Basic", "Jump", "Skill", "Swap", "Dash" })
        {
            foreach (var action in actions)
            {
                string type = action.Str("_type");
                string sectionKind = type switch
                {
                    "BasicAttack" => "Basic",
                    "JumpAttack" => "Jump",
                    "Skill" => "Skill",
                    "Swap" => "Swap",
                    "Dash" => "Dash",
                    _ => null,
                };
                if (sectionKind != kind)
                    continue;
                string key = "";
                if (kind == "Skill" && !skillKeys.TryGetValue(action.Path ?? "", out key))
                    continue; // story/intro actions are "skills" without a SkillInfo
                if (kind != "Swap" && action.Str("_inputMethod") == "NotUsed")
                    continue;

                var section = new Section { Kind = kind, Key = key ?? "", Cooldown = Cooldown(action), Path = action.Path ?? "" };
                AddActionSteps(action, section, walker);
                if (section.HasHits || kind == "Skill")
                    b.Sections.Add(section);
            }
        }

        var weapon = doc.Components.FirstOrDefault(x => x.Is("Weapon"));
        if (!weapon.IsNull)
        {
            var passive = new Section { Kind = "Passive" };
            var step = new Step();
            walker.WalkField(weapon, "_abilityAttacher", step);
            walker.WalkField(weapon, "_passiveAbilityAttacher", step);
            passive.Steps.Add(step);
            if (passive.HasHits)
                b.Sections.Add(passive);
        }
    }

    /// <summary>The motions an action plays, in order (combo steps, chain parts or its single motion).</summary>
    private static List<Node> MotionsOf(Node action)
    {
        var motions = new List<Node>();
        if (action.Has("_actionInfo"))
            motions.AddRange(action.List("_actionInfo").Select(info => info.Child("_motion")).Where(m => !m.IsNull));
        var single = action.Child("_motion");
        if (!single.IsNull)
            motions.Add(single);
        motions.AddRange(action.List("_motions"));
        motions.AddRange(action.List("_landingMotions")); // PowerbombChainAction: the landing hit
        // Single motions under their own field: charge stages; the counter hit of a parry (ParryAction, not its waiting
        // stance); the two hits of AttackHitTriggerAction (Minotaurus' stomp); a streak's start and end; a powerbomb's
        // landing (the damage scan found these skipped, docs/DAMAGE_SCAN.md).
        foreach (var field in new[] { "_anticipation", "_charging", "_charged", "_earlyFinish", "_finish", "_parryMotion",
                     "_attackMotion", "_secondMotion", "_startMotion", "_endMotion", "_fullStreakEndMotion", "_landingMotion" })
        {
            var m = action.Child(field);
            if (!m.IsNull)
                motions.Add(m);
        }
        foreach (var entry in action.List("_chargeMotions"))
            motions.AddRange(new[] { entry.Child("charging"), entry.Child("finish") }.Where(m => !m.IsNull));
        motions.AddRange(action.List("_chargingMotions"));
        // GrabAction: the grab that lands and what follows while holding (the Champion's combos hit there); not the miss.
        motions.AddRange(action.List("_grabMotions"));
        motions.AddRange(action.List("_maintainMotions"));
        return motions;
    }

    private static void AddActionSteps(Node action, Section section, Walker walker)
    {
        var plan = StepPlan(action);
        for (int i = 0; i < plan.Count; i++)
        {
            var step = new Step { Label = plan[i].label, IsPart = plan[i].part };
            foreach (var motion in plan[i].motions)
                walker.Walk(motion, step);
            if (i == 0)
                walker.WalkField(action, "_operations", step); // action-level operations run with every motion; count once
            section.Steps.Add(step);
        }
        if (plan.Count == 0)
        {
            var step = new Step();
            walker.WalkField(action, "_operations", step);
            section.Steps.Add(step);
        }
        foreach (var s in section.Steps)
            Collapse(s.Hits);
        // Charge variants often share the same hits (e.g. the anticipation motion); drop empty steps.
        section.Steps.RemoveAll(st => st.Hits.Count == 0 && section.Steps.Count > 1);
        // A chain where only one part deals damage: "Part 3" means nothing to the player.
        if (section.Steps.Count == 1 && section.Steps[0].IsPart)
            section.Steps[0].Label = "";
    }

    /// <summary>
    /// Which motions make up each labelled step of an action: combo hits, chain parts,
    /// or the uncharged/charged releases of charge actions.
    /// </summary>
    private static List<(string label, List<Node> motions, bool part)> StepPlan(Node action)
    {
        var plan = new List<(string, List<Node>, bool)>();
        List<Node> Of(params Node[] nodes) => nodes.Where(n => !n.IsNull).ToList();

        // Charge actions: released early vs. fully charged (ChargeAction).
        if (action.Has("_earlyFinish") && action.Has("_finish"))
        {
            plan.Add((Loc.T("Uncharged"), Of(action.Child("_anticipation"), action.Child("_earlyFinish")), false));
            plan.Add((Loc.T("Charged"), Of(action.Child("_charged"), action.Child("_finish")), false));
            return plan;
        }
        // Several charge levels (MultiChargeAction). The shared wind-up (_anticipation) is left out:
        // it can start the level-specific actions and would mix all levels together.
        if (action.Has("_chargeMotions"))
        {
            plan.Add((Loc.T("Uncharged"), Of(action.Child("_earlyFinish")), false));
            int level = 1;
            foreach (var entry in action.List("_chargeMotions"))
                plan.Add((Loc.F("Charge {0}", level++), Of(entry.Child("finish")), false));
            return plan;
        }
        // Combo where every hit can be charged (ChargeComboAction).
        var infos = action.List("_actionInfo").ToList();
        if (infos.Count > 0 && infos[0].Has("earlyFinish"))
        {
            for (int i = 0; i < infos.Count; i++)
            {
                string hit = infos.Count > 1 ? Loc.F("Hit {0}", i + 1) : Loc.T("Attack");
                plan.Add((hit, Of(infos[i].Child("anticipation"), infos[i].Child("earlyFinish")), false));
                plan.Add((Loc.F("{0} charged", hit), Of(infos[i].Child("charged"), infos[i].Child("finish")), false));
            }
            return plan;
        }
        // Holding to charge: the normal motion(s), plus one extra motion per charge level.
        if (action.Has("_chargingMotions"))
        {
            plan.Add(("", MotionsOf(action), false));
            int level = 1;
            foreach (var motion in action.List("_chargingMotions"))
                plan.Add((Loc.F("Charge {0}", level++), Of(motion), false));
            return plan;
        }

        var motions = MotionsOf(action);
        bool combo = action.Is("ComboAction") && motions.Count > 1;
        for (int i = 0; i < motions.Count; i++)
            plan.Add((combo ? Loc.F("Hit {0}", i + 1) : motions.Count > 1 ? Loc.F("Part {0}", i + 1) : "", Of(motions[i]), !combo && motions.Count > 1));
        return plan;
    }

    private static void AnalyzeRoot(GearDoc doc, Breakdown b, Walker walker, string rootType, string kind)
    {
        var root = doc.Components.FirstOrDefault(x => x.Is(rootType));
        if (root.IsNull)
            return;
        var section = new Section { Kind = kind, Cooldown = Cooldown(root) };
        var step = new Step();
        walker.Walk(root, step, isRoot: true);
        Collapse(step.Hits);
        section.Steps.Add(step);
        b.Sections.Add(section);
    }

    /// <summary>
    /// Inscriptions: one section per field of the inscription component (and its ability) that deals damage,
    /// keyed by field name (e.g. Brawl "_operations" = shockwave, "_enhanceOperations" = every 5th shockwave).
    /// InscriptionRefiner maps them to steps.
    /// </summary>
    /// <summary>Inscription fields that hold several separate attacks, analyzed one sub-field at a time.</summary>
    private static readonly HashSet<(string, string)> SplitInscriptionFields = new() { ("Arms", "_additionalHit") };

    private static void AnalyzeInscription(GearDoc doc, Breakdown b, Walker walker)
    {
        var root = doc.Components.FirstOrDefault(x => x.Path == "" && x.Type.Contains(".Inscriptions."));
        if (root.IsNull)
            return;
        var owners = new List<Node> { root };
        var ability = root.Child("_ability");
        if (!ability.IsNull)
            owners.Add(ability);
        foreach (var owner in owners)
        {
            foreach (var (name, value) in owner.Fields())
            {
                if (name == "_ability" || !(value is Dictionary<string, object> || value is List<object>))
                    continue;
                if (SplitInscriptionFields.Contains((b.Name, name)))
                {
                    // One section per sub-list (e.g. Arms: normal / enhanced / true form, ground / air).
                    var holder = owner.Child(name);
                    foreach (var (subName, subValue) in holder.Fields())
                    {
                        if (!(subValue is Dictionary<string, object> || subValue is List<object>))
                            continue;
                        var sub = new Section { Kind = "Inscription", Key = name + "/" + subName };
                        var subStep = new Step();
                        walker.WalkField(holder, subName, subStep);
                        Collapse(subStep.Hits);
                        sub.Steps.Add(subStep);
                        if (sub.HasHits)
                            b.Sections.Add(sub);
                    }
                    continue;
                }
                var section = new Section { Kind = "Inscription", Key = name };
                var step = new Step();
                walker.WalkField(owner, name, step);
                Collapse(step.Hits);
                section.Steps.Add(step);
                if (section.HasHits)
                    b.Sections.Add(section);
            }
        }
    }

    /// <summary>
    /// Summoned characters (turrets, companions, buddies): one "Summon" section per action that deals damage,
    /// keyed by the action's object name (e.g. "Attack", "FastAttack").
    /// </summary>
    private static void AnalyzeCharacter(GearDoc doc, Breakdown b, Walker walker)
    {
        foreach (var action in doc.Components.Where(x => x.Has("_inputMethod") && x.Has("_type") && x.Has("_priority")))
        {
            var section = new Section { Kind = "Summon", Key = action.GameObject ?? "", Cooldown = Cooldown(action) };
            AddActionSteps(action, section, walker);
            if (section.HasHits)
                b.Sections.Add(section);
        }
    }

    /// <summary>
    /// Prefabs other gear loads through an AssetReference (e.g. Fairy Tale's Oberon): one "Summon" section per
    /// damaging field of the root component. A field "_xOperationRunner" takes its cooldown from "_xCooldown".
    /// </summary>
    /// <summary>Readable names for the parts of linked prefabs (Oberon's three attacks).</summary>
    private static readonly Dictionary<string, string> LinkedTitles = new()
    {
        ["_attackOperationRunner"] = Loc.N("Galaxy beam"),
        ["_thunderOperationRunner"] = Loc.N("Spirit thunder"),
        ["_bombOperationRunner"] = Loc.N("Spirit bomb"),
    };

    private static void AnalyzeLinked(GearDoc doc, Breakdown b, Walker walker)
    {
        var root = doc.Components.FirstOrDefault(x => x.Path == "");
        if (root.IsNull)
            return;
        foreach (var (name, value) in root.Fields())
        {
            if (!(value is Dictionary<string, object> || value is List<object>))
                continue;
            string stem = name.Replace("OperationRunner", "").Replace("Operations", "");
            var section = new Section { Kind = "Summon", Key = name, Cooldown = root.Num(stem + "Cooldown") };
            if (LinkedTitles.TryGetValue(name, out var title))
                section.Title = section.Cooldown > 0
                    ? Loc.F("{0} (every {1} s)", Loc.T(title), section.Cooldown.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture))
                    : Loc.T(title);
            var step = new Step();
            walker.WalkField(root, name, step);
            Collapse(step.Hits);
            section.Steps.Add(step);
            if (section.HasHits)
                b.Sections.Add(section);
        }
    }

    private static double Cooldown(Node owner)
    {
        var cd = owner.Child("_cooldown");
        return !cd.IsNull && cd.Str("_type") == "Time" ? cd.Num("_cooldownTime") : 0;
    }

    /// <summary>Merges identical hits into one with a higher count ("3 × 8–12").</summary>
    private static void Collapse(List<Hit> hits)
    {
        for (int i = hits.Count - 1; i > 0; i--)
        {
            for (int j = 0; j < i; j++)
            {
                if (!hits[j].SameAs(hits[i]) || hits[j].Count == 0 || hits[i].Count == 0)
                    continue;
                hits[j].Count += hits[i].Count;
                hits.RemoveAt(i);
                break;
            }
        }
    }

    /// <summary>Depth-first walk over the object graph, emitting a Hit for every HitInfo it meets.</summary>
    private sealed class Walker
    {
        private readonly List<(string path, double min, double max)> _damageSources;

        public Walker(List<Node> damageSources)
        {
            _damageSources = damageSources
                .Select(x => (x.Path ?? "", x.Num("_minAttackDamage"), x.Num("_maxAttackDamage")))
                .OrderByDescending(x => x.Item1.Length)
                .ToList();
        }

        private struct Ctx
        {
            public double BaseMin, BaseMax;
            public string BaseSource;
            public bool BaseLocked;
            public double MultMin, MultMax;
            public int Count;
            public double Chance;
            public string LastPath; // hierarchy path of the last gear component on the way (for hits in referenced prefabs)
        }

        private static Ctx Start => new() { MultMin = 1, MultMax = 1, Count = 1, Chance = 1, BaseSource = "" };

        public void Walk(Node node, Step step, bool isRoot = false) =>
            Visit(node, Start, step, new HashSet<object>(), isRoot);

        public void WalkField(Node owner, string field, Step step)
        {
            var visited = new HashSet<object>();
            var ctx = WithPathBase(Start, owner.Path);
            object raw = owner.Raw(field);
            if (raw is Dictionary<string, object>)
                Visit(owner.Child(field), ctx, step, visited, false);
            else if (raw is List<object>)
                foreach (var child in owner.List(field))
                    Visit(child, ctx, step, visited, false);
        }

        private Ctx WithPathBase(Ctx ctx, string path)
        {
            if (path == null || ctx.BaseLocked)
                return ctx;
            // Unity's GetComponentInParent: the AttackDamage on the nearest ancestor (or the object itself).
            foreach (var (p, min, max) in _damageSources)
            {
                if (p.Length == 0 || path == p || path.StartsWith(p + "/"))
                {
                    ctx.BaseMin = min;
                    ctx.BaseMax = max;
                    ctx.BaseSource = p.Length == 0 ? "gear" : "gear:" + p;
                    return ctx;
                }
            }
            return ctx;
        }

        private void Visit(Node node, Ctx ctx, Step step, HashSet<object> visited, bool isRoot)
        {
            if (node.IsNull || !visited.Add(node.Data))
                return;
            string type = node.ShortType;
            bool startsAction = type is "DoAction" or "RunAction" or "TriggerActionStart";
            if (!isRoot && !startsAction && (type == "Weapon" || type == "Item" || type == "Quintessence" || type.EndsWith("Action")))
                return; // another gear or action: not part of this hit list
            if (type == "$character")
                return; // summoned character: handled separately (minions)
            // A reference to a whole object that contains the gear itself (e.g. the Werewolf's Hunt has a ToObject
            // operation targeting the skull's own object): it lists every component of the gear, so following it
            // would pull every other action's hits into this one.
            if (!isRoot && node.Has("$prefab") && node.List("components").Any(c => c.Path == ""))
                return;

            ctx = WithPathBase(ctx, node.Path);
            if (node.Path != null)
                ctx.LastPath = node.Path;

            // Operations that start another action (e.g. a skill that triggers a hidden "DoAction"):
            // that action's hits belong to this one.
            if (startsAction)
            {
                var target = node.Child("_action");
                // A link back up to an enclosing action (e.g. TriggerActionStart on the parent skill, used for its
                // cooldown) is not a new attack; following it would pull in every charge level.
                bool backLink = !target.IsNull && target.Path != null && node.Path != null && node.Path.StartsWith(target.Path + "/");
                if (!target.IsNull && !backLink && visited.Add(target.Data))
                {
                    var targetCtx = WithPathBase(ctx, target.Path);
                    foreach (var motion in MotionsOf(target))
                        Visit(motion, targetCtx, step, visited, false);
                    foreach (var op in target.List("_operations"))
                        Visit(op, targetCtx, step, visited, false);
                }
                return;
            }

            switch (type)
            {
                case "Repeater":
                {
                    double times = node.Num("_times");
                    // 0 or a huge number (the game uses int.MaxValue-ish values) means "keeps repeating".
                    ctx.Count = times <= 0 || times >= 10000 || ctx.Count == 0 ? 0 : ctx.Count * (int)times;
                    Visit(node.Child("_toRepeat"), ctx, step, visited, false);
                    return;
                }
                case "Repeater2":
                case "Repeater3":
                {
                    int times = Math.Max(1, node.Numbers("_timesToTrigger").Count);
                    ctx.Count *= times;
                    break; // children are walked generically below
                }
                case "Chance":
                    ctx.Chance *= node.Num("_successChance", 1);
                    break;
            }

            // Projectile launchers: damage × launcher multiplier, once per direction.
            if (node.Has("_projectile") && node.Has("_damageMultiplier"))
            {
                var (lo, hi) = CustomFloat(node.Child("_damageMultiplier"), 1);
                ctx.MultMin *= lo;
                ctx.MultMax *= hi;
                int directions = node.List("_directions").Count();
                if (directions == 0)
                    directions = node.Numbers("_directions").Count;
                ctx.Count *= Math.Max(1, directions);
            }

            // Operation runners summoned without copying the weapon's damage use their own AttackDamage.
            if (node.Has("_copyAttackDamage") && !node.Bool("_copyAttackDamage"))
            {
                var runnerDamage = node.Child("_operationRunner").Child("_attackDamage");
                if (!runnerDamage.IsNull)
                {
                    ctx.BaseMin = runnerDamage.Num("_minAttackDamage");
                    ctx.BaseMax = runnerDamage.Num("_maxAttackDamage");
                    ctx.BaseSource = "runner";
                    ctx.BaseLocked = true;
                }
            }

            foreach (var (name, value) in node.Fields())
            {
                if (SkipFields.Contains(name))
                    continue;
                if (value is Dictionary<string, object>)
                {
                    var child = node.Wrap(value);
                    if (child.Is("HitInfo"))
                        Emit(node, child, ctx, step);
                    else
                        Visit(child, ctx, step, visited, false);
                }
                else if (value is List<object> list)
                {
                    foreach (var child in node.Nodes(list))
                    {
                        if (child.Is("HitInfo"))
                            Emit(node, child, ctx, step);
                        else
                            Visit(child, ctx, step, visited, false);
                    }
                }
            }
        }

        private static void Emit(Node owner, Node hitInfo, Ctx ctx, Step step)
        {
            string attackType = hitInfo.Str("_type") ?? "Melee";
            double mult = hitInfo.Num("_damageMultiplier", 1);
            if (attackType == "None" || mult == 0)
                return;

            double baseMin = ctx.BaseMin, baseMax = ctx.BaseMax;
            string source = ctx.BaseSource;
            if (!ctx.BaseLocked && TryOwnAmount(owner, out double ownMin, out double ownMax, out string field))
            {
                baseMin = ownMin;
                baseMax = ownMax;
                source = "field:" + field;
            }
            if (baseMax <= 0)
            {
                // No AttackDamage of its own: the game uses the nearest one above it at runtime,
                // which for an equipped item is the current skull's damage.
                baseMin = baseMax = 0;
                source = "skull";
            }

            step.Hits.Add(new Hit
            {
                BaseMin = baseMin,
                BaseMax = baseMax,
                BaseSource = source,
                MultMin = mult * ctx.MultMin,
                MultMax = mult * ctx.MultMax,
                Attribute = hitInfo.Str("_attribute") ?? "Physical",
                MotionType = hitInfo.Str("_motionType") ?? "Basic",
                AttackType = attackType,
                Count = ctx.Count,
                Chance = ctx.Chance,
                AdaptiveForce = owner.Bool("_adaptiveForce"),
                UsesEnemyStats = EnemyStatOwners.Contains(owner.ShortType),
                Owner = owner.ShortType,
                OwnerPath = owner.Path ?? ctx.LastPath ?? "",
                Key = hitInfo.Str("_key") ?? "",
            });
        }

        /// <summary>Some abilities carry their own base amount (a number, CustomFloat or AttackDamage).</summary>
        private static bool TryOwnAmount(Node owner, out double min, out double max, out string field)
        {
            foreach (var f in AmountFields)
            {
                object raw = owner.Raw(f);
                field = f;
                switch (raw)
                {
                    case double d when d > 0:
                        min = max = d;
                        return true;
                    case Dictionary<string, object>:
                    {
                        var n = owner.Child(f);
                        if (n.Has("_minAttackDamage"))
                        {
                            min = n.Num("_minAttackDamage");
                            max = n.Num("_maxAttackDamage");
                            return max > 0;
                        }
                        if (n.Is("CustomFloat") || n.Is("CustomDouble"))
                        {
                            (min, max) = CustomFloat(n, 0);
                            return max > 0;
                        }
                        break;
                    }
                }
            }
            min = max = 0;
            field = "";
            return false;
        }

        /// <summary>CustomFloat: Constant → _value; random range → _value.._maxValue.</summary>
        private static (double min, double max) CustomFloat(Node n, double fallback)
        {
            if (n.IsNull)
                return (fallback, fallback);
            double v = n.Num("_value", fallback);
            if (n.Str("_type") == "Constant")
                return (v, v);
            double max = n.Num("_maxValue", v);
            return max >= v ? (v, max) : (max, v);
        }
    }
}

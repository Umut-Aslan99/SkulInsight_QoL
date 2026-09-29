using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Characters.Abilities;

namespace DamageInsight.UI;

/// <summary>
/// How many seconds are left on an ability icon. The game only exposes the icon's fill amount, and every
/// ability keeps its timer in its own field (_remainCooldownTime, _remainBuffDuration, _summonRemainCooldown...),
/// so find that field by name once per type: a float/double with "remain" in its name, cooldowns first; else
/// the instance's own remainTime (buff durations).
/// </summary>
public static class CooldownReader
{
    private static readonly Dictionary<Type, FieldInfo> Fields = new();

    public static double Remaining(IAbilityInstance instance)
    {
        if (instance == null)
            return 0;
        float fill = instance.iconFillAmount;
        if (!(fill > 0.001f && fill < 0.999f)) // nothing is ticking (ready, permanent, or not a timer)
            return 0;

        var field = FieldFor(instance.GetType());
        double value = field != null ? Convert.ToDouble(field.GetValue(instance)) : instance.remainTime;
        return value > 0 && value < 100000 ? value : 0;
    }

    private static FieldInfo FieldFor(Type type)
    {
        if (Fields.TryGetValue(type, out var cached))
            return cached;

        var candidates = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            candidates.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => (f.FieldType == typeof(float) || f.FieldType == typeof(double))
                            && f.Name.IndexOf("remain", StringComparison.OrdinalIgnoreCase) >= 0));
        var field = candidates.FirstOrDefault(f => f.Name.IndexOf("cooldown", StringComparison.OrdinalIgnoreCase) >= 0)
                    ?? candidates.FirstOrDefault();
        Fields[type] = field;
        return field;
    }
}

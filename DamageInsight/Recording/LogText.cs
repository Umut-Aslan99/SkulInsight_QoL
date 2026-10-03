using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Characters;
using DamageInsight.Lang;

namespace DamageInsight.Recording;

/// <summary>Turns recorded hits into the combat log's rich text. Plain logic, no Unity, so it can be unit tested.</summary>
public static class LogText
{
    public static string EmptyMessage => Loc.T("No damage matches the current filters yet.");

    /// <param name="lineMap">If given, filled with the index in <paramref name="shown"/> of every text line (-1 for other lines).</param>
    public static string Build(IReadOnlyList<DamageRecord> shown, IReadOnlyList<RoomInfo> rooms, int maxLines, List<int> lineMap = null)
    {
        lineMap?.Clear();
        if (shown.Count == 0)
        {
            lineMap?.Add(-1);
            return $"<color=#8A8290>{EmptyMessage}</color>";
        }

        var sb = new StringBuilder();
        int start = Math.Max(0, shown.Count - maxLines);
        if (start > 0)
        {
            sb.Append($"<color=#8A8290>{Loc.P("… {0} older entry hidden (showing the last {1})", "… {0} older entries hidden (showing the last {1})", start, maxLines)}</color>\n");
            lineMap?.Add(-1);
        }

        int lastRoom = -1;
        for (int i = start; i < shown.Count; i++)
        {
            var r = shown[i];
            if (r.Room != lastRoom)
            {
                lastRoom = r.Room;
                string label = rooms.FirstOrDefault(room => room.Index == r.Room).Label ?? "";
                sb.Append(RoomHeader(r.Room, label)).Append('\n');
                lineMap?.Add(-1);
            }
            sb.Append(Line(r)).Append('\n');
            lineMap?.Add(i);
        }
        return sb.ToString();
    }

    /// <summary>Removes rich-text tags, e.g. for plain log files.</summary>
    public static string Plain(string richText) =>
        System.Text.RegularExpressions.Regex.Replace(richText, "<[^>]+>", "");

    public static string RoomHeader(int room, string label) =>
        $"<color=#C9A86A>—— {Loc.F("Room {0}", room)}  <size=80%>{label}</size> ——</color>";

    /// <summary>
    /// Short line for the mini log, e.g. "45 Skill » Ent". The number is coloured by damage type.
    /// Every colour carries <paramref name="alpha"/> (0..1), so older lines can be drawn fainter.
    /// </summary>
    public static string CompactLine(in DamageRecord r, float alpha = 1f)
    {
        string a = ((int)Math.Round(Clamp01(alpha) * 255)).ToString("X2");
        string C(string hex) => $"<color={hex}{a}>";
        string amount = $"{C(DamageSources.AttributeColorHex(r.Attribute))}{r.Amount:N0}</color>";
        string source = $"{C(DamageSources.ColorHex(r.Source))}{DamageSources.Title(r.Source)}</color>";
        string crit = r.Critical ? $" {C("#FFE14D")}{Loc.T("CRIT")}</color>" : "";

        if (r.ByPlayer && !r.ToPlayer)
            return $"{C("#F2E9D8")}{amount} {source}{crit} » {r.Target}</color>";
        if (r.ToPlayer)
            return $"{C("#FF8080")}-{amount} {source}{crit} « {r.Attacker}</color>";
        return $"{C("#C8C0B8")}{r.Attacker} » {r.Target} {amount} {source}{crit}</color>";
    }

    private static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    /// <summary>"X dealt N SOURCE TYPE damage to Y", with colours.</summary>
    public static string Line(in DamageRecord r)
    {
        // Your hits are named after what dealt them ("Shadow Spirit Death dealt …"), unless that just repeats the source.
        string origin = r.Trace?.Origin ?? "";
        bool named = origin.Length > 0 && origin != DamageSources.Title(r.Source);
        string attacker = r.AttackerKind == EntityKind.Player ? $"<color=#FFE9A8>{(named ? origin : Loc.T("You"))}</color>"
            : named ? $"{r.Attacker} <color=#8A8290>({origin})</color>" : r.Attacker;
        string target = r.TargetKind == EntityKind.Player ? $"<color=#FF6060>{Loc.T("You")}</color>" : r.Target;
        string attribute = $"<color={DamageSources.AttributeColorHex(r.Attribute)}>{DamageInsight.Describe.DescriptionFormatter.AttributeName(r.Attribute.ToString())}</color>";
        string crit = r.Critical ? $" <color=#FFE14D>{Loc.T("CRIT")}</color>" : "";
        return $"<color=#8A8290>{r.Time,6:0.0}s</color>  " + Loc.F("{0} dealt {1} {2} {3} damage to {4}",
                   attacker, $"<color=#FFFFFF>{r.Amount:N0}</color>", $"<color={DamageSources.ColorHex(r.Source)}>{DamageSources.Title(r.Source)}</color>",
                   attribute, target) + crit;
    }
}

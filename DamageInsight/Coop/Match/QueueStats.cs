using System;
using System.Globalization;
using DamageInsight.Describe;

namespace DamageInsight.Coop.Match;

/// <summary>
/// The matchmaking queue as everyone may see it (GET /api/coop/stats; counts only): who is searching now and for which
/// mode, and how many players were in the queue per UTC hour of the day over the last days. The Co-op window shows it
/// before anyone queues (Umut 2026-11-23: "how many people are searching and which mode, even before queuing up and
/// maybe the busiest time compared to their timezone on pc or UTC"). Plain logic, tested.
/// </summary>
public sealed class QueueStats
{
    public int Looking, InMatch, Normal, DarkMirror, Any, MatchesLastHour, HoursDays;
    /// <summary>Players per UTC hour of the day (index 0 = 00:00-01:00 UTC), or null (an older server).</summary>
    public int[] HoursUtc;

    /// <summary>null when the text is no stats answer (the server off: 404 "coop_off").</summary>
    public static QueueStats Parse(string json)
    {
        Node n;
        try { n = string.IsNullOrWhiteSpace(json) ? default : GearDoc.ParseNode(json); }
        catch (Exception) { return null; }
        if (n.IsNull || n.Raw("looking") is not double)
            return null;
        var mode = n.Child("byMode");
        var stats = new QueueStats
        {
            Looking = (int)n.Num("looking"),
            InMatch = (int)n.Num("inMatch"),
            MatchesLastHour = (int)n.Num("matchesLastHour"),
            Normal = mode.IsNull ? 0 : (int)mode.Num("normal"),
            DarkMirror = mode.IsNull ? 0 : (int)mode.Num("darkmirror"),
            Any = mode.IsNull ? 0 : (int)mode.Num("any"),
            HoursDays = (int)n.Num("hoursDays"),
        };
        var hours = n.Numbers("hoursUtc");
        if (hours != null && hours.Count == 24)
        {
            stats.HoursUtc = new int[24];
            for (int h = 0; h < 24; h++)
                stats.HoursUtc[h] = Math.Max(0, (int)hours[h]);
        }
        return stats;
    }

    /// <summary>The UTC hour of the day with the most players (the earliest of equals), or -1 when nobody came.</summary>
    public int BusiestUtcHour
    {
        get
        {
            if (HoursUtc == null)
                return -1;
            int best = -1;
            for (int h = 0; h < 24; h++)
                if (HoursUtc[h] > 0 && (best < 0 || HoursUtc[h] > HoursUtc[best]))
                    best = h;
            return best;
        }
    }

    /// <summary>A UTC hour of the day as this PC's local hour span: "19:00-20:00" (with an en dash). The offset of
    /// <paramref name="utcNow"/>'s day is used (daylight saving as it is today).</summary>
    public static string LocalHourSpan(int utcHour, DateTime utcNow, TimeZoneInfo zone = null)
    {
        var start = new DateTime(utcNow.Year, utcNow.Month, utcNow.Day, 0, 0, 0, DateTimeKind.Utc).AddHours(utcHour);
        var local = TimeZoneInfo.ConvertTimeFromUtc(start, zone ?? TimeZoneInfo.Local);
        int from = local.Hour, to = (local.Hour + 1) % 24;
        return from.ToString("00", CultureInfo.InvariantCulture) + ":" + local.Minute.ToString("00", CultureInfo.InvariantCulture) + "–"
               + to.ToString("00", CultureInfo.InvariantCulture) + ":" + local.Minute.ToString("00", CultureInfo.InvariantCulture);
    }
}

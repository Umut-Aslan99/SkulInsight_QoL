using System;
using System.Globalization;
using System.Text;

namespace DamageInsight.Coop.Rules;

/// <summary>What kind of enemy, for the health multiplier and the target lock (§4.1, §4.7).</summary>
public enum EnemyCategory : byte
{
    Normal = 0,
    /// <summary>Elites and mini-bosses (3 s target lock, their own health column).</summary>
    Elite = 1,
    /// <summary>Bosses: the threat table instead of the target lock.</summary>
    Boss = 2,
}

/// <summary>
/// Enemy health multipliers by player count (§4.7): "Per-player rewards make each player about as strong as alone.
/// Two players deal almost twice the damage, and enemies split their attention. So enemies need much more health than
/// +25%." Starting values, to be tuned with run-history room times; the host's values decide, and the server can send
/// recommended ones (<see cref="TryParse"/> reads the same short text <see cref="ToText"/> writes).
/// One player is always x 1.0 (solo play is never changed).
/// </summary>
public sealed class BalanceSettings
{
    public const int MaxPlayers = 4;
    public const float MinMultiplier = 0.5f, MaxMultiplier = 10f;

    /// <summary>The plan's table: rows 2, 3, 4 players; columns normal, elite / mini-boss, boss.</summary>
    public static readonly float[,] Defaults =
    {
        { 1.6f, 1.7f, 1.8f },
        { 2.2f, 2.4f, 2.6f },
        { 2.8f, 3.1f, 3.4f },
    };

    private readonly float[,] _table = (float[,])Defaults.Clone();

    /// <summary>The slider's range and default (Coop/EnemyHealth, Umut 2026-11-21: "1.6x for 2 players felt too weak
    /// ... a slider that goes from 1.5x - 3x or so, put it at 2x as default"; 2026-11-23: "Slider min should be 1.5,
    /// slider max 4.5. Default is x2": skilled players each deal a whole player's damage). The 0.17.0 numbers are kept
    /// as a backup in docs/coop/BALANCE_BACKUP.md (max 3x, the same table).</summary>
    public const float MinTwoPlayers = 1.5f, MaxTwoPlayers = 4.5f, DefaultTwoPlayers = 2f;

    /// <summary>One click of the Co-op window's arrows (the host's: the setting itself).</summary>
    public const float WindowStep = 0.25f;

    /// <summary>The slider value one arrow click up or down: the next quarter step, kept in the slider's range
    /// (2.3 goes up to 2.5 and down to 2.25).</summary>
    public static float StepTwoPlayers(float value, int direction)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
            value = DefaultTwoPlayers;
        double steps = value / WindowStep;
        double next = direction > 0 ? Math.Floor(steps + 1e-4) + 1 : direction < 0 ? Math.Ceiling(steps - 1e-4) - 1 : steps;
        return Math.Max(MinTwoPlayers, Math.Min(MaxTwoPlayers, (float)(next * WindowStep)));
    }

    /// <summary>The slider's value as a whole number of hundredths (2.0 = 200): what a request or an OK is about.</summary>
    public static int Hundredths(float value) =>
        float.IsNaN(value) || float.IsInfinity(value) ? 0 : (int)Math.Round(value * 100.0, MidpointRounding.AwayFromZero);

    /// <summary>Sets the level of the whole table by its first value (two players, normal enemies): every other value
    /// keeps its ratio to it, so the table's shape (the plan's defaults, or a table the server recommends) stays.</summary>
    public void ScaleTo(float twoPlayers)
    {
        float target = CoopRulesSettings.Range(twoPlayers, MinTwoPlayers, MaxTwoPlayers, DefaultTwoPlayers);
        float first = _table[0, 0] > 0f ? _table[0, 0] : Defaults[0, 0];
        float k = target / first;
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                _table[r, c] = CoopRulesSettings.Range(_table[r, c] * k, MinMultiplier, MaxMultiplier, Defaults[r, c]);
    }

    /// <summary>The multiplier for this many players (clamped to 1-4; 1 = x 1.0) and this kind of enemy.</summary>
    public float Get(int players, EnemyCategory category)
    {
        players = CoopRulesSettings.Range(players, 1, MaxPlayers);
        if (players == 1)
            return 1f;
        return _table[players - 2, Column(category)];
    }

    /// <summary>Overrides one value (2-4 players; 1 player can't be changed). Returns false when ignored.</summary>
    public bool Set(int players, EnemyCategory category, float multiplier)
    {
        if (players < 2 || players > MaxPlayers)
            return false;
        int c = Column(category);
        _table[players - 2, c] = CoopRulesSettings.Range(multiplier, MinMultiplier, MaxMultiplier, Defaults[players - 2, c]);
        return true;
    }

    public void ResetToDefaults()
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                _table[r, c] = Defaults[r, c];
    }

    public void Clamp()
    {
        for (int r = 0; r < 3; r++)
            for (int c = 0; c < 3; c++)
                _table[r, c] = CoopRulesSettings.Range(_table[r, c], MinMultiplier, MaxMultiplier, Defaults[r, c]);
    }

    /// <summary>The table as one short text, rows 2/3/4 players: "1.6/1.7/1.8; 2.2/2.4/2.6; 2.8/3.1/3.4".</summary>
    public string ToText()
    {
        var sb = new StringBuilder();
        for (int r = 0; r < 3; r++)
        {
            if (r > 0)
                sb.Append("; ");
            for (int c = 0; c < 3; c++)
            {
                if (c > 0)
                    sb.Append('/');
                sb.Append(_table[r, c].ToString("0.###", CultureInfo.InvariantCulture));
            }
        }
        return sb.ToString();
    }

    /// <summary>Reads a table written by <see cref="ToText"/> (a config value or the server's recommendation). All
    /// or nothing: on any error the table stays as it was and false comes back. Values are clamped.</summary>
    public bool TryParse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] rows = text.Split(';');
        if (rows.Length != 3)
            return false;
        var parsed = new float[3, 3];
        for (int r = 0; r < 3; r++)
        {
            string[] cells = rows[r].Split('/');
            if (cells.Length != 3)
                return false;
            for (int c = 0; c < 3; c++)
            {
                if (!float.TryParse(cells[c].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ||
                    float.IsNaN(v) || float.IsInfinity(v))
                    return false;
                parsed[r, c] = CoopRulesSettings.Range(v, MinMultiplier, MaxMultiplier, Defaults[r, c]);
            }
        }
        Array.Copy(parsed, _table, parsed.Length);
        return true;
    }

    private static int Column(EnemyCategory category) => category switch
    {
        EnemyCategory.Elite => 1,
        EnemyCategory.Boss => 2,
        _ => 0,
    };
}

/// <summary>
/// Enemy health in co-op (§4.7): the enemy's own max health x the multiplier for the party size. Enemy damage stays
/// as it is (each player has their own health). [DECISION] The party size is the number of connected players when the
/// enemy spawns; it isn't changed when someone leaves mid-room.
/// </summary>
public sealed class CoopBalance
{
    private readonly BalanceSettings _settings;

    public CoopBalance(BalanceSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _settings.Clamp();
    }

    /// <summary>The health multiplier; player counts outside 1-4 are clamped, 1 player = 1.0.</summary>
    public float HealthMultiplier(int players, EnemyCategory category) => _settings.Get(players, category);

    /// <summary>The co-op max health for an enemy with this solo max health.</summary>
    public double ScaledHealth(double soloHealth, int players, EnemyCategory category) =>
        soloHealth * HealthMultiplier(players, category);
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace DamageInsight.Coop.Testing;

/// <summary>Which side of a co-op test session a game copy starts as (<c>--skulinsight-coop</c>).</summary>
public enum CoopRole { None, Host, Join }

/// <summary>The transport a game copy is told to use (<c>--skulinsight-transport</c>); None = not given.</summary>
public enum CoopTransportKind { None, Udp, Steam, Loopback }

/// <summary>
/// The start options for co-op testing with several game copies on one PC (tools/coop/start-instances.ps1 passes them):
/// which copy this is, host or join, the transport, the port, a simulated bad connection and a test bot. Read once from
/// the game's command line (<see cref="Current"/>); normal players never pass any, so for them this is the normal game
/// (instance 1, nothing else set). Unknown arguments (Unity's own, Steam's) are ignored; a bad value keeps the default.
/// Both "--skulinsight-port 27960" and "--skulinsight-port=27960" work, names in any case.
/// </summary>
public sealed class CoopLaunch
{
    public const string Prefix = "--skulinsight-";
    /// <summary>The host's UDP port on 127.0.0.1 unless --skulinsight-port says otherwise (the same as UdpTransport's).</summary>
    public const int DefaultPort = 27960;
    public const int MaxInstance = 4;

    private static CoopLaunch _current;

    /// <summary>This game's start options, parsed once from Environment.GetCommandLineArgs().</summary>
    public static CoopLaunch Current => _current ??= Parse(SafeArgs());

    /// <summary>Tests: replace the parsed options (null = parse the command line again).</summary>
    internal static void Override(CoopLaunch launch) => _current = launch;

    /// <summary>--skulinsight-instance N (1-4): 1 = the normal game, 2-4 = test copies with their own data and save.</summary>
    public int Instance { get; private set; } = 1;
    /// <summary>--skulinsight-coop host|join.</summary>
    public CoopRole Role { get; private set; }
    /// <summary>--skulinsight-transport udp|steam|loopback.</summary>
    public CoopTransportKind Transport { get; private set; }
    /// <summary>--skulinsight-port P (1-65535).</summary>
    public int Port { get; private set; } = DefaultPort;
    /// <summary>--skulinsight-lag ms (0-2000): added to every message this copy sends (one way; both copies 100 = ~200 ms round trip).</summary>
    public int LagMs { get; private set; }
    /// <summary>--skulinsight-jitter ms (0-1000): up to this much more or less per message.</summary>
    public int JitterMs { get; private set; }
    /// <summary>--skulinsight-loss percent (0-100): the share of sent messages dropped.</summary>
    public float LossPercent { get; private set; }
    /// <summary>--skulinsight-bot: a test bot walks this copy's character around.</summary>
    public bool Bot { get; private set; }
    /// <summary>--skulinsight-bot-skulls=NAME,NAME (the game's skull names, e.g. KingArthur,Berserker_2) or "random":
    /// the bot's player gets these two skulls at the run's start, swaps between them and fills their gauge now and
    /// then (transformations). Null: the plain skull.</summary>
    public string BotSkulls { get; private set; }

    /// <summary>--skulinsight-bot-items=sweep:HALF:FIRST: the item sweep (Testing/BotItems). Null: none.</summary>
    public string BotItems { get; private set; }
    /// <summary>--skulinsight-autorun: unattended run test (CoopAutoRun): the host leaves the castle once everyone is
    /// there and takes a door every so often; this copy's player takes no damage.</summary>
    public bool AutoRun { get; private set; }
    /// <summary>--skulinsight-autorun=N: the chapter the autorun starts at (1 = a normal run).</summary>
    public int AutoRunChapter { get; private set; } = 1;
    /// <summary>--skulinsight-autorun-skip: the host skips stages like the game's dev menu (catch-up test).</summary>
    public bool AutoRunSkip { get; private set; }
    /// <summary>--skulinsight-autorun-dm: the autorun's run is a Dark Mirror run (its chapter N).</summary>
    public bool AutoRunDarkMirror { get; private set; }
    /// <summary>--skulinsight-autorun-dm-level=N: the host copy plays Dark Mirror level N (0-10; its isolated save's
    /// own level otherwise, -1). Dark elites only appear from some level on (COOP_LOG 62: the copies' level 1 had none).</summary>
    public int AutoRunDarkMirrorLevel { get; private set; } = -1;
    /// <summary>--skulinsight-test-fresh-world: a test copy clears its save's world flags (scenes seen, Darktech, NPCs
    /// met / rescued) at start, so it differs from the host's like a young save (HostWorld's test).</summary>
    public bool TestFreshWorld { get; private set; }
    /// <summary>--skulinsight-autorun-boss: the autorun goes straight to the last room of each stage (the stage's
    /// adventurers or the chapter's boss) instead of through every room.</summary>
    public bool AutoRunBoss { get; private set; }
    /// <summary>--skulinsight-autorun-door=SECONDS: how long the autorun's host stays in a room (default 25).</summary>
    public float AutoRunDoorSeconds { get; private set; } = 25f;
    /// <summary>--skulinsight-autorun-rooms=N: the autorun is done after N rooms (0 = until the chapter's boss fell).</summary>
    public int AutoRunRooms { get; private set; }
    /// <summary>--skulinsight-autorun-players=N (2-4): the autorun's host leaves the castle once N players are in the
    /// session (at most 120 s), and the window test waits for N (start-instances.ps1 passes it for 3-4 copies, COOP_LOG 70).</summary>
    public int AutoRunPlayers { get; private set; } = 2;
    /// <summary>--skulinsight-autorun-from=N: the boss route goes from the run's first room straight to room N of the
    /// first stage (a test of one boss without the rooms before it: King Alexander is dm6's room 2).</summary>
    public int AutoRunFrom { get; private set; }
    /// <summary>--skulinsight-autorun-down=SECONDS: a client copy's player is killed that long after a room's
    /// enemies showed up, once per room (the revive test; 0 = never).</summary>
    public float AutoRunDown { get; private set; }
    /// <summary>--skulinsight-bot-attack=PERCENT: an autorun copy's player attacks with at least this much of its
    /// physical and magic attack (100 = unchanged). Umut 2026-11-15: the bots could not break King Alexander's bomb
    /// in time ("give each bot attack values of at least 800%").</summary>
    public float BotAttackPercent { get; private set; } = 100f;
    /// <summary>--skulinsight-shots=SECONDS: paired test pictures every so often (CoopShots; 0 = none).</summary>
    public float ShotSeconds { get; private set; }
    /// <summary>--skulinsight-cam-host: every copy's camera stays on the host's player (CoopShots).</summary>
    public bool CameraOnHost { get; private set; }
    /// <summary>--skulinsight-mute: this copy makes no sound.</summary>
    public bool Mute { get; private set; }
    /// <summary>--skulinsight-window-test: the Co-op window opens in this launcher session, the client asks for more
    /// enemy health and says OK, the host raises it, and every game takes pictures of it (CoopWindowTest).</summary>
    public bool WindowTest { get; private set; }
    /// <summary>--skulinsight-drop=AFTER:FOR (seconds, FOR may be "forever"): this copy's line breaks AFTER seconds
    /// after its first teammate came, for FOR seconds (package DC's drop tests). Null: never.</summary>
    public Net.Outage Drop { get; private set; }
    /// <summary>--skulinsight-name NAME (up to 24 characters), or null.</summary>
    public string Name { get; private set; }
    /// <summary>--skulinsight-matchmaking=URL (http://127.0.0.1:8765): the matchmaking test (package MM). The copy opens no
    /// session at the start: it queues at this server in the castle, its bot accepts, and the confirmed match opens the
    /// session over the launcher's UDP plan (the server picks the host). Null: off.</summary>
    public string MatchServer { get; private set; }
    /// <summary>True when any --skulinsight- option was given (the copy was started by the test launcher).</summary>
    public bool FromLauncher { get; private set; }

    /// <summary>A second to fourth copy: its own data folder and save, no daily runs.</summary>
    public bool IsTestCopy => Instance >= 2;
    /// <summary>A simulated bad connection was asked for (wrap the transport in B's LagSimulator).</summary>
    public bool SimulatesLag => LagMs > 0 || JitterMs > 0 || LossPercent > 0f;

    /// <summary>Reads the options out of <paramref name="args"/> (the program name first or not; it doesn't matter).</summary>
    public static CoopLaunch Parse(IReadOnlyList<string> args)
    {
        var launch = new CoopLaunch();
        if (args == null)
            return launch;
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            if (arg == null || !arg.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
                continue;
            string name = arg.Substring(Prefix.Length);
            string value = null;
            int equals = name.IndexOf('=');
            if (equals >= 0)
            {
                value = name.Substring(equals + 1);
                name = name.Substring(0, equals);
            }
            name = name.ToLowerInvariant();
            launch.FromLauncher = true;

            if (name == "bot")
            {
                launch.Bot = value == null || !IsOff(value);
                continue;
            }
            if (name == "bot-items")
            {
                launch.BotItems = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                continue;
            }
            if (name == "bot-skulls")
            {
                // the value may also come as the next argument
                if (value == null && i + 1 < args.Count && args[i + 1] != null && !args[i + 1].StartsWith("-"))
                    value = args[++i];
                launch.BotSkulls = string.IsNullOrWhiteSpace(value) ? "random" : value.Trim();
                continue;
            }
            if (name == "autorun-dm")
            {
                launch.AutoRunDarkMirror = value == null || !IsOff(value);
                continue;
            }
            if (name == "test-fresh-world")
            {
                launch.TestFreshWorld = value == null || !IsOff(value);
                continue;
            }
            if (name == "autorun-dm-level")
            {
                if (int.TryParse(value, out int dmLevel))
                    launch.AutoRunDarkMirrorLevel = Math.Max(-1, Math.Min(10, dmLevel));
                continue;
            }
            if (name == "autorun-boss")
            {
                launch.AutoRunBoss = value == null || !IsOff(value);
                continue;
            }
            if (name == "cam-host")
            {
                launch.CameraOnHost = value == null || !IsOff(value);
                continue;
            }
            if (name == "mute")
            {
                launch.Mute = value == null || !IsOff(value);
                continue;
            }
            if (name == "window-test")
            {
                launch.WindowTest = value == null || !IsOff(value);
                continue;
            }
            if (name == "autorun-skip")
            {
                launch.AutoRunSkip = value == null || !IsOff(value);
                continue;
            }
            if (name == "autorun")
            {
                launch.AutoRun = value == null || !IsOff(value);
                // --skulinsight-autorun=N: the run starts at chapter N (1-5, Dark Mirror 1-6; boss tests without the
                // chapters before)
                if (value != null && int.TryParse(value, out int chapter) && chapter is >= 1 and <= 6)
                    launch.AutoRunChapter = chapter;
                continue;
            }
            if (value == null)
            {
                // "--skulinsight-port 27960": the next argument is the value, unless it is another option.
                if (i + 1 >= args.Count || args[i + 1] == null || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    continue;
                value = args[++i];
            }
            launch.Apply(name, value.Trim());
        }
        return launch;
    }

    private void Apply(string name, string value)
    {
        switch (name)
        {
            case "instance":
                if (TryInt(value, 1, MaxInstance, out int instance)) Instance = instance;
                break;
            case "coop":
                Role = value.ToLowerInvariant() switch { "host" => CoopRole.Host, "join" => CoopRole.Join, _ => Role };
                break;
            case "transport":
                Transport = value.ToLowerInvariant() switch
                {
                    "udp" => CoopTransportKind.Udp,
                    "steam" => CoopTransportKind.Steam,
                    "loopback" => CoopTransportKind.Loopback,
                    _ => Transport,
                };
                break;
            case "port":
                if (TryInt(value, 1, 65535, out int port)) Port = port;
                break;
            case "lag":
                if (TryInt(value, 0, 2000, out int lag)) LagMs = lag;
                break;
            case "jitter":
                if (TryInt(value, 0, 1000, out int jitter)) JitterMs = jitter;
                break;
            case "loss":
                if (float.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out float loss)
                    && loss >= 0f && loss <= 100f)
                    LossPercent = loss;
                break;
            case "name":
                if (value.Length > 0) Name = value.Length > 24 ? value.Substring(0, 24) : value;
                break;
            case "matchmaking":
                if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    MatchServer = value.TrimEnd('/');
                break;
            case "shots":
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float shots) && shots >= 0f && shots <= 600f)
                    ShotSeconds = shots;
                break;
            case "autorun-door":
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float door) && door >= 3f && door <= 600f)
                    AutoRunDoorSeconds = door;
                break;
            case "autorun-rooms":
                if (TryInt(value, 0, 1000, out int rooms)) AutoRunRooms = rooms;
                break;
            case "autorun-players":
                if (TryInt(value, 2, Net.Protocol.MaxPlayers, out int players)) AutoRunPlayers = players;
                break;
            case "autorun-from":
                if (TryInt(value, 0, 50, out int fromRoom)) AutoRunFrom = fromRoom;
                break;
            case "bot-attack":
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float attack) && attack >= 100f && attack <= 100000f)
                    BotAttackPercent = attack;
                break;
            case "drop":
                if (Net.Outage.TryParse(value, out var drop)) Drop = drop;
                break;
            case "autorun-down":
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float down) && down >= 0f && down <= 600f)
                    AutoRunDown = down;
                break;
        }
    }

    private static bool TryInt(string value, int min, int max, out int result) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result) && result >= min && result <= max;

    private static bool IsOff(string value) => value is "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase)
                                                             || value.Equals("off", StringComparison.OrdinalIgnoreCase);

    private static string[] SafeArgs()
    {
        try { return Environment.GetCommandLineArgs(); }
        catch (Exception) { return new string[0]; }
    }

    /// <summary>One line for the log: "instance 2, join, udp :27960, lag 100 ms +-20, loss 2 %, bot".</summary>
    public override string ToString()
    {
        var parts = new List<string> { "instance " + Instance };
        if (Role != CoopRole.None) parts.Add(Role.ToString().ToLowerInvariant());
        if (Transport != CoopTransportKind.None) parts.Add(Transport.ToString().ToLowerInvariant() + " :" + Port);
        if (SimulatesLag)
            parts.Add(string.Format(CultureInfo.InvariantCulture, "lag {0} ms +-{1}, loss {2} %", LagMs, JitterMs, LossPercent));
        if (Drop != null) parts.Add(Drop.ToString());
        if (Bot) parts.Add("bot");
        if (AutoRun) parts.Add("autorun");
        if (Name != null) parts.Add("name " + Name);
        return string.Join(", ", parts);
    }
}

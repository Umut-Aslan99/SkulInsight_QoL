using DamageInsight.Coop.Net;
using DamageInsight.Coop.Testing;

namespace DamageInsight.Coop;

/// <summary>How this game takes part in co-op at startup: host, join or not at all.</summary>
public enum CoopStartKind
{
    None,
    Host,
    Join,
}

/// <summary>
/// Whether and how co-op starts in this game, decided once from the start options (plain logic, tested). Phase 0:
/// only the test launcher starts co-op (tools/coop/start-instances.ps1, `--skulinsight-coop host|join` over UDP);
/// Steam invites come with phase 1. [DECISION] Launcher options start co-op without `[Coop] Enabled`: they are an
/// explicit test, and the runtime doesn't need the preloader for two players walking around (the preloader keeps its
/// own gate, the config key). A test copy's session waits until its save is redirected (<see cref="NeedsSaveIsolation"/>:
/// the redirect happens when the game's save system starts, after Plugin.Awake) and never opens if that fails.
/// </summary>
public sealed class CoopStartPlan
{
    public CoopStartKind Kind;
    public CoopTransportKind Transport;
    public int Port;
    /// <summary>The lag simulator's settings, or null: no simulated lag.</summary>
    public LagSettings Lag;
    /// <summary>A broken line for the drop tests (package DC), or null.</summary>
    public Outage Drop;
    /// <summary>Why co-op doesn't start (Kind None), for the log; "" when it starts.</summary>
    public string Reason = "";
    /// <summary>A test copy: the session opens only once SaveIsolation.IsSafe (its own save, never the real one).</summary>
    public bool NeedsSaveIsolation;

    public bool Starts => Kind != CoopStartKind.None;

    public static CoopStartPlan From(CoopLaunch launch)
    {
        var plan = new CoopStartPlan { Transport = launch.Transport, Port = launch.Port, NeedsSaveIsolation = launch.IsTestCopy };
        if (launch.Role == CoopRole.None)
            return plan.Off("no co-op start option");
        switch (launch.Transport)
        {
            case CoopTransportKind.Udp:
                break;
            case CoopTransportKind.None:
                // -coop without -transport: the launcher's default.
                plan.Transport = CoopTransportKind.Udp;
                break;
            case CoopTransportKind.Steam:
                return plan.Off("Steam co-op starts from an invite (phase 1), not from start options");
            default:
                return plan.Off("the loopback transport only works inside one game (tests)");
        }
        plan.Kind = launch.Role == CoopRole.Host ? CoopStartKind.Host : CoopStartKind.Join;
        if (launch.SimulatesLag)
            plan.Lag = new LagSettings { DelayMs = launch.LagMs, JitterMs = launch.JitterMs, LossPercent = launch.LossPercent };
        plan.Drop = launch.Drop;
        return plan;
    }

    private CoopStartPlan Off(string reason)
    {
        Kind = CoopStartKind.None;
        Reason = reason;
        return this;
    }

    public override string ToString() => Starts
        ? $"{Kind.ToString().ToLowerInvariant()} over {Transport.ToString().ToLowerInvariant()} :{Port}" +
          (Lag == null ? "" : $", simulated lag {Lag.DelayMs:0} ms +-{Lag.JitterMs:0} ms, {Lag.LossPercent:0.#}% loss") +
          (Drop == null ? "" : ", " + Drop)
        : "off (" + Reason + ")";
}

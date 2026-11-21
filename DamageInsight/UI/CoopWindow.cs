using System.Globalization;
using DamageInsight.Coop;
using DamageInsight.Coop.Net;
using DamageInsight.Lang;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// The Co-op window (docs/coop/NEXT_SPECS.md B): what this game is doing in co-op (a state line, the reason when
/// something failed), Host a game / Invite a friend (Steam's overlay) / Leave, and the players with their colour,
/// role and ping, with Kick for the host. Opened from the pause menu (Co-op, below Mods; setting Coop/MenuButton) or
/// the key Coop/WindowKey. Hosting and joining happen in the castle only [DECISION]. Lives on the plugin object only
/// when the co-op switch is on (CoopRuntime.Begin); the look follows the Mods window.
/// </summary>
public sealed class CoopWindow : MonoBehaviour
{
    private const float Width = 980f, Height = 780f, RowHeight = 64f, MatchHeight = 116f;
    private static readonly int[] PingSteps = { 100, 150, 200, 300, 500 };
    private static readonly string[] ModeSteps = { "normal", "darkmirror", "any" };
    private const double GoodMs = 120, OkMs = 250;

    private static readonly Color Back = new Color32(0x14, 0x11, 0x27, 0xF7);
    private static readonly Color Frame = new Color32(0x5A, 0x50, 0xC8, 0xFF);
    private static readonly Color Panel = new Color32(0x1C, 0x18, 0x38, 0xFF);
    private static readonly Color PanelEdge = new Color32(0x34, 0x2F, 0x66, 0xFF);
    private static readonly Color RowColor = new Color32(0x25, 0x21, 0x4A, 0xFF);
    private static readonly Color Ink = new Color32(0xEC, 0xEA, 0xF7, 0xFF);
    private static readonly Color Dim = new Color32(0x9C, 0x97, 0xC4, 0xFF);
    private static readonly Color Green = new Color32(0x3D, 0xDC, 0x97, 0xFF);
    private static readonly Color Red = new Color32(0xE5, 0x62, 0x6A, 0xFF);
    private static readonly Color Orange = new Color32(0xF2, 0xA9, 0x3B, 0xFF);
    private static readonly Color Blue = new Color32(0x4A, 0x55, 0xC8, 0xFF);
    private static readonly Color Amber = new Color32(0xFF, 0xC8, 0x5A, 0xFF);

    private sealed class PlayerRow
    {
        public GameObject Root;
        public Image Dot;
        public TextMeshProUGUI Name, Role, Ping;
        public UiKit.Chip Kick;
        public int Slot = -1;
    }

    private static CoopWindow _instance;
    public static bool IsOpen => _instance != null && _instance._canvas != null && _instance._canvas.gameObject.activeSelf;

    private Canvas _canvas;
    private RectTransform _window;
    private TextMeshProUGUI _state, _hint, _playersTitle, _empty, _message;
    private Image _stateDot;
    private UiKit.Chip _host, _invite, _leave;
    // Find a partner (matchmaking, package MM).
    private TextMeshProUGUI _matchStatus;
    private UiKit.Chip _matchMode, _matchPing, _matchGo;
    private readonly PlayerRow[] _rows = new PlayerRow[Protocol.MaxPlayers];
    private int _language = -1;
    private float _nextDraw, _messageUntil;
    private bool _fromMenu;

    private void Awake() => _instance = this;

    // ---------------------------------------------------------------- open / close

    /// <summary>From the pause menu's Co-op button.</summary>
    public static void Open()
    {
        if (_instance != null)
            _instance.Show(fromMenu: true);
    }

    /// <summary>The pause menu shows the Co-op button: the co-op switch is on (this window exists) and its setting.</summary>
    internal static bool Available => _instance != null && CoopRuntime.Instance?.Lobby != null && Plugin.CoopMenuButton?.Value != false;

    private void Show(bool fromMenu)
    {
        if (_canvas != null && _language != Loc.Version)
        {
            Destroy(_canvas.gameObject);
            _canvas = null;
        }
        if (_canvas == null)
            Build();
        _fromMenu = fromMenu;
        _canvas.gameObject.SetActive(true);
        _nextDraw = 0f;
        EventSystem.current?.SetSelectedGameObject(null);
        MenuSounds.Play(MenuSounds.Kind.Select);
    }

    private void Close()
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
        MenuSounds.Play(MenuSounds.Kind.Close);
    }

    /// <summary>Esc / Cancel in the pause menu (PauseReturnPatch): closes the window. False when it isn't open.</summary>
    internal static bool HandleBack()
    {
        if (!IsOpen)
            return false;
        _instance.Close();
        return true;
    }

    private void Update()
    {
        Patches.Guard.Run("Co-op window", () =>
        {
            var key = Plugin.CoopWindowKey?.Value ?? default;
            if (key.MainKey != KeyCode.None && key.IsDown() && !SettingsPage.IsOpen && !TextInput.IsTyping() && CoopRuntime.Instance?.Lobby != null)
            {
                if (IsOpen)
                    Close();
                else
                    Show(fromMenu: false);
            }
            else if (IsOpen && !_fromMenu && Input.GetKeyDown(KeyCode.Escape))
                Close(); // opened by its key: no pause menu below takes the Esc
            if (IsOpen && Time.unscaledTime >= _nextDraw)
            {
                _nextDraw = Time.unscaledTime + 0.25f;
                Draw();
            }
        });
    }

    // ---------------------------------------------------------------- building

    private void Build()
    {
        _language = Loc.Version;
        _canvas = UiKit.OverlayCanvas("DamageInsight_CoopWindow", 2150);
        _canvas.gameObject.AddComponent<GraphicRaycaster>();
        ModEventSystem.Ensure();

        var shade = UiKit.Image("Shade", _canvas.transform, new Color(0, 0, 0, 0.72f));
        shade.rectTransform.Fill();
        var window = UiKit.Image("Window", _canvas.transform, Back);
        _window = window.rectTransform;
        _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
        _window.sizeDelta = new Vector2(Width, Height);
        Outline(window, Frame, 2.5f);

        // Header: icon, title, what this is; Close on the right.
        var icon = UiKit.Image("Icon", _window, Color.white, PixelIcons.Get("skull"));
        icon.preserveAspect = true;
        icon.rectTransform.Place(24, 16, 48, 48);
        Text(_window, $"<b>{Loc.T("Co-op")}</b>", 34, Ink).rectTransform.Place(84, 12, 400, 44);
        Text(_window, Loc.T("Play with friends through Steam. Everyone needs the same SkulInsight version and the same mods."), 15, Dim)
            .rectTransform.Place(86, 52, Width - 86 - 100, 22);
        var close = new UiKit.Chip(_window, "X", Width - 24 - 56, 22, 56, Close, Red);
        close.Background.rectTransform.sizeDelta = new Vector2(56, 36);
        close.ClickSound = false;
        close.SetOn(true);

        // The state: a dot and a line, and a dim hint below.
        var statePanel = PanelAt("State", 20, 88, Width - 40, 96);
        _stateDot = UiKit.Image("Dot", statePanel, Dim);
        _stateDot.rectTransform.Place(20, 24, 14, 14);
        _state = Text(statePanel, "", 22, Ink);
        _state.rectTransform.Place(46, 14, Width - 40 - 66, 34);
        NoWrap(_state);
        _hint = Text(statePanel, "", 16, Dim);
        _hint.rectTransform.Place(46, 52, Width - 40 - 66, 40);

        // The players.
        float listTop = 88 + 96 + 16, listHeight = Height - listTop - 96 - MatchHeight - 16;
        var players = PanelAt("Players", 20, listTop, Width - 40, listHeight);
        _playersTitle = Text(players, Loc.T("Players"), 22, Ink);
        _playersTitle.rectTransform.Place(16, 10, 600, 32);
        // A bug report (COOP_LOG 64): the logs in one zip (Steam ids masked) and the GitHub issue page; or the zip alone.
        float rx = Width - 40 - 12;
        foreach (var (label, onClick, color) in new (string, System.Action, Color)[]
                     { (Loc.T("Report a bug or idea"), () => OnReport(true), Orange), (Loc.T("Save logs"), () => OnReport(false), Blue) })
        {
            float w = UiKit.ChipWidth(label) + 26;
            rx -= w;
            var chip = new UiKit.Chip(players, label, rx, 8, w, onClick, color);
            chip.Background.rectTransform.sizeDelta = new Vector2(w, 34);
            rx -= 8;
        }
        _empty = Text(players, "", 18, Dim, TextAlignmentOptions.Center);
        _empty.rectTransform.Place(16, 60, Width - 40 - 32, listHeight - 80);
        for (int i = 0; i < _rows.Length; i++)
            _rows[i] = MakeRow(players, 12, 50 + i * (RowHeight + 6), Width - 40 - 24);

        // Find a partner: the mode, the highest ping, Search / Stop (or Turn on matchmaking), the search's state.
        var match = PanelAt("Match", 20, listTop + listHeight + 16, Width - 40, MatchHeight);
        Text(match, Loc.T("Find a partner"), 22, Ink).rectTransform.Place(16, 8, 400, 32);
        _matchStatus = Text(match, "", 16, Dim);
        _matchStatus.rectTransform.Place(16, 40, Width - 40 - 32, 24);
        NoWrap(_matchStatus);
        float mx = 16, my = MatchHeight - 48;
        _matchMode = PanelChip(match, 250, ref mx, my, OnMatchMode, Blue);
        _matchPing = PanelChip(match, 230, ref mx, my, OnMatchPing, Blue);
        _matchGo = PanelChip(match, 300, ref mx, my, OnMatchGo, Green);

        // Bottom: Host, Invite, Leave on the left; a message; Close on the right.
        float y = Height - 76 + 8, x = 20;
        _host = BottomChip(Loc.T("Host a game"), ref x, y, OnHost, Green);
        _invite = BottomChip(Loc.T("Invite a friend"), ref x, y, OnInvite, Blue);
        _leave = BottomChip(Loc.T("Leave"), ref x, y, OnLeave, Red);
        string closeLabel = Loc.T("Close");
        float closeWidth = UiKit.ChipWidth(closeLabel) + 30;
        var bottomClose = new UiKit.Chip(_window, closeLabel, Width - 20 - closeWidth, y, closeWidth, Close);
        bottomClose.Background.rectTransform.sizeDelta = new Vector2(closeWidth, 40);
        bottomClose.ClickSound = false;
        _message = Text(_window, "", 16, Orange);
        _message.rectTransform.Place(x + 8, y + 8, Width - 20 - closeWidth - 16 - (x + 8), 28);
        NoWrap(_message);
        _canvas.gameObject.SetActive(false);
    }

    private UiKit.Chip BottomChip(string label, ref float x, float y, System.Action onClick, Color color)
    {
        float w = UiKit.ChipWidth(label) + 30;
        var chip = new UiKit.Chip(_window, label, x, y, w, onClick, color);
        chip.Background.rectTransform.sizeDelta = new Vector2(w, 40);
        x += w + 8;
        return chip;
    }

    private static UiKit.Chip PanelChip(RectTransform parent, float w, ref float x, float y, System.Action onClick, Color color)
    {
        var chip = new UiKit.Chip(parent, "", x, y, w, onClick, color);
        chip.Background.rectTransform.sizeDelta = new Vector2(w, 38);
        x += w + 8;
        return chip;
    }

    private RectTransform PanelAt(string name, float x, float y, float w, float h)
    {
        var panel = UiKit.Image(name, _window, Panel);
        panel.rectTransform.Place(x, y, w, h);
        Outline(panel, PanelEdge, 1.5f);
        return panel.rectTransform;
    }

    private PlayerRow MakeRow(RectTransform parent, float x, float y, float w)
    {
        var row = new PlayerRow();
        var back = UiKit.Image("Player", parent, RowColor);
        back.rectTransform.Place(x, y, w, RowHeight);
        row.Root = back.gameObject;
        row.Dot = UiKit.Image("Dot", back.transform, Ink);
        row.Dot.rectTransform.Place(18, RowHeight / 2 - 8, 16, 16);
        row.Name = Text(back.transform, "", 22, Ink);
        row.Name.richText = false; // a player's name is shown as typed
        // Heights with room for a descender: at 22 px the role's line ("host · you") grew taller than its box at the
        // "y", and the ellipsis cut it to "host..." (Umut's solo Steam check, COOP_LOG 59h).
        row.Name.rectTransform.Place(46, 5, 420, 34);
        NoWrap(row.Name);
        row.Role = Text(back.transform, "", 15, Dim);
        row.Role.rectTransform.Place(46, 35, 420, 28);
        NoWrap(row.Role);
        row.Ping = Text(back.transform, "", 20, Ink, TextAlignmentOptions.Right);
        row.Ping.rectTransform.Place(w - 140 - 24 - 220, 18, 220, 30);
        NoWrap(row.Ping);
        string kick = Loc.T("Kick");
        float kw = UiKit.ChipWidth(kick) + 24;
        row.Kick = new UiKit.Chip(back.transform, kick, w - 16 - kw, (RowHeight - 36) / 2, kw, () => OnKick(row), Red);
        row.Kick.Background.rectTransform.sizeDelta = new Vector2(kw, 36);
        row.Root.SetActive(false);
        return row;
    }

    // ---------------------------------------------------------------- buttons

    private void OnHost()
    {
        var runtime = CoopRuntime.Instance;
        var lobby = runtime?.Lobby;
        if (lobby == null)
            return;
        if (!lobby.CanHost)
            Say(Loc.T("Leave this game first."));
        else if (!lobby.SteamReady)
            Say(NoSteamText());
        else if (!CoopRuntime.InCastle)
            Say(Loc.T("Hosting and joining work in the castle only."));
        else
            runtime.HostSteam();
        _nextDraw = 0f;
    }

    private void OnInvite()
    {
        var lobby = CoopRuntime.Instance?.Lobby;
        if (lobby == null)
            return;
        if (!lobby.CanInvite)
            Say(Loc.T("Host a game first, then invite your friends."));
        else if (!lobby.Invite())
            Say(Loc.T("The Steam overlay did not open: switch it on in Steam (Settings, In game)."));
    }

    private void OnLeave()
    {
        var runtime = CoopRuntime.Instance;
        if (runtime != null && runtime.InMatchSession)
        {
            runtime.LeaveMatch(); // a matchmaking session has no lobby
            _nextDraw = 0f;
            return;
        }
        var lobby = runtime?.Lobby;
        if (lobby == null || !lobby.CanLeave)
            return;
        lobby.Leave();
        _nextDraw = 0f;
    }

    // ---------------------------------------------------------------- find a partner

    private void OnMatchMode()
    {
        if (Plugin.CoopMatchMode == null || CoopRuntime.Instance?.Matchmaking?.Client.Searching == true)
            return;
        int i = System.Array.IndexOf(ModeSteps, Plugin.CoopMatchMode.Value);
        Plugin.CoopMatchMode.Value = ModeSteps[(i + 1) % ModeSteps.Length];
        _nextDraw = 0f;
    }

    private void OnMatchPing()
    {
        if (Plugin.CoopMaxPing == null || CoopRuntime.Instance?.Matchmaking?.Client.Searching == true)
            return;
        int now = Plugin.CoopMaxPing.Value, next = PingSteps[0];
        foreach (int step in PingSteps)
            if (step > now)
            {
                next = step;
                break;
            }
        Plugin.CoopMaxPing.Value = next;
        _nextDraw = 0f;
    }

    private void OnMatchGo()
    {
        var matchmaking = CoopRuntime.Instance?.Matchmaking;
        if (matchmaking == null)
            return;
        if (!matchmaking.Enabled)
        {
            // Off until the player turns it on (the line above says what is sent).
            if (Plugin.CoopMatchmaking != null)
                Plugin.CoopMatchmaking.Value = true;
        }
        else if (matchmaking.Client.Searching)
            matchmaking.Cancel();
        else if (matchmaking.Start() is { } why)
            Say(why);
        _nextDraw = 0f;
    }

    private void DrawMatch(CoopRuntime runtime)
    {
        var matchmaking = runtime.Matchmaking;
        if (matchmaking == null)
            return;
        var client = matchmaking.Client;
        string mode = Plugin.CoopMatchMode?.Value ?? "any";
        _matchMode.Label.text = Loc.F("Mode: {0}", MatchFoundBox.ModeText(mode, -1));
        _matchPing.Label.text = Loc.F("Max ping: {0} ms", Plugin.CoopMaxPing?.Value ?? 200);
        _matchMode.SetOn(!client.Searching);
        _matchPing.SetOn(!client.Searching);
        _matchGo.Label.text = !matchmaking.Enabled ? Loc.T("Turn on matchmaking") : client.Searching ? Loc.T("Stop searching") : Loc.T("Search");
        bool busy = runtime.Session is { State: not SessionState.Ended };
        _matchGo.SetOn(!matchmaking.Enabled || client.Searching || (client.CanStart && !busy && CoopRuntime.InCastle));
        MatchStatusText(matchmaking, runtime, out string text, out Color color);
        _matchStatus.text = text;
        _matchStatus.color = color;
    }

    /// <summary>The search's state in plain words.</summary>
    private static void MatchStatusText(Coop.Match.CoopMatchmaking matchmaking, CoopRuntime runtime, out string text, out Color color)
    {
        var client = matchmaking.Client;
        color = Dim;
        if (!matchmaking.Enabled)
        {
            text = Loc.T("Off. When on, it sends your player id, Steam id, name, versions, mods, mode and Steam ping location to the daily run server while you search.");
            return;
        }
        var last = client.Last;
        switch (client.State)
        {
            case Coop.Match.MatchState.Queueing:
            case Coop.Match.MatchState.Queued:
                int s = client.SearchSeconds;
                text = Loc.F("Searching for {0}. Players looking: {1}", (s / 60) + ":" + (s % 60).ToString("00", CultureInfo.InvariantCulture), last?.Looking ?? 0);
                if (last != null && last.Notice == "partner_declined")
                    text = Loc.T("Your partner declined. Searching again.") + "  " + text;
                else if (last != null && last.Notice == "partner_timeout")
                    text = Loc.T("Your partner did not answer. Searching again.") + "  " + text;
                else if (last != null && client.Ticket != null && last.MaxPing > client.Ticket.MaxPing)
                    text += "  " + Loc.F("Searching wider: up to {0} ms.", last.MaxPing);
                color = Amber;
                return;
            case Coop.Match.MatchState.Proposed:
            case Coop.Match.MatchState.Accepted:
                text = Loc.T("Match found");
                color = Green;
                return;
            case Coop.Match.MatchState.Confirmed:
                text = Loc.F("Match found! Connecting to {0}...", matchmaking.PartnerName);
                color = Amber;
                return;
            case Coop.Match.MatchState.Cooldown:
                text = Loc.F("Too many declined matches: search again in {0} min.", (client.CooldownSecondsLeft + 59) / 60);
                color = Red;
                return;
            case Coop.Match.MatchState.ServerOff:
                text = Loc.T("The matchmaking server is off right now. Invite a friend instead.");
                color = Red;
                return;
            case Coop.Match.MatchState.Failed:
                color = Red;
                text = client.Problem switch
                {
                    Coop.Match.MatchProblem.Offline => Loc.T("No connection to the matchmaking server."),
                    Coop.Match.MatchProblem.VersionRefused => Loc.T("The server did not take your versions: update SkulInsight."),
                    Coop.Match.MatchProblem.Timeout => Loc.T("You did not answer in time."),
                    Coop.Match.MatchProblem.Replaced => Loc.T("You searched in another game."),
                    Coop.Match.MatchProblem.Lost or Coop.Match.MatchProblem.Expired => Loc.T("The search ended. Search again."),
                    _ => Loc.T("The matchmaking server has a problem. Try again in a moment."),
                };
                return;
            case Coop.Match.MatchState.Cancelled:
                text = client.Problem == Coop.Match.MatchProblem.Declined ? Loc.T("You declined the match.") : "";
                return;
            case Coop.Match.MatchState.Done:
                if (runtime.InMatchSession)
                {
                    text = Loc.F("In a co-op game with {0}.", matchmaking.PartnerName);
                    color = Green;
                }
                else if (matchmaking.ConnectProblem.Length > 0)
                {
                    text = Loc.T("The match did not connect. Search again.");
                    color = Red;
                }
                else
                    text = "";
                return;
            default:
                text = "";
                return;
        }
    }

    private void OnKick(PlayerRow row)
    {
        var session = CoopRuntime.Instance?.Session;
        if (session == null || !session.IsHost || row.Slot <= 0)
            return;
        string name = session.PlayerAt(row.Slot)?.Name ?? "";
        // [DECISION] Kick without a ban: a friend kicked by mistake can be invited again.
        if (session.Kick(row.Slot))
            Say(Loc.F("{0} was removed from the game.", name));
        _nextDraw = 0f;
    }

    private void OnReport(bool issue)
    {
        string zip = Crash.BugReport.MakeAndOpen(issue, out string problem);
        if (zip == null)
            Say(Loc.F("The logs could not be packed: {0}", problem));
        else if (issue)
            Say(Loc.F("Saved {0} (Steam ids hidden): drag it into the GitHub page.", System.IO.Path.GetFileName(zip)));
        else
            Say(Loc.F("Saved {0} (Steam ids hidden) in the folder that opened.", System.IO.Path.GetFileName(zip)));
    }

    private void Say(string text)
    {
        _message.text = text;
        _messageUntil = Time.unscaledTime + 8f;
    }

    // ---------------------------------------------------------------- drawing

    private void Draw()
    {
        var runtime = CoopRuntime.Instance;
        var lobby = runtime?.Lobby;
        if (lobby == null)
            return;
        var session = runtime.Session;
        bool live = session != null && session.State != SessionState.Ended;
        bool castle = CoopRuntime.InCastle;

        StateText(lobby, session, castle, out string state, out string hint, out Color color);
        if (lobby.State is LobbyState.Idle or LobbyState.Failed)
            MatchSessionText(runtime, session, ref state, ref hint, ref color);
        _state.text = state;
        _state.color = color == Red ? Red : Ink;
        _stateDot.color = color;
        _hint.text = hint;

        bool matchSession = runtime.InMatchSession;
        bool searching = runtime.Matchmaking?.Client.Searching == true;
        _host.SetOn(lobby.CanHost && lobby.SteamReady && castle && !matchSession && !searching);
        _invite.SetOn(lobby.CanInvite);
        _leave.SetOn(lobby.CanLeave || matchSession);
        DrawMatch(runtime);
        if (_message.text.Length > 0 && Time.unscaledTime > _messageUntil)
            _message.text = "";

        int shown = 0;
        if (live)
            foreach (var player in session.Roster)
            {
                if (shown >= _rows.Length)
                    break;
                FillRow(_rows[shown++], session, player);
            }
        for (int i = shown; i < _rows.Length; i++)
        {
            _rows[i].Slot = -1;
            _rows[i].Root.SetActive(false);
        }
        int max = session?.Options.MaxPlayers ?? lobby.MaxPlayers;
        _playersTitle.text = live ? $"{Loc.T("Players")}  <color=#9C97C4>{shown} / {max}</color>" : Loc.T("Players");
        _empty.text = shown > 0 ? "" : Loc.T("Nobody here yet. Host a game and invite a friend, or accept a friend's Steam invite.");
    }

    private void FillRow(PlayerRow row, CoopSession session, RosterEntry player)
    {
        row.Root.SetActive(true);
        row.Slot = player.Slot;
        var color = TeammateTags.ColorOf(player.Slot);
        row.Dot.color = color;
        row.Name.color = color;
        if (row.Name.text != player.Name)
            row.Name.text = player.Name;
        bool me = player.Slot == session.LocalSlot;
        string role = player.IsHost ? Loc.T("host") : Loc.T("player");
        row.Role.text = me ? role + " · " + Loc.T("you") : role;

        // Ping: a client talks to the host only, the host to everyone.
        if (me)
        {
            row.Ping.text = "";
        }
        else if (!session.IsHost && !player.IsHost)
        {
            row.Ping.text = "-";
            row.Ping.color = Dim;
        }
        else
        {
            double ping = session.PingMs(player.Slot), silence = session.SilenceSeconds(player.Slot);
            if (silence >= 1.5)
            {
                row.Ping.text = Loc.F("no signal for {0} s", Mathf.FloorToInt((float)silence));
                row.Ping.color = Red;
            }
            else if (ping < 0)
            {
                row.Ping.text = "...";
                row.Ping.color = Amber;
            }
            else
            {
                row.Ping.text = ping.ToString("0", CultureInfo.InvariantCulture) + " ms";
                row.Ping.color = ping < GoodMs ? Green : ping < OkMs ? Amber : Red;
            }
        }
        bool kick = session.IsHost && !me && session.State == SessionState.Active;
        row.Kick.Background.gameObject.SetActive(kick);
        if (kick)
            row.Kick.SetOn(true);
    }

    /// <summary>The state line, the hint under it and the dot's colour.</summary>
    private static void StateText(SteamLobby lobby, CoopSession session, bool castle, out string state, out string hint, out Color color)
    {
        hint = "";
        color = Dim;
        switch (lobby.State)
        {
            case LobbyState.Creating:
                state = Loc.T("Making a Steam lobby...");
                color = Amber;
                break;
            case LobbyState.Hosting:
                state = Loc.T("You host a co-op game.");
                hint = !castle ? Loc.T("New players can join when you are back in the castle.")
                    : lobby.FriendsCanJoin ? Loc.T("Invite friends, or they join from their Steam friends list (Join game).")
                    : Loc.T("Only friends you invite can join.");
                color = Green;
                break;
            case LobbyState.WaitingForCastle:
                state = Loc.T("Joining a friend's game...");
                hint = !lobby.SteamReady ? Loc.T("Waiting for Steam.") : Loc.T("You join once you are in the castle.");
                color = Amber;
                break;
            case LobbyState.Joining:
                state = Loc.T("Joining a friend's game...");
                color = Amber;
                break;
            case LobbyState.InLobby:
                string name = session?.PlayerAt(0)?.Name;
                if (string.IsNullOrEmpty(name))
                    name = lobby.HostName;
                if (session is { State: SessionState.Active })
                {
                    state = Loc.F("In {0}'s game.", name);
                    color = Green;
                }
                else
                {
                    state = string.IsNullOrEmpty(name) ? Loc.T("Connecting to the host...") : Loc.F("Connecting to {0}...", name);
                    hint = Loc.T("Steam finds a route between the two games: this takes a few seconds.");
                    color = Amber;
                }
                break;
            case LobbyState.Failed:
                state = ProblemText(lobby);
                hint = !lobby.SteamReady ? "" : castle ? Loc.T("Host a game, or accept a friend's Steam invite.")
                    : Loc.T("Hosting and joining work in the castle.");
                color = Red;
                break;
            default:
                if (!lobby.SteamReady)
                {
                    state = NoSteamText();
                    color = Red;
                }
                else
                {
                    state = Loc.T("Not in a co-op game.");
                    hint = castle ? Loc.T("Host a game, or accept a friend's Steam invite.") : Loc.T("Hosting and joining work in the castle.");
                }
                break;
        }
    }

    /// <summary>A matchmaking session has no Steam lobby: its state line (connecting, playing, why it ended).</summary>
    private static void MatchSessionText(CoopRuntime runtime, CoopSession session, ref string state, ref string hint, ref Color color)
    {
        if (runtime.InMatchSession)
        {
            string partner = runtime.Matchmaking?.PartnerName ?? "";
            if (session is { State: SessionState.Active })
            {
                state = Loc.F("In a co-op game with {0}.", partner);
                hint = "";
                color = Green;
            }
            else
            {
                state = Loc.F("Match found! Connecting to {0}...", partner);
                hint = Loc.T("Steam finds a route between the two games: this takes a few seconds.");
                color = Amber;
            }
        }
        else if (runtime.LastMatchEnd is { } end && runtime.Lobby?.State == LobbyState.Idle)
        {
            state = LeaveText(end.why, end.rejection, end.hostVersion);
            color = end.why == LeaveReason.LocalLeave ? Dim : Red;
            if (end.why == LeaveReason.LocalLeave)
                state = Loc.T("Not in a co-op game.");
        }
    }

    private static string NoSteamText() => Loc.T("Steam is not running: co-op needs Skul started from Steam.");

    /// <summary>Why the last try failed, for the player.</summary>
    internal static string ProblemText(SteamLobby lobby)
    {
        string detail = lobby.Detail;
        switch (lobby.Problem)
        {
            case LobbyProblem.NoSteam: return NoSteamText();
            case LobbyProblem.NotInCastle: return Loc.T("Hosting and joining work in the castle only.");
            case LobbyProblem.CreateFailed: return Loc.F("Steam could not make a lobby ({0}). Try again in a moment.", detail);
            case LobbyProblem.LobbyGone: return Loc.T("That game is not open any more.");
            case LobbyProblem.LobbyFull: return Loc.T("That game is full.");
            case LobbyProblem.JoinFailed: return Loc.F("Steam could not join that game ({0}).", detail);
            case LobbyProblem.VersionMismatch: return VersionText(detail);
            case LobbyProblem.StartFailed: return Loc.F("Co-op could not start: {0}", detail);
            case LobbyProblem.SessionEnded: return LeaveText(lobby.EndReason, lobby.Rejection, detail);
            default: return Loc.T("Something went wrong.");
        }
    }

    private static string VersionText(string theirs) => theirs.Length > 0
        ? Loc.F("The host plays SkulInsight {0}, you have {1}: both need the same version.", theirs, MyPluginInfo.PLUGIN_VERSION)
        : Loc.T("The host plays another SkulInsight version: both need the same version.");

    private static string LeaveText(LeaveReason why, RejectReason rejection, string detail) => why switch
    {
        LeaveReason.Timeout => Loc.T("The connection timed out: nothing came from the other game for 10 seconds."),
        LeaveReason.ConnectionLost => Loc.T("The connection was lost (or Steam found no route to the other game)."),
        LeaveReason.ProtocolError => Loc.T("The games could not understand each other (a protocol error)."),
        LeaveReason.Kicked => Loc.T("The host removed you from the game."),
        LeaveReason.HostLeft => Loc.T("The host ended the game."),
        LeaveReason.Rejected => RejectText(rejection, detail),
        _ => Loc.T("The co-op game ended."),
    };

    /// <summary>The texts of the session's reject reasons (RejectReason).</summary>
    internal static string RejectText(RejectReason rejection, string hostVersion) => rejection switch
    {
        RejectReason.VersionMismatch => VersionText(hostVersion ?? ""),
        RejectReason.ModListMismatch => Loc.T("Your mods are not the same as the host's: both need the same mods in the same versions (see Mods)."),
        RejectReason.Full => Loc.T("That game is full."),
        RejectReason.Kicked => Loc.T("The host removed you from the game."),
        RejectReason.HostLeaving => Loc.T("The host ended the game."),
        RejectReason.NotAccepting => Loc.T("The host is in a run: join when they are back in the castle."),
        RejectReason.DuplicatePlayer => Loc.T("You are in that game already."),
        _ => Loc.T("The host did not let you in."),
    };

    // ---------------------------------------------------------------- helpers

    private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft) => UiKit.Text("Text", parent, text, size, alignment, color);

    private static void NoWrap(TextMeshProUGUI label)
    {
        label.enableWordWrapping = false;
        label.overflowMode = TextOverflowModes.Ellipsis;
    }

    private static void Outline(Image image, Color color, float distance)
    {
        var outline = image.gameObject.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    private void OnDestroy()
    {
        if (_canvas != null)
            Destroy(_canvas.gameObject);
        if (_instance == this)
            _instance = null;
    }
}

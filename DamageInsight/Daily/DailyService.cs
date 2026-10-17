using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using DamageInsight.Recording;
using UnityEngine;
using UnityEngine.Networking;

namespace DamageInsight.Daily;

/// <summary>
/// The daily run server's client (0.13; server/daily): the day's run, the boards, a try's start and its result. Talks
/// only while the setting Daily/Enabled is on (the board asks first and says what is sent: the random player id, the
/// name, the run's numbers). A result that can't be sent (no connection) is kept and sent again later.
/// </summary>
public sealed class DailyService : MonoBehaviour
{
    public static string Folder => Path.Combine(BepInEx.Paths.BepInExRootPath, "DamageInsight", "Daily");
    private static string StatePath => Path.Combine(Folder, "state.json");

    public static DailyService Instance { get; private set; }
    public static DailyState State { get; private set; } = new();
    public static DailyInfo Today { get; private set; }
    public static Board DayBoard { get; private set; }
    public static Board MonthBoard { get; private set; }

    /// <summary>"" when fine, else what went wrong last ("offline", a server message).</summary>
    public static string Problem { get; private set; } = "";

    /// <summary>Increases whenever something above changed (the book redraws).</summary>
    public static int Version { get; private set; }

    public static bool Enabled => Plugin.DailyEnabled?.Value == true;
    private static string Server => (Plugin.DailyServer?.Value ?? "").TrimEnd('/');

    private float _nextInfo, _nextBoards, _nextPending;
    private bool _busyInfo, _busyBoards, _busyPending;

    private void Awake()
    {
        Instance = this;
        State = DailyState.Load(StatePath);
        State.EnsurePlayer();
        SaveState();
    }

    public static void SaveState()
    {
        try { State.Save(StatePath); }
        catch (Exception e) { Plugin.Log.LogWarning($"Daily run: state not saved: {e.Message}"); }
    }

    private static void Changed() => Version++;

    private void Update()
    {
        if (!Enabled)
            return;
        float now = Time.unscaledTime;
        // The day's run: on start, every 10 minutes, and right after the UTC day changed.
        bool newDay = Today != null && Today.Date != DateTime.UtcNow.ToString("yyyy-MM-dd");
        if (!_busyInfo && (Today == null || newDay || now >= _nextInfo))
        {
            _nextInfo = now + (Today == null ? 30f : 600f);
            StartCoroutine(LoadToday());
        }
        if (!_busyPending && now >= _nextPending)
        {
            _nextPending = now + 60f;
            StartCoroutine(SendPending());
        }
    }

    /// <summary>The boards again (the book asks while the Daily section is open; at most once a minute).</summary>
    public static void RequestBoards(bool force = false)
    {
        if (!Enabled || Instance == null || Instance._busyBoards || (!force && Time.unscaledTime < Instance._nextBoards))
            return;
        Instance._nextBoards = Time.unscaledTime + 60f;
        Instance.StartCoroutine(Instance.LoadBoards());
    }

    /// <summary>Asks for the day's run now (after the player turned the setting on).</summary>
    public static void RefreshNow()
    {
        if (Instance == null || !Enabled)
            return;
        Instance._nextInfo = 0f;
        RequestBoards(force: true);
    }

    private IEnumerator LoadToday()
    {
        _busyInfo = true;
        yield return Get("/api/daily", json =>
        {
            var info = DailyInfo.FromJson(json);
            if (info != null)
            {
                Today = info;
                State.Trim(info.Date);
                Problem = "";
                Changed();
            }
        });
        yield return LoadMe();
        _busyInfo = false;
    }

    /// <summary>Today's try as the server knows it: this PC follows it (a run the admin removed may be played again).</summary>
    private IEnumerator LoadMe()
    {
        yield return Get("/api/me?player=" + Uri.EscapeDataString(State.PlayerId), json =>
        {
            var today = DailyInfo.Parse(json).Child("today");
            string date = today.Str("date") ?? "", state = today.Str("state") ?? "";
            if (date.Length > 0 && State.FollowServer(date, state))
            {
                Plugin.Log.LogInfo($"Daily run: the server has no run of yours for {date} (removed): it can be played again");
                SaveState();
                Changed();
            }
        });
    }

    private IEnumerator LoadBoards()
    {
        _busyBoards = true;
        string player = Uri.EscapeDataString(State.PlayerId);
        yield return Get("/api/board?limit=100&player=" + player, json =>
        {
            DayBoard = Board.FromJson(json) ?? DayBoard;
            Changed();
        });
        yield return Get("/api/board/month?limit=100&player=" + player, json =>
        {
            MonthBoard = Board.FromJson(json) ?? MonthBoard;
            Changed();
        });
        yield return LoadMe();
        _busyBoards = false;
    }

    /// <summary>The day's try begins (the daily run left the castle): the server counts it.</summary>
    public static void SendStart(string date)
    {
        if (Instance == null || !Enabled)
            return;
        string body = "{\"date\":" + LogJson.Quote(date) + ",\"player\":" + LogJson.Quote(State.PlayerId) +
                      ",\"mod\":" + LogJson.Quote(MyPluginInfo.PLUGIN_VERSION) +
                      ",\"mods\":[" + string.Join(",", DailyMods.Others().Select(m => LogJson.Quote(m.guid))) + "]}";
        Instance.StartCoroutine(Instance.Post("/api/runs/start", body, _ => Changed(), (code, text) =>
            Plugin.Log.LogWarning($"Daily run: start not counted ({code}): {text}")));
    }

    /// <summary>A finished daily run's result (its numbers wait in the try): kept until the server took it.</summary>
    public static void QueueFinish(DailyTry t, string name)
    {
        string body = "{\"date\":" + LogJson.Quote(t.Date) + ",\"player\":" + LogJson.Quote(State.PlayerId) +
                      ",\"name\":" + LogJson.Quote(name) + ",\"seed\":" + t.Seed + ",\"mode\":" + LogJson.Quote(t.Mode) +
                      ",\"level\":" + t.Level + ",\"numbers\":" + (t.Numbers.Length > 0 ? t.Numbers : "{}") +
                      (t.Run.Length > 0 ? ",\"run\":" + t.Run : "") +
                      ",\"mod\":" + LogJson.Quote(MyPluginInfo.PLUGIN_VERSION) + "}";
        t.Pending = body;
        t.State = "finished";
        SaveState();
        if (Instance != null)
        {
            Instance._nextPending = 0f;
            Changed();
        }
    }

    private IEnumerator SendPending()
    {
        _busyPending = true;
        foreach (var t in State.Tries.Values)
        {
            if (string.IsNullOrEmpty(t.Pending))
                continue;
            var current = t;
            // The start again first (it may not have reached the server: no connection when the run began). The server
            // takes a start once per player and day; a second one changes nothing.
            string start = "{\"date\":" + LogJson.Quote(current.Date) + ",\"player\":" + LogJson.Quote(State.PlayerId) +
                           ",\"mod\":" + LogJson.Quote(MyPluginInfo.PLUGIN_VERSION) + "}";
            yield return Post("/api/runs/start", start, _ => { }, (_, _) => { });
            yield return Post("/api/runs/finish", current.Pending, json =>
            {
                if (FinishAnswer.FromJson(json) is { } answer)
                {
                    current.Points = answer.Points;
                    current.Rank = answer.Modded ? -3 : answer.Rank;
                    current.Of = answer.Of;
                    current.Pending = "";
                    current.Run = "";
                    if (answer.Name.Length > 0)
                        State.Name = answer.Name;
                    SaveState();
                    RequestBoards(force: true);
                    Changed();
                }
            }, (code, text) =>
            {
                // The server refused it for good (already sent, too late, not the day's run): stop trying.
                if (code is >= 400 and < 500 and not 429)
                {
                    Plugin.Log.LogWarning($"Daily run: result refused ({code}): {text}");
                    current.Pending = "";
                    current.Rank = -1;
                    SaveState();
                    Changed();
                }
            });
        }
        _busyPending = false;
    }

    // ------------------------------------------------------------------ other players' runs, likes

    private static readonly Dictionary<string, RunDetail> Details = new();
    private static readonly Dictionary<string, float> Asked = new();

    /// <summary>A board line's run, once loaded (asked for when first wanted; again after 30 s if no answer came).</summary>
    public static RunDetail Detail(string date, string id)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(date))
            return null;
        if (Details.TryGetValue(id, out var detail))
            return detail;
        if (Instance == null || !Enabled || (Asked.TryGetValue(id, out var at) && Time.unscaledTime < at + 30f))
            return null;
        Asked[id] = Time.unscaledTime;
        Instance.StartCoroutine(Instance.Get($"/api/runs/detail?date={Uri.EscapeDataString(date)}&id={Uri.EscapeDataString(id)}" +
                                             $"&player={Uri.EscapeDataString(State.PlayerId)}", json =>
            {
                if (RunDetail.FromJson(json) is { } d)
                {
                    Details[id] = d;
                    Changed();
                }
            }));
        return null;
    }

    /// <summary>A thumbs up for a board line's run (once; not one's own).</summary>
    public static void Like(string date, BoardEntry entry)
    {
        if (Instance == null || !Enabled || entry == null || entry.You || entry.Liked)
            return;
        entry.Liked = true;
        entry.Likes++;
        Changed();
        string body = "{\"date\":" + LogJson.Quote(date) + ",\"id\":" + LogJson.Quote(entry.Id) + ",\"player\":" + LogJson.Quote(State.PlayerId) + "}";
        Instance.StartCoroutine(Instance.Post("/api/runs/like", body, json =>
        {
            int likes = (int)DailyInfo.Parse(json).Num("likes", entry.Likes);
            foreach (var e in new[] { entry }.Concat(DayBoard?.Entries.Where(x => x.Id == entry.Id) ?? Enumerable.Empty<BoardEntry>())
                         .Concat(Details.Values.Select(d => d.Entry).Where(x => x.Id == entry.Id)))
            {
                e.Likes = likes;
                e.Liked = true;
            }
            Changed();
        }, (_, _) => { }));
    }

    // ------------------------------------------------------------------ HTTP

    private IEnumerator Get(string path, Action<string> done)
    {
        using var request = UnityWebRequest.Get(Server + path);
        yield return Send(request, done, null);
    }

    private IEnumerator Post(string path, string body, Action<string> done, Action<long, string> refused)
    {
        using var request = new UnityWebRequest(Server + path, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body)),
            downloadHandler = new DownloadHandlerBuffer(),
        };
        request.SetRequestHeader("Content-Type", "application/json");
        yield return Send(request, done, refused);
    }

    private static IEnumerator Send(UnityWebRequest request, Action<string> done, Action<long, string> refused)
    {
        request.timeout = 10;
        request.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        yield return request.SendWebRequest();
        string text = request.downloadHandler?.text ?? "";
        if (request.result == UnityWebRequest.Result.Success)
        {
            DamageInsight.Patches.Guard.Run("Daily run (answer)", () => done(text));
            yield break;
        }
        if (request.result == UnityWebRequest.Result.ProtocolError)
        {
            Problem = "server " + request.responseCode;
            refused?.Invoke(request.responseCode, text);
        }
        else
        {
            Problem = "offline";
        }
        Changed();
    }
}

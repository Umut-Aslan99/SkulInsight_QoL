using System;
using System.Globalization;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// A private co-op test build for one person (Umut 2026-11-16: a quick tester from the Skul Discord should not pass
/// the zip on or present it as their own). `tools/coop/friend-package.ps1 -For NAME -Hours 24` builds the mod with two
/// assembly attributes (`SkulInsight.TestBuildFor`, `SkulInsight.TestBuildUntil`, an UTC midnight): co-op switches
/// itself off after that moment (the PC clock, or the daily server's date, whichever says so first), and a line on the
/// screen names the author, the tester and the end. A normal build has neither attribute: nothing changes.
/// [DECISION] The line is English only and not a setting: it exists only in these private builds, never in a release.
/// A determined person can patch a .NET mod; this stops casual sharing and makes a leak traceable to its tester.
/// </summary>
internal static class TestBuild
{
    public static readonly string For = Read("SkulInsight.TestBuildFor");
    public static readonly DateTime? Until = ParseUntil(Read("SkulInsight.TestBuildUntil"));

    public static bool IsTestBuild => For.Length > 0 && Until.HasValue;

    /// <summary>The official server (fixed, not the Daily/Server setting, which a player can point anywhere; HTTPS with
    /// the certificate checked). Its /api/dailies answer carries today's date (UTC).</summary>
    public const string DateUrl = "https://skul.umut-aslan.dev/api/dailies";

    /// <summary>Today's date as the server said it (null until it answered).</summary>
    public static DateTime? ServerDate { get; internal set; }

    /// <summary>Past its end by the PC clock, or the server already reports a day at or after the end.</summary>
    public static bool Expired =>
        IsTestBuild && (DateTime.UtcNow >= Until.Value || (ServerDate.HasValue && ServerDate.Value >= Until.Value));

    /// <summary>A test build starts co-op only once the server confirmed the date (Umut 2026-11-16: an old PC clock
    /// without internet must not bring an ended build back).</summary>
    public static bool MayStart => !IsTestBuild || (ServerDate.HasValue && !Expired);

    internal static bool TryParseServerDate(string json, out DateTime date)
    {
        date = default;
        var match = System.Text.RegularExpressions.Regex.Match(json ?? "", "\"date\"\\s*:\\s*\"(\\d{4}-\\d{2}-\\d{2})\"");
        return match.Success && DateTime.TryParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out date);
    }

    public static string Line =>
        $"SkulInsight co-op TEST BUILD by DocRun  ·  for {For}  ·  until {Until.Value.ToLocalTime():yyyy-MM-dd HH:mm}  ·  please don't share";

    private static string Read(string key)
    {
        try
        {
            return typeof(TestBuild).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == key)?.Value ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static DateTime? ParseUntil(string text) =>
        DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var until)
            ? until
            : null;
}

/// <summary>The test build's line at the bottom of the screen, always on top (see <see cref="TestBuild"/>).</summary>
internal sealed class TestBuildMark : MonoBehaviour
{
    private Canvas _canvas;
    private TMPro.TextMeshProUGUI _text;
    private float _next, _nextAsk;
    private UnityEngine.Networking.UnityWebRequest _ask;

    private void Update()
    {
        AskDate();
        if (Time.unscaledTime < _next)
            return;
        _next = Time.unscaledTime + 1f;
        if (_canvas == null)
        {
            _canvas = DamageInsight.UI.UiKit.OverlayCanvas("SkulInsight_TestBuild", 2200);
            _text = DamageInsight.UI.UiKit.Text("Line", _canvas.transform, "", 15f, TMPro.TextAlignmentOptions.Bottom,
                new Color(1f, 1f, 1f, 0.75f));
            _text.raycastTarget = false;
            _text.enableWordWrapping = false;
            var rect = _text.rectTransform;
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 4f);
            rect.sizeDelta = new Vector2(0f, 24f);
        }
        _text.text = TestBuild.Expired ? TestBuild.Line + "  ·  ENDED: co-op is off"
            : TestBuild.ServerDate.HasValue ? TestBuild.Line
            : TestBuild.Line + "  ·  co-op waits for the internet";
    }

    /// <summary>Asks the server for today's date: at once, then every 60 s until it answered (and every 30 min after,
    /// so a game left running past the end notices).</summary>
    private void AskDate()
    {
        if (_ask != null)
        {
            if (!_ask.isDone)
                return;
            if (_ask.result == UnityEngine.Networking.UnityWebRequest.Result.Success
                && TestBuild.TryParseServerDate(_ask.downloadHandler.text, out var date))
                TestBuild.ServerDate = date;
            _ask.Dispose();
            _ask = null;
            _nextAsk = Time.unscaledTime + (TestBuild.ServerDate.HasValue ? 1800f : 60f);
            return;
        }
        if (Time.unscaledTime < _nextAsk)
            return;
        _ask = UnityEngine.Networking.UnityWebRequest.Get(TestBuild.DateUrl);
        _ask.timeout = 20;
        _ask.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        _ask.SendWebRequest();
    }
}

using System;
using System.Collections;
using System.Text;
using System.Threading.Tasks;
using DamageInsight.Crash;
using DamageInsight.Lang;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// "Report a bug or idea" (Umut 2026-11-24, docs/coop/REPORTS.md part A): a form opened from the Co-op window, the
/// settings page (General) and the Codex. Bug or idea, a title, the text, "attach my logs" (on for bugs), an optional
/// contact. Send posts it to the mod's server (/api/reports: JSON, or the BugReport zip with report.json inside, packed
/// on a background thread); the server keeps a ticket and opens a public GitHub issue of the title and text. When the
/// server can't be reached (or General/ReportsToServer is off) the old way runs: the zip is saved, its folder and
/// GitHub's page open with the report filled in, and the text goes to the clipboard. Only on the player's click.
/// </summary>
public sealed class ReportWindow : MonoBehaviour
{
    private const float Width = 900f, Height = 740f;
    private static readonly Color Back = new Color32(0x14, 0x11, 0x27, 0xF7);
    private static readonly Color Frame = new Color32(0x5A, 0x50, 0xC8, 0xFF);
    private static readonly Color FieldBack = new Color32(0x0F, 0x0C, 0x22, 0xFF);
    private static readonly Color Ink = new Color32(0xEC, 0xEA, 0xF7, 0xFF);
    private static readonly Color Dim = new Color32(0x9C, 0x97, 0xC4, 0xFF);
    private static readonly Color Green = new Color32(0x3D, 0xDC, 0x97, 0xFF);
    private static readonly Color Red = new Color32(0xE5, 0x62, 0x6A, 0xFF);
    private static readonly Color Blue = new Color32(0x4A, 0x55, 0xC8, 0xFF);
    private static readonly Color Orange = new Color32(0xF2, 0xA9, 0x3B, 0xFF);

    private enum Stage
    {
        Editing,
        Sending,
        Done,
    }

    private static ReportWindow _instance;
    private static int _closedFrame = -1;

    /// <summary>The window is open.</summary>
    public static bool IsOpen => _instance != null && _instance._canvas != null && _instance._canvas.gameObject.activeSelf;

    /// <summary>Open, or closed by a key this frame: the windows below leave this frame's keys alone (Esc, K, ...).</summary>
    public static bool Busy => IsOpen || _closedFrame == Time.frameCount;

    private Canvas _canvas;
    private RectTransform _window;
    private TMP_InputField _title, _text, _contact;
    private TextMeshProUGUI _textLabel, _count, _status;
    private UiKit.Chip _bug, _idea, _logs, _send, _cancel, _openIssue;
    private ReportKind _kind = ReportKind.Bug;
    private bool _attach = true, _logsTouched, _paused, _blocked;
    private Stage _stage;
    private string _issueUrl = "";
    private int _language = -1;

    private void Awake() => _instance = this;

    /// <summary>Opens the form (from the Co-op window, the settings page or the Codex).</summary>
    public static void Open()
    {
        if (_instance != null)
            Patches.Guard.Run("Report window", _instance.Show);
    }

    /// <summary>Esc / Cancel in the pause menu (Panel.Return): taken while the window is open (it closes on Esc by itself).</summary>
    internal static bool HandleBack() => Busy;

    /// <summary>The launcher's window test: the window with a sample text.</summary>
    internal static void TestShow()
    {
        Open();
        if (IsOpen)
        {
            _instance._title.text = "Teammate fell through the floor after a dash";
            _instance._text.text = "In the second room of chapter 3, right after a dash through the gate.\nThe host saw it too.";
            _instance._contact.text = "Tester#1234";
        }
    }

    private void Show()
    {
        if (_canvas != null && _language != Loc.Version)
        {
            Destroy(_canvas.gameObject);
            _canvas = null;
        }
        if (_canvas == null)
            Build();
        if (_stage != Stage.Sending)
        {
            if (_stage == Stage.Done)
                Clear();
            _stage = Stage.Editing;
            Refresh();
        }
        _canvas.gameObject.SetActive(true);
        Hold(true);
        EventSystem.current?.SetSelectedGameObject(null);
        MenuSounds.Play(MenuSounds.Kind.Select);
    }

    private void Close()
    {
        if (_canvas != null)
            _canvas.gameObject.SetActive(false);
        _closedFrame = Time.frameCount;
        Hold(false);
        EventSystem.current?.SetSelectedGameObject(null);
        MenuSounds.Play(MenuSounds.Kind.Close);
    }

    /// <summary>While the window is open the character stands still (typing "x" doesn't attack) and, alone, the game
    /// pauses like with the Codex; in co-op it never pauses (COOP_PLAN 4.5).</summary>
    private void Hold(bool on)
    {
        try
        {
            if (on && !_blocked)
            {
                Characters.Controllers.PlayerInput.blocked.Attach(this);
                _blocked = true;
            }
            else if (!on && _blocked)
            {
                Characters.Controllers.PlayerInput.blocked.Detach(this);
                _blocked = false;
            }
            if (on && !_paused && Coop.CoopWorld.Current?.Active != true)
            {
                Chronometer.global.AttachTimeScale(this, 0f);
                _paused = true;
            }
            else if (!on && _paused)
            {
                Chronometer.global.DetachTimeScale(this);
                _paused = false;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning("Report window: could not hold the game: " + e.Message);
        }
    }

    private void OnDestroy() => Hold(false);

    private void Update()
    {
        if (!IsOpen)
            return;
        Patches.Guard.Run("Report window", () =>
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (Focused() != null)
                    EventSystem.current?.SetSelectedGameObject(null); // Esc first leaves the field; the text stays
                else
                    Close();
            }
            else if (Input.GetKeyDown(KeyCode.Tab) && Focused() is { } field)
            {
                var next = field == _title ? _text : field == _text ? _contact : _title;
                if (field != _text)
                    next.ActivateInputField();
            }
            _count.text = $"{_text.text.Length} / {ReportPayload.TextMax}";
        });
    }

    private TMP_InputField Focused() =>
        _title.isFocused ? _title : _text.isFocused ? _text : _contact.isFocused ? _contact : null;

    // ---------------------------------------------------------------- building

    private void Build()
    {
        _language = Loc.Version;
        _canvas = UiKit.OverlayCanvas("SkulInsight Report window", 5300); // above the Codex (5100) and the pause menu
        _canvas.gameObject.AddComponent<GraphicRaycaster>();
        ModEventSystem.Ensure();
        UiKit.Image("Shade", _canvas.transform, new Color(0, 0, 0, 0.6f)).rectTransform.Fill(); // takes the clicks below

        var window = UiKit.Image("Window", _canvas.transform, Back);
        _window = window.rectTransform;
        _window.anchorMin = _window.anchorMax = _window.pivot = new Vector2(0.5f, 0.5f);
        _window.sizeDelta = new Vector2(Width, Height);
        Outline(window, Frame, 2.5f);

        Text(_window, Loc.T("Report a bug or idea"), 30, Ink).rectTransform.Place(28, 16, Width - 140, 44);
        var close = new UiKit.Chip(_window, "X", Width - 24 - 56, 20, 56, Close, Red);
        close.Background.rectTransform.sizeDelta = new Vector2(56, 36);
        close.ClickSound = false;
        close.SetOn(true);

        float bugWidth = UiKit.ChipWidth(Loc.T("Bug")) + 40, ideaWidth = UiKit.ChipWidth(Loc.T("Idea or wish")) + 40;
        _bug = new UiKit.Chip(_window, Loc.T("Bug"), 28, 70, bugWidth, () => SetKind(ReportKind.Bug), Red);
        _idea = new UiKit.Chip(_window, Loc.T("Idea or wish"), 28 + bugWidth + 10, 70, ideaWidth, () => SetKind(ReportKind.Idea), Green);
        foreach (var chip in new[] { _bug, _idea })
            chip.Background.rectTransform.sizeDelta = new Vector2(chip.Background.rectTransform.sizeDelta.x, 34);

        Text(_window, Loc.T("Title"), 16, Dim).rectTransform.Place(28, 116, 400, 22);
        _title = Field(_window, 28, 140, Width - 56, 40, ReportPayload.TitleMax, false);
        _textLabel = Text(_window, "", 16, Dim);
        _textLabel.rectTransform.Place(28, 192, Width - 56, 22);
        _text = Field(_window, 28, 216, Width - 56, 236, ReportPayload.TextMax, true);
        _count = Text(_window, "", 14, Dim, TextAlignmentOptions.TopRight);
        _count.rectTransform.Place(Width - 28 - 200, 456, 200, 20);

        float logsWidth = Mathf.Max(UiKit.ChipWidth(Loc.T("Logs: attached")), UiKit.ChipWidth(Loc.T("Logs: not attached"))) + 30;
        _logs = new UiKit.Chip(_window, "", 28, 476, logsWidth, ToggleLogs, Blue);
        _logs.Background.rectTransform.sizeDelta = new Vector2(logsWidth, 34);
        Text(_window, Loc.T("This game's logs and settings; Steam ids and your account name are hidden. Only the developer sees them."), 15, Dim)
            .rectTransform.Place(28 + logsWidth + 14, 474, Width - 56 - logsWidth - 14, 40);

        Text(_window, Loc.T("Contact (optional): e.g. your Discord name, if the developer may ask you something"), 16, Dim)
            .rectTransform.Place(28, 526, Width - 56, 22);
        _contact = Field(_window, 28, 550, 460, 38, ReportPayload.ContactMax, false);

        Text(_window, Loc.T("Your title and text become a public GitHub issue. Your logs (if attached) only go to the developer."), 15, Dim)
            .rectTransform.Place(28, 600, Width - 56, 40);
        _status = Text(_window, "", 16, Ink);
        _status.rectTransform.Place(28, 640, Width - 56 - 330, 92);
        _status.enableAutoSizing = true;
        _status.fontSizeMin = 12;
        _status.fontSizeMax = 16;

        _send = new UiKit.Chip(_window, Loc.T("Send"), Width - 28 - 150, Height - 24 - 38, 150, () => Patches.Guard.Run("Report (send)", Send), Orange);
        _cancel = new UiKit.Chip(_window, Loc.T("Cancel"), Width - 28 - 150 - 12 - 150, Height - 24 - 38, 150, Close, Blue);
        float issueWidth = UiKit.ChipWidth(Loc.T("Open the issue")) + 30;
        _openIssue = new UiKit.Chip(_window, Loc.T("Open the issue"), Width - 28 - 150 - 12 - issueWidth, Height - 24 - 38, issueWidth,
            () => { if (_issueUrl.Length > 0) Application.OpenURL(_issueUrl); }, Green);
        foreach (var chip in new[] { _send, _cancel, _openIssue })
            chip.Background.rectTransform.sizeDelta = new Vector2(chip.Background.rectTransform.sizeDelta.x, 38);
        _send.SetOn(true);
        _cancel.SetOn(true);
        _openIssue.SetOn(true);

        Clear();
        _canvas.gameObject.SetActive(false);
    }

    private TMP_InputField Field(RectTransform parent, float x, float y, float w, float h, int limit, bool multiLine)
    {
        var box = UiKit.Image("Field", parent, FieldBack);
        box.rectTransform.Place(x, y, w, h);
        Outline(box, Frame, 1.5f);
        var area = UiKit.Rect("Text Area", box.transform).Fill(8);
        area.gameObject.AddComponent<RectMask2D>();
        var align = multiLine ? TextAlignmentOptions.TopLeft : TextAlignmentOptions.MidlineLeft;
        var text = UiKit.Text("Text", area, "", 18, align, Ink);
        text.rectTransform.Fill();
        text.enableWordWrapping = multiLine;
        text.richText = false; // "<b>" typed stays text
        var input = box.gameObject.AddComponent<TMP_InputField>();
        input.textViewport = area;
        input.textComponent = text;
        input.fontAsset = text.font;
        input.pointSize = 18;
        input.richText = false;
        input.characterLimit = limit;
        input.lineType = multiLine ? TMP_InputField.LineType.MultiLineNewline : TMP_InputField.LineType.SingleLine;
        input.restoreOriginalTextOnEscape = false; // Esc must never throw the text away
        input.onFocusSelectAll = false;
        input.navigation = new Navigation { mode = Navigation.Mode.None }; // arrows move the caret, not to the page below
        input.caretColor = Ink;
        input.selectionColor = new Color(0.35f, 0.3f, 0.8f, 0.5f);
        return input;
    }

    private static TextMeshProUGUI Text(Transform parent, string text, float size, Color color,
        TextAlignmentOptions alignment = TextAlignmentOptions.TopLeft) =>
        UiKit.Text("Text", parent, text, size, alignment, color);

    private static void Outline(Image image, Color color, float distance)
    {
        var outline = image.gameObject.AddComponent<UnityEngine.UI.Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(distance, -distance);
    }

    // ---------------------------------------------------------------- the form

    private void Clear()
    {
        _title.text = "";
        _text.text = "";
        _contact.text = Plugin.ReportContact?.Value ?? "";
        _logsTouched = false;
        _issueUrl = "";
        _status.text = "";
        SetKind(ReportKind.Bug);
    }

    private void SetKind(ReportKind kind)
    {
        _kind = kind;
        if (!_logsTouched)
            _attach = kind == ReportKind.Bug && Plugin.ReportLogs?.Value != false;
        _textLabel.text = kind == ReportKind.Bug
            ? Loc.T("What happened? What did you expect? Where (chapter, room, skull)?")
            : Loc.T("Your idea or wish: what should the mod do?");
        Refresh();
    }

    private void ToggleLogs()
    {
        _attach = !_attach;
        _logsTouched = true;
        Refresh();
    }

    private void Refresh()
    {
        if (_canvas == null)
            return;
        _bug.SetOn(_kind == ReportKind.Bug);
        _idea.SetOn(_kind == ReportKind.Idea);
        _logs.Label.text = _attach ? Loc.T("Logs: attached") : Loc.T("Logs: not attached");
        _logs.SetOn(_attach);
        bool editing = _stage == Stage.Editing;
        _send.Background.gameObject.SetActive(editing);
        _openIssue.Background.gameObject.SetActive(_stage == Stage.Done && _issueUrl.Length > 0);
        _cancel.Label.text = _stage == Stage.Done ? Loc.T("Close") : Loc.T("Cancel");
        _title.interactable = _text.interactable = _contact.interactable = editing;
    }

    private ReportForm Form() => new()
    {
        Kind = _kind, Title = _title.text, Text = _text.text, Contact = _contact.text, AttachLogs = _attach,
    };

    private void Send()
    {
        if (_stage != Stage.Editing)
            return;
        var form = Form();
        var problem = ReportPayload.Check(form);
        if (problem != ReportPayload.Problem.None)
        {
            Say(problem == ReportPayload.Problem.TitleShort ? Loc.T("Please write a title (at least 3 characters).")
                : Loc.F("The server did not take the report ({0}).", problem.ToString()), Red);
            return;
        }
        if (Plugin.ReportContact != null)
            Plugin.ReportContact.Value = ReportPayload.Clean(form.Contact, false);
        if (Plugin.ReportsToServer?.Value == false)
        {
            Fallback(form, Loc.T("Sending to the server is off (settings)."));
            return;
        }
        _stage = Stage.Sending;
        Refresh();
        Say(Loc.T("Sending…"), Ink);
        StartCoroutine(CSend(form));
    }

    private IEnumerator CSend(ReportForm form)
    {
        string build =
#if DEV
            "Debug";
#elif THUNDERSTORE
            "Thunderstore";
#else
            "Release";
#endif
        string json = ReportPayload.Json(form, MyPluginInfo.PLUGIN_VERSION, Application.version, build, Loc.Current,
            Coop.CoopWorld.Current?.Active == true);
        byte[] body = Encoding.UTF8.GetBytes(json);
        string type = "application/json";
        if (form.AttachLogs)
        {
            Task<byte[]> pack = null;
            try
            {
                var parts = BugReport.Parts(new ZipPart { Name = "report.json", Data = body });
                string user = Environment.UserName;
                var now = DateTime.Now;
                pack = BackgroundWork.Track(Task.Run(() => ReportZip.Build(parts, ReportPayload.ZipMax, user, now)));
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("report: the logs could not be gathered: " + e.Message);
            }
            while (pack != null && !pack.IsCompleted)
                yield return null;
            if (pack != null && pack.Status == TaskStatus.RanToCompletion && pack.Result != null)
            {
                body = pack.Result;
                type = "application/zip";
            }
            else if (pack?.Exception != null)
                Plugin.Log.LogWarning("report: the logs could not be packed: " + pack.Exception.InnerException?.Message);
        }

        string server = (Plugin.DailyServer?.Value ?? "").TrimEnd('/');
        using var request = new UnityWebRequest(server + ReportPayload.ReportsPath, UnityWebRequest.kHttpVerbPOST)
        {
            uploadHandler = new UploadHandlerRaw(body),
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = 120,
        };
        request.SetRequestHeader("Content-Type", type);
        request.SetRequestHeader("User-Agent", "SkulInsight/" + MyPluginInfo.PLUGIN_VERSION);
        yield return request.SendWebRequest();
        long code = request.result is UnityWebRequest.Result.Success or UnityWebRequest.Result.ProtocolError ? request.responseCode : 0;
        string text = request.downloadHandler?.text ?? "";
        Plugin.Log.LogInfo($"report: sent {body.Length / 1024} KB ({type}), answer {code}");
        Patches.Guard.Run("Report (answer)", () => Answered(form, code, text));
    }

    private void Answered(ReportForm form, long code, string text)
    {
        var answer = code == 200 ? ReportPayload.ParseAnswer(text) : null;
        if (answer != null)
        {
            _stage = Stage.Done;
            _issueUrl = answer.Issue;
            string said = Loc.F("Thank you! Your report is ticket {0}.", answer.Ticket);
            if (answer.Issue.Length > 0)
                said += " " + Loc.F("It is public on GitHub as issue #{0}.", answer.IssueNumber);
            if (answer.LogsRefused.Length > 0)
                said += " " + Loc.T("The server was full: your logs were not sent.");
            Say(said, Green);
            Plugin.Log.LogInfo($"report: ticket {answer.Ticket}" + (answer.Issue.Length > 0 ? ", issue " + answer.Issue : ""));
            Refresh();
            return;
        }
        if (code == 429)
        {
            _stage = Stage.Editing;
            Refresh();
            Say(Loc.T("You sent several reports in a short time. Please try again in an hour."), Red);
            return;
        }
        if (code == 400)
        {
            _stage = Stage.Editing;
            Refresh();
            Say(Loc.F("The server did not take the report ({0}).", ReportPayload.RefusedField(text)), Red);
            return;
        }
        Fallback(form, Loc.T("The server could not be reached."));
    }

    /// <summary>The old way (BugReport): the text to the clipboard, GitHub's page with the report filled in, and for a
    /// report with logs the zip in its folder.</summary>
    private void Fallback(ReportForm form, string why)
    {
        GUIUtility.systemCopyBuffer = ReportPayload.Clean(form.Title, false) + "\n\n" + ReportPayload.Clean(form.Text, true);
        string said = why + " " + Loc.T("GitHub's page opened with your report (it needs a GitHub account); your text is also in the clipboard (Ctrl+V).");
        if (form.AttachLogs)
        {
            string zip = BugReport.MakeAndOpen(true, out _, form);
            if (zip != null)
                said += " " + Loc.T("The logs are in the folder that opened: drag the zip into the GitHub page.");
        }
        else
            Application.OpenURL(BugReport.IssueUrl(null, form));
        _stage = Stage.Done;
        _issueUrl = "";
        Refresh();
        Say(said, Orange);
    }

    private void Say(string text, Color color)
    {
        _status.text = text;
        _status.color = color;
    }
}

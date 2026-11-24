using System.Collections;
using System.IO;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop.Testing;

/// <summary>
/// Picture of the other-co-op-mod warning (`--skulinsight-repro=conflict` on a test copy, COOP_LOG 73): in the castle
/// it pretends OnlineCoopBeta was found, opens the warning, writes what a host / search would answer, saves a picture
/// to &lt;data&gt;/Repro/conflict-notice.png and quits. Developer tool only: English.
/// </summary>
public sealed class ConflictNoticeTest : MonoBehaviour
{
    private float _since = -1f;
    private bool _started;

    private void Update()
    {
        if (_started || Singleton<Service>.Instance?.levelManager?.player == null)
            return;
        if (_since < 0f)
            _since = Time.unscaledTime;
        if (Time.unscaledTime - _since < 6f)
            return;
        _started = true;
        StartCoroutine(CRun());
    }

    private IEnumerator CRun()
    {
        CoopConflicts.PretendFound(CoopConflicts.OnlineCoopBeta);
        Plugin.Log.LogInfo("[ConflictTest] blocker: " + (CoopConflicts.Blocker ?? "(none)"));
        UI.CoopConflictNotice.ShowNow();
        Plugin.Log.LogInfo("[ConflictTest] notice open: " + UI.CoopConflictNotice.IsOpen);
        yield return new WaitForSecondsRealtime(1.5f);
        string folder = Path.Combine(ModData.Root, "Repro");
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "conflict-notice.png");
        ScreenCapture.CaptureScreenshot(file);
        yield return new WaitForSecondsRealtime(1.5f);
        Plugin.Log.LogInfo("[ConflictTest] picture: " + file + " (" + File.Exists(file) + ")");
        Application.Quit();
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DamageInsight.Codex;
using DamageInsight.History;
using UnityEngine;
using UnityEngine.UI;

namespace DamageInsight.UI;

/// <summary>
/// A past run's end screen, as the game showed it (Umut 2026-10-16: "something along the lines of this, with the dark
/// powers"): a copy of the game's own result panel (UI.GameResult: its art, labels, icons and layout, in the current
/// language), filled with the run's numbers, picture, gear and dark abilities. The copy has none of the panel's game
/// logic (that pauses the game and waits for a key); only pictures, texts and their layout stay. Click, Esc or Enter
/// closes it.
/// </summary>
public sealed class EndScreenView : MonoBehaviour
{
    private static EndScreenView _open;

    public static bool IsOpen => _open != null;

    private Texture2D _photo;
    private Transform _container;
    private Vector3 _target;
    private AnimationCurve _curve;
    private float _time, _openedAt;

    /// <summary>
    /// Opens the end screen of <paramref name="run"/>; false when the game's panel isn't there (title screen).
    /// <paramref name="portrait"/> stands in for the picture when the run has none (the boss or the killer).
    /// </summary>
    public static bool Open(RunRecord run, Sprite portrait = null)
    {
        Close();
        var source = Scenes.Scene<Scenes.GameBase>.instance?.uiManager?.gameResult;
        if (source == null || source.gameObject.activeSelf)
            return false; // no game scene, or the game shows its own end screen right now
        Canvas canvas = null;
        try
        {
            canvas = UiKit.OverlayCanvas("DamageInsight_EndScreen", 5300);
            canvas.gameObject.AddComponent<GraphicRaycaster>();
            MatchGameCanvas(source, canvas);
            UiKit.Image("Dim", canvas.transform, new Color(0, 0, 0, 0.8f)).rectTransform.Fill();
            var view = canvas.gameObject.AddComponent<EndScreenView>();
            view.Build(source, canvas.transform, run, portrait);
            _open = view;
            MenuSounds.Play(MenuSounds.Kind.Select);
            return true;
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"End screen view failed: {e}");
            if (canvas != null)
                Destroy(canvas.gameObject);
            _open = null;
            return false;
        }
    }

    public static void Close()
    {
        if (_open == null)
            return;
        var view = _open;
        _open = null;
        if (view._photo != null)
            Destroy(view._photo);
        Destroy(view.gameObject);
    }

    /// <summary>
    /// The game's canvas scales its pixel-art sprites with its own "pixels per unit": without it the gear icons (sized
    /// by SetNativeSize, like the game does) came out three to four times too big.
    /// </summary>
    private static void MatchGameCanvas(global::UI.GameResult source, Canvas canvas)
    {
        var canvases = source.GetComponentsInParent<Canvas>(true); // the panel is hidden: only this form finds its canvas
        var gameCanvas = canvases.Length > 0 ? canvases[canvases.Length - 1] : null;
        var gameScaler = gameCanvas != null ? gameCanvas.GetComponent<CanvasScaler>() : null;
        var scaler = canvas.GetComponent<CanvasScaler>();
        if (gameScaler == null || scaler == null)
            return;
        scaler.uiScaleMode = gameScaler.uiScaleMode;
        scaler.referenceResolution = gameScaler.referenceResolution;
        scaler.screenMatchMode = gameScaler.screenMatchMode;
        scaler.matchWidthOrHeight = gameScaler.matchWidthOrHeight;
        scaler.referencePixelsPerUnit = gameScaler.referencePixelsPerUnit;
        canvas.referencePixelsPerUnit = gameScaler.referencePixelsPerUnit; // the scaler sets it only on its next update
        Plugin.Log.LogInfo($"End screen view: game canvas {gameScaler.uiScaleMode} {gameScaler.referenceResolution} " +
                           $"{gameScaler.referencePixelsPerUnit} px/unit");
    }

    private void Build(global::UI.GameResult source, Transform parent, RunRecord run, Sprite portrait)
    {
        // The source is inactive (hidden): so is its copy, and nothing of it runs until it is cleaned.
        var clone = Instantiate(source.gameObject, parent, false);
        clone.name = "Run end screen";
        var r = clone.GetComponent<global::UI.GameResult>();

        // Its parts, read before the panel's own logic is removed.
        _container = r._container.transform;
        _curve = r._curve;
        var screen = RunPages.Screen(run);
        r._panel.sprite = screen.Panel switch { "emperor10" => r._emperor10Panel, "hardmode" => r._hardmodePanel, _ => r._normalmodePanel };
        r._yourEnemy.gameObject.SetActive(screen.YourEnemy);
        if (screen.YourEnemy)
            r._yourEnemy.sprite = screen.Panel switch { "emperor10" => null, "hardmode" => r._yourEnemyInHard, _ => r._yourEnemyInNormal };
        r._title.text = GameText(screen.Title, run.Level);
        r._subTitle.text = GameText(screen.Subtitle, run.Level);
        r._stageName.text = screen.Stage.Length > 0 ? GameText(screen.Stage, run.Level) : run.EndStageName;

        string N(string key) => ((long)Math.Round(run.Number(key))).ToString(CultureInfo.InvariantCulture);
        int play = run.Numbers.ContainsKey(RunNumbers.PlayTime) ? (int)run.Number(RunNumbers.PlayTime) : run.PlaySeconds;
        r._playTime.text = TimeSpan.FromSeconds(play).ToString("hh\\:mm\\:ss", CultureInfo.InvariantCulture);
        r._deaths.text = N(RunNumbers.Deaths);
        r._kills.text = N(RunNumbers.Kills);
        r._darkQuartz.text = N(RunNumbers.DarkQuartz);
        r._gold.text = N(RunNumbers.Gold);
        r._bone.text = N(RunNumbers.Bones);
        r._totalDamage.text = N(RunNumbers.TotalDamage);
        r._totalTakingDamage.text = N(RunNumbers.DamageTaken);
        r._bestDamage.text = N(RunNumbers.BestDamage);
        r._totalHeal.text = N(RunNumbers.Healed);
        var (skulls, items, essences) = RunPages.Obtained(run);
        r._encounterWeapon.text = skulls.ToString(CultureInfo.InvariantCulture);
        r._encounterItem.text = items.ToString(CultureInfo.InvariantCulture);
        r._encounterEssence.text = essences.ToString(CultureInfo.InvariantCulture);

        // The picture: the one the game took for this run, or the boss / killer.
        r._endingPortrait.SetActive(false);
        r._deathCam.gameObject.SetActive(true);
        _photo = LoadPhoto(run);
        if (_photo != null)
        {
            r._deathCam.texture = _photo;
            r._deathCam.uvRect = new Rect(0, 0, 1, 1);
            r._deathCam.color = Color.white;
        }
        else
        {
            r._deathCam.texture = null;
            r._deathCam.color = new Color(0, 0, 0, 0.6f);
            if (portrait != null)
            {
                // No picture (a win from before the pictures of wins were kept): the last boss or the killer instead.
                var stand = UiKit.Image("Portrait", r._deathCam.transform, Color.white, portrait);
                stand.preserveAspect = true;
                stand.rectTransform.Fill(24);
            }
        }

        // The gear row (skulls, quintessence, items, fragment) and the dark abilities, like the game fills them.
        Clear(r._gearListContainer);
        Clear(r._upgradeListListContainer);
        var (top, rowItems) = RunPages.Gear(run);
        var gear = top.Where(g => g.Kind == "skull")
            .Concat(top.Where(g => g.Kind == "essence"))
            .Concat(rowItems)
            .Concat(top.Where(g => g.Kind == "fragment"));
        foreach (var g in gear)
            AddIcon(r._gearContainerPrefab, r._gearListContainer, InventoryIcon(g));
        if (run.DarkMirror)
            foreach (var g in top.Where(g => g.Kind == "dark"))
                AddIcon(r._upgradeContainerPrefab, r._upgradeListListContainer, InventoryIcon(g));

        // Only pictures, texts and their layout stay: the panel's own logic would pause the game and wait for a key.
        DestroyImmediate(r);
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || Keep(behaviour))
                continue;
            try { DestroyImmediate(behaviour); }
            catch (Exception) { }
        }
        clone.SetActive(true);

        _target = _container.position;
        _container.position = _target + new Vector3(0, 200f, 0); // the game's own slide-in (GameResult.CAnimate)
        _openedAt = Time.unscaledTime;
    }

    private static bool Keep(MonoBehaviour behaviour)
    {
        string ns = behaviour.GetType().Namespace ?? "";
        return ns.StartsWith("UnityEngine", StringComparison.Ordinal) || ns.StartsWith("TMPro", StringComparison.Ordinal) ||
               behaviour is global::UI.TextLocalizer || behaviour is global::UI.TextLocalizerByMode ||
               behaviour is global::UI.GearImageContainer;
    }

    private static string GameText(string key, int level)
    {
        if (string.IsNullOrEmpty(key))
            return "";
        string text = CodexCatalog.Localize(key);
        try { return string.Format(text, level); }
        catch (FormatException) { return text; }
    }

    private static void Clear(Transform container)
    {
        if (container == null)
            return;
        for (int i = container.childCount - 1; i >= 0; i--)
            DestroyImmediate(container.GetChild(i).gameObject);
    }

    private static void AddIcon(global::UI.GearImageContainer prefab, Transform container, Sprite sprite)
    {
        if (prefab == null || container == null || sprite == null)
            return;
        var icon = Instantiate(prefab, container);
        icon.image.sprite = sprite;
        icon.image.SetNativeSize();
    }

    /// <summary>The icon the game's own end screen uses for a piece of gear (its inventory icon).</summary>
    private static Sprite InventoryIcon(RunGear g)
    {
        try
        {
            if (g.Kind == "fragment")
                return CodexCatalog.FragmentIcon(g.Id);
            var entry = CodexPages.Entry(g.Id);
            if (entry == null)
                return null;
            if (g.Kind == "dark")
                return CodexCatalog.DarkOf(entry) is { } dark ? dark.icon ?? dark.thumbnail : null;
            return CodexCatalog.GearOf(entry) is { } reference ? reference.icon ?? reference.thumbnail : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static Texture2D LoadPhoto(RunRecord run)
    {
        try
        {
            string path = RunJournal.PathOf(run.Id, ".jpg");
            if (!run.Picture || !File.Exists(path))
                return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (texture.LoadImage(File.ReadAllBytes(path)))
                return texture;
            Destroy(texture);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"End screen view: picture not loaded: {e.Message}");
        }
        return null;
    }

    private void Update()
    {
        if (_container != null && _time < 1f)
        {
            _time = Mathf.Min(1f, _time + Time.unscaledDeltaTime);
            var start = _target + new Vector3(0, 200f, 0);
            _container.position = Vector3.LerpUnclamped(start, _target, _curve != null ? _curve.Evaluate(_time) : _time);
        }
        if (Time.unscaledTime - _openedAt < 0.3f)
            return; // the click that opened it
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Backspace) ||
            Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1))
        {
            MenuSounds.Play(MenuSounds.Kind.Close);
            Close();
        }
    }
}

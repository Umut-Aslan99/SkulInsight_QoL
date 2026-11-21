using System.Collections.Generic;
using System.Text;
using Characters;
using Characters.Controllers;
using Services;
using Singletons;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Watches this game's player during a co-op session (flight recorder lite, COOP_LOG 64): hidden parts (collider,
/// renderer, attach object, the game's Visibility), the controls held without a dialogue (and by what) and the
/// player's parent, fed every frame to <see cref="PlayerWatchLogic"/>: changes go to the detailed trace
/// ("player-state"), long ones to the trace and the always-on session report ("anomaly"). The unstuck key
/// (<see cref="CoopUnstuck"/>) uses <see cref="Repair"/> and the holder helpers to get a stuck player going again.
/// Reads a few fields a frame and builds a text only when something changed.
/// </summary>
internal static class PlayerWatch
{
    private static PlayerWatchLogic _logic;
    private static int _hiddenMask = 0;
    private static string _hiddenText = "";
    private static readonly List<object> LastHolders = new();
    private static string _controlsText = "";
    private static Transform _lastParent;
    private static string _parentText = "";

    /// <summary>Every frame from the co-op runtime.</summary>
    public static void Tick()
    {
        var world = CoopWorld.Current;
        if (world?.Active != true)
            return;
        var player = Singleton<Service>.Instance?.levelManager?.player;
        _logic ??= new PlayerWatchLogic(OnChange, OnAnomaly);
        bool normal = player == null || world.Busy || !world.RoomLoaded || player.health == null || player.health.dead ||
                      (CoopRevive.Current?.IsDown(player) ?? false);
        double now = Time.unscaledTimeAsDouble;
        if (normal)
        {
            _logic.Sample(now, "", "", "");
            return;
        }
        _logic.Sample(now, HiddenText(player), ControlsText(), ParentText(player));
    }

    /// <summary>Seconds the controls have been held without a dialogue (0: free).</summary>
    public static double BlockedFor => _logic?.For(PlayerWatchLogic.Part.Controls, Time.unscaledTimeAsDouble) ?? 0;

    private static void OnChange(PlayerWatchLogic.Part part, string text) =>
        CoopTrace.Write("player-state", CoopWorld.CurrentRoom(), name: part.ToString().ToLowerInvariant(), what: text);

    private static void OnAnomaly(string text)
    {
        CoopTrace.Write("anomaly", CoopWorld.CurrentRoom(), what: text);
        Plugin.Log.LogWarning("[Coop] anomaly: " + text); // CoopDiagnostics' listener puts it in the session report
    }

    // ------------------------------------------------------------------ hidden parts

    private static int HiddenMask(Character player)
    {
        int mask = 0;
        if (player.collider != null && !player.collider.enabled)
            mask |= 1;
        var main = player.spriteEffectStack != null ? player.spriteEffectStack.mainRenderer : null;
        if (main != null && !main.enabled)
            mask |= 2;
        if (player.attach != null && !player.attach.activeSelf)
            mask |= 4;
        var visibility = player.playerComponents?.visibility;
        if (visibility != null && !visibility.visible)
            mask |= 8;
        if (!player.gameObject.activeInHierarchy)
            mask |= 16;
        return mask;
    }

    private static string HiddenText(Character player)
    {
        int mask = HiddenMask(player);
        if (mask == _hiddenMask)
            return _hiddenText;
        _hiddenMask = mask;
        var text = new StringBuilder();
        void Add(int bit, string what)
        {
            if ((mask & bit) == 0)
                return;
            if (text.Length > 0)
                text.Append(", ");
            text.Append(what);
        }
        Add(1, "collider off");
        Add(2, "renderer off");
        Add(4, "attach off");
        Add(8, "out of view (Visibility)");
        Add(16, "object off");
        _hiddenText = text.ToString();
        return _hiddenText;
    }

    /// <summary>
    /// Makes this game's player whole again: collider, the main renderer and every skull's own renderer, the attach
    /// object, the game's Visibility. Returns what was fixed ("collider, renderer"), or null when nothing was hidden.
    /// </summary>
    public static string Repair(Character player)
    {
        if (player == null)
            return null;
        var fixedParts = new List<string>();
        if (player.collider != null && !player.collider.enabled)
        {
            player.collider.enabled = true;
            fixedParts.Add("collider");
        }
        var main = player.spriteEffectStack != null ? player.spriteEffectStack.mainRenderer : null;
        if (main != null && !main.enabled)
        {
            main.enabled = true;
            fixedParts.Add("renderer");
        }
        var weapons = player.playerComponents?.inventory?.weapon?.weapons;
        if (weapons != null)
        {
            foreach (var weapon in weapons)
            {
                var body = weapon != null && weapon.equipped != null ? weapon.equipped.GetComponent<SpriteRenderer>() : null;
                if (body != null && body != main && !body.enabled)
                {
                    body.enabled = true;
                    fixedParts.Add("renderer of " + weapon.name);
                }
            }
        }
        if (player.attach != null && !player.attach.activeSelf)
        {
            player.attach.SetActive(true);
            fixedParts.Add("attach");
        }
        var visibility = player.playerComponents?.visibility;
        if (visibility != null && !visibility.visible)
        {
            visibility.SetVisible(true);
            fixedParts.Add("Visibility");
        }
        return fixedParts.Count == 0 ? null : string.Join(", ", fixedParts);
    }

    // ------------------------------------------------------------------ the controls

    private static string ControlsText()
    {
        var holders = PlayerInput.blocked._list;
        if (holders == null || holders.Count == 0 || global::UI.Dialogue.anyDialogueOpened)
        {
            LastHolders.Clear();
            _controlsText = "";
            return "";
        }
        bool same = holders.Count == LastHolders.Count;
        for (int i = 0; same && i < holders.Count; i++)
            same = ReferenceEquals(holders[i], LastHolders[i]);
        if (same)
            return _controlsText;
        LastHolders.Clear();
        LastHolders.AddRange(holders);
        _controlsText = HolderNames();
        return _controlsText;
    }

    /// <summary>What holds the controls now, e.g. "LetterBox (CutScenes.Shots.Events.LetterBox), destroyed Foo".</summary>
    public static string HolderNames()
    {
        var holders = PlayerInput.blocked._list;
        if (holders == null || holders.Count == 0)
            return "";
        var text = new StringBuilder();
        foreach (var holder in holders)
        {
            if (text.Length > 0)
                text.Append(", ");
            text.Append(NameOf(holder));
        }
        return text.ToString();
    }

    private static string NameOf(object holder) => holder switch
    {
        null => "null",
        UnityEngine.Object unity when unity == null => "destroyed " + holder.GetType().Name,
        UnityEngine.Object unity => unity.name + " (" + holder.GetType().FullName + ")",
        _ => holder.GetType().FullName,
    };

    /// <summary>Lets go of holders that are destroyed Unity objects (they can never let go themselves). Count.</summary>
    public static int FreeDestroyedHolders()
    {
        var holders = PlayerInput.blocked._list;
        if (holders == null)
            return 0;
        return holders.RemoveAll(h => h is UnityEngine.Object unity && unity == null);
    }

    /// <summary>Lets go of every holder (the unstuck key pressed twice). Returns their names.</summary>
    public static string FreeAllHolders()
    {
        string names = HolderNames();
        PlayerInput.blocked._list?.Clear();
        return names;
    }

    // ------------------------------------------------------------------ the parent

    private static string ParentText(Character player)
    {
        var parent = player.transform.parent;
        if (parent == _lastParent)
            return _parentText;
        _lastParent = parent;
        // in a room the player sits under the scene's GameBase (seen in the 2026-11-21 bot run): that is normal
        _parentText = parent == null || parent.name == "GameBase" ? "" : parent.name;
        return _parentText;
    }
}

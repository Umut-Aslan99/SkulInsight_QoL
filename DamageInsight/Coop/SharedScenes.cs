using System;
using System.Collections;
using System.Collections.Generic;
using DamageInsight.Coop.Net;
using HarmonyLib;
using Level;
using Runnables;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// Story scenes of a room start together (COOP_PLAN 4.6, T.md Open 3). The game starts a scene (Runnables.ACutScene:
/// letterbox, talk, the actors' moves) from a trigger of the room, which watches THIS game's player. So each game
/// played it when its own player got there: a veteran adventurer's intro talk ran on the host at 44 s and on the
/// client at 61 s, over the fight that had begun meanwhile (n4, 2026-11-11). Now:
/// - the host's game starts scenes: when one starts there, every client starts the same one;
/// - a client's own trigger starts nothing; it asks the host, whose game starts the scene for everyone (whoever gets
///   there first starts it, as a single player would);
/// - only story scenes are shared (<see cref="IsStory"/>): the game runs much of a room's plain logic as "cut
///   scenes" too (a map's "On Entered", gates, the castle's NPC switches: first try 2026-11-11, a client in the
///   castle asked the host in another room for five of them and never ran them);
/// - a scene that isn't part of the room (not below the map), a scene in the castle and a scene the host can't
///   start (it is in another room) stay each game's own.
/// The lines of a shared scene go on together: one press by any player moves a line on for everyone (WaitLine).
/// </summary>
internal sealed class SharedScenes : IDisposable
{
    internal static SharedScenes Current;

    private readonly CoopSession _session;
    private readonly NetWriter _writer = new();
    /// <summary>"room:scene" of the scenes started here (a client: on the host's word).</summary>
    private readonly HashSet<string> _started = new(StringComparer.Ordinal);
    private readonly HashSet<string> _asked = new(StringComparer.Ordinal);
    /// <summary>The host's scenes this game couldn't start yet (its room isn't loaded).</summary>
    private readonly List<(uint Room, string Key, float Until)> _waiting = new();
    private bool _applying;
    /// <summary>When each scene last started here, by "room:scene" (<see cref="Twice"/>).</summary>
    private readonly Dictionary<string, float> _lastStart = new(StringComparer.Ordinal);
    public const float TwiceSeconds = 3f;

    /// <summary>
    /// The same scene again within a moment is one start that arrived twice, not a second showing (dm6's first room,
    /// 2026-11-11): on the host a client's ask starts the scene and the host's own trigger, set off by that client's
    /// copy, starts it again 0.15 s later; a client that came into the room late gets the start it had waiting and
    /// the one sent again for its ask. A scene run twice plays every shot twice at once.
    /// </summary>
    private bool Twice(string id, float now) => _lastStart.TryGetValue(id, out float at) && now - at < TwiceSeconds;

    private void Started(string id, float now)
    {
        if (_lastStart.Count > 200)
            _lastStart.Clear();
        _lastStart[id] = now;
    }

    public SharedScenes(CoopSession session)
    {
        _session = session;
        Current = this;
        session.RegisterHandler(MessageType.CutScene, OnMessage);
    }

    public void Dispose()
    {
        if (Current == this)
            Current = null;
    }

    private bool Active => _session.State == SessionState.Active && _session.Roster.Count > 1;

    /// <summary>A scene's name on every game: its path below the map and its number among the scenes of that path;
    /// null when it isn't part of the map.</summary>
    internal static string KeyOf(ACutScene scene, Map map)
    {
        if (scene == null || map == null || !scene.transform.IsChildOf(map.transform))
            return null;
        string path = RemoteAvatar.PathOf(scene.transform, map.transform);
        int number = 0;
        foreach (var other in map.GetComponentsInChildren<ACutScene>(true))
        {
            if (other == scene)
                return path + "#" + number;
            if (RemoteAvatar.PathOf(other.transform, map.transform) == path)
                number++;
        }
        return null;
    }

    private static readonly Dictionary<ACutScene, bool> Stories = new();

    /// <summary>A story scene: it talks or shows the letterbox, and it offers no choice box (an NPC's service is
    /// private, COOP_PLAN 4.6). Everything else an ACutScene runs is a room's own logic.</summary>
    internal static bool IsStory(ACutScene scene)
    {
        if (Stories.TryGetValue(scene, out bool story))
            return story;
        if (IsPersonal(scene))
            return Stories[scene] = false;
        if (Stories.Count > 300)
            Stories.Clear();
        bool talks = false, service = false;
        foreach (var part in Parts(scene))
        {
            if (part == null)
                continue;
            var type = part.GetType();
            string space = type.Namespace ?? "", name = type.Name;
            if (space == "CutScenes.Shots.Sequences")
            {
                if (name.Contains("Selector"))
                    service = true;
                else if (name.Contains("Talk") || name.Contains("Dialog") || name == "ShowLine" || name == "NextLetterBoxText")
                    talks = true;
            }
            else if (space == "CutScenes.Shots.Events" && name == "ControlLetterBox")
                talks = true;
        }
        return Stories[scene] = talks && !service;
    }

    /// <summary>
    /// A scene of one player's own service: Arachne's awakening, a road NPC's offer, a Darktech machine. Shared, it
    /// played on the partner's game too (Umut 2026-11-20, finding 3): the partner walked to Arachne's cocoon, its own
    /// player vanished, Arachne's upgrade ran there without a skull (NullReferenceException in Arachne.CUpgrade on the
    /// host twice) and the partner's next awakening was free. Each player uses these for their own run.
    /// </summary>
    internal static bool IsPersonal(ACutScene scene)
    {
        if (scene == null)
            return false;
        for (var t = scene.transform; t != null; t = t.parent)
        {
            if (t.GetComponent<Level.Npc.Arachne>() != null || t.GetComponent<Level.Npc.FieldNpcs.FieldNpc>() != null)
                return true;
            foreach (var component in t.GetComponents<MonoBehaviour>())
                if (component != null && component.GetType().Namespace == "Hardmode.Darktech")
                    return true;
            if (t.GetComponent<Map>() != null)
                break;
        }
        return false;
    }

    /// <summary>The last scene with talk that started on this game: personal (each player's own, or not shared at
    /// all) or shared, and when. CoopWorld's stuck-scene stopgap only touches a shared one.</summary>
    internal static bool LastTalkPersonal { get; private set; }

    private static void NoteTalk(ACutScene scene, bool personal)
    {
        if (!Talks(scene))
            return;
        LastTalkPersonal = personal;
    }

    private static readonly Dictionary<ACutScene, bool> TalkCache = new();

    /// <summary>A scene with lines or a letterbox (a story or a service talk), not a room's plain logic.</summary>
    private static bool Talks(ACutScene scene)
    {
        if (scene == null)
            return false;
        if (TalkCache.TryGetValue(scene, out bool talks))
            return talks;
        if (TalkCache.Count > 300)
            TalkCache.Clear();
        talks = false;
        foreach (var part in Parts(scene))
        {
            if (part == null)
                continue;
            var type = part.GetType();
            string space = type.Namespace ?? "", name = type.Name;
            if ((space == "CutScenes.Shots.Sequences" && (name.Contains("Talk") || name.Contains("Dialog") || name == "ShowLine"
                                                          || name == "NextLetterBoxText"))
                || (space == "CutScenes.Shots.Events" && name == "ControlLetterBox"))
            {
                talks = true;
                break;
            }
        }
        return TalkCache[scene] = talks;
    }

    /// <summary>The scripts of a scene: below it, and below its shots when those sit elsewhere.</summary>
    private static IEnumerable<MonoBehaviour> Parts(ACutScene scene)
    {
        foreach (var part in scene.GetComponentsInChildren<MonoBehaviour>(true))
            yield return part;
        var shots = scene._shotInfos?.components;
        if (shots == null)
            yield break;
        foreach (var shot in shots)
        {
            if (shot == null || shot.transform.IsChildOf(scene.transform))
                continue;
            foreach (var part in shot.GetComponentsInChildren<MonoBehaviour>(true))
                yield return part;
        }
    }

    private static bool InCastle() =>
        Singletons.Singleton<Services.Service>.Instance?.levelManager?.currentChapter?.type == Chapter.Type.Castle;

    private const byte Start = 0, Ask = 1, YourOwn = 2, Line = 3;

    private static ACutScene Find(Map map, string key)
    {
        if (map == null)
            return null;
        foreach (var scene in map.GetComponentsInChildren<ACutScene>(true))
        {
            if (KeyOf(scene, map) == key)
                return scene;
        }
        return null;
    }

    // ------------------------------------------------------------------ lines

    // The shared scene whose dialogue lines are counted here, how many of its waits this game has begun, the
    // furthest line any OTHER game pressed on, and a line that arrived for a scene this game has not started yet.
    private uint _talkRoom, _aheadRoom;
    private string _talkKey, _aheadKey;
    private int _line, _remoteLine, _aheadLine;
    private float _talkAt;

    private void BeginTalk(uint room, string key)
    {
        _talkRoom = room;
        _talkKey = key;
        _line = 0;
        _remoteLine = room == _aheadRoom && key == _aheadKey ? _aheadLine : 0;
        _aheadKey = null;
        _talkAt = Time.unscaledTime;
    }

    /// <summary>A wait for a key that begins now belongs to the shared scene: this game is in its room, and the
    /// scene shows its letterbox or a line of it ran in the last 20 s (a talk at an NPC afterwards is private).</summary>
    private bool TalkShared()
    {
        if (!Active || _talkKey == null || _talkRoom != CoopWorld.CurrentRoom())
            return false;
        if (Time.unscaledTime - _talkAt < 20f)
            return true;
        var ui = Scenes.Scene<Scenes.GameBase>.instance?.uiManager;
        return ui != null && ui.letterBox != null && ui.letterBox.visible;
    }

    /// <summary>
    /// A dialogue line of a shared scene (COOP_PLAN 4.6; Umut 2026-11-13: not "everyone pressed" per line, "both
    /// players need to click many many times"): ONE press by any player moves the line on for everyone. The game's
    /// own wait runs as before; it also ends when another game pressed on this line or a later one (a game that is
    /// behind catches up line by line). A press names its line, so two players pressing at once move one line.
    /// </summary>
    private IEnumerator WaitLine(IEnumerator original, Action endLikeAPress)
    {
        int line = ++_line;
        uint room = _talkRoom;
        string key = _talkKey;
        _talkAt = Time.unscaledTime;
        float botAt = Time.unscaledTime + 1.5f; // a test bot presses no keys
        bool mine;
        while (true)
        {
            if (room == _talkRoom && key == _talkKey && _remoteLine >= line)
            {
                mine = false;
                break;
            }
            if (LocalBot.Active)
            {
                if (Time.unscaledTime >= botAt)
                {
                    mine = true;
                    break;
                }
                yield return null;
                continue;
            }
            if (!original.MoveNext())
            {
                mine = true;
                break;
            }
            yield return original.Current;
        }
        _talkAt = Time.unscaledTime;
        if (!mine || LocalBot.Active)
            endLikeAPress?.Invoke(); // the game's own wait did not run to its end here
        if (!mine)
        {
            CoopTrace.Write("line-followed", room, what: key + " #" + line);
            yield break;
        }
        CoopTrace.Write("line-pressed", room, what: key + " #" + line);
        if (_session.State != SessionState.Active)
            yield break;
        Write(room, key, Line);
        _writer.WriteUInt16((ushort)line);
        if (_session.IsHost)
            _session.Broadcast(MessageType.CutScene, _writer, Delivery.Reliable);
        else
            _session.Send(0, MessageType.CutScene, _writer, Delivery.Reliable);
    }

    private void OnLine(uint room, string key, int line, int fromSlot)
    {
        if (room == _talkRoom && key == _talkKey)
            _remoteLine = Math.Max(_remoteLine, line);
        else
        {
            // the scene starts here a moment later (its start is still on the way, or this game is loading)
            _aheadLine = room == _aheadRoom && key == _aheadKey ? Math.Max(_aheadLine, line) : line;
            _aheadRoom = room;
            _aheadKey = key;
        }
        if (_session.IsHost && fromSlot != _session.LocalSlot)
        {
            // 3-4 players: the other clients hear it from the host
            Write(room, key, Line);
            _writer.WriteUInt16((ushort)line);
            _session.Broadcast(MessageType.CutScene, _writer, Delivery.Reliable);
        }
    }

    private static void ShareLine(ref IEnumerator wait, Action endLikeAPress)
    {
        var scenes = Current;
        if (scenes != null && wait != null && scenes.TalkShared())
            wait = scenes.WaitLine(wait, endLikeAPress);
    }

    [HarmonyPatch(typeof(global::UI.NpcConversation), nameof(global::UI.NpcConversation.CWaitInput))]
    [HarmonyPriority(Priority.Last)]
    public static class ConversationLine
    {
        private static void Postfix(global::UI.NpcConversation __instance, ref IEnumerator __result) =>
            ShareLine(ref __result, () => __instance._enter.enabled = false);
    }

    [HarmonyPatch(typeof(global::UI.SystemDialogue), "CWaitInput")]
    public static class DialogueLine
    {
        private static void Postfix(global::UI.SystemDialogue __instance, ref IEnumerator __result) =>
            ShareLine(ref __result, () =>
            {
                __instance._enter.enabled = false;
                __instance.Done();
            });
    }

    [HarmonyPatch(typeof(global::UI.LetterBoxText), "CWaitInput")]
    public static class LetterBoxLine
    {
        private static void Postfix(global::UI.LetterBoxText __instance, ref IEnumerator __result) =>
            ShareLine(ref __result, () =>
            {
                if (__instance.skippable)
                    __instance.Skip();
            });
    }

    [HarmonyPatch(typeof(ACutScene), nameof(ACutScene.Run))]
    public static class OnRun
    {
        private static bool Prefix(ACutScene __instance)
        {
            var scenes = Current;
            if (scenes == null)
                return true;
            try
            {
                return scenes.Starting(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] a scene wasn't shared: " + e.Message);
                return true;
            }
        }
    }

    /// <summary>A scene is about to start on this game; false: not now (a client waits for the host's word).</summary>
    private bool Starting(ACutScene scene)
    {
        if (!Active || _applying || InCastle() || !IsStory(scene))
        {
            NoteTalk(scene, personal: !_applying); // the host's scene played here on its word is a shared one
            if (Active && !_applying && Talks(scene) && IsPersonal(scene))
                CoopTrace.Write("scene-personal", CoopWorld.CurrentRoom(), what: scene.name);
            return true;
        }
        uint room = CoopWorld.CurrentRoom();
        string key = room == 0 ? null : KeyOf(scene, Map.Instance);
        if (key == null)
        {
            NoteTalk(scene, personal: true);
            CoopTrace.Write("scene-own", room, what: scene.name);
            return true;
        }
        string id = room.ToString("x8") + ":" + key;
        if (_started.Count > 200)
            _started.Clear();
        if (_session.IsHost)
        {
            if (Twice(id, Time.unscaledTime))
            {
                CoopTrace.Write("scene-twice", room, what: key);
                return false;
            }
            Started(id, Time.unscaledTime);
            _started.Add(id);
            BeginTalk(room, key);
            NoteTalk(scene, personal: false);
            Write(room, key, Start);
            _session.Broadcast(MessageType.CutScene, _writer, Delivery.Reliable);
            CoopTrace.Write("scene-sent", room, what: key);
            return true;
        }
        if (_started.Contains(id))
        {
            CoopTrace.Write("scene-skipped", room, what: key); // played already, on the host's word
            return false;
        }
        if (_asked.Count > 200)
            _asked.Clear();
        if (_asked.Add(id))
        {
            Write(room, key, Ask);
            _session.Send(0, MessageType.CutScene, _writer, Delivery.Reliable);
            CoopTrace.Write("scene-asked", room, what: key);
        }
        return false;
    }

    // CutScene: [room u32][u8: 0 the host starts it, 1 a client asks for it, 2 the host to that client: play your
    // own][scene key string]
    private void Write(uint room, string key, byte what)
    {
        _writer.Reset();
        _writer.WriteUInt32(room);
        _writer.WriteByte(what);
        _writer.WriteString(key);
    }

    private void OnMessage(GameMessage message, NetReader payload)
    {
        uint room = payload.ReadUInt32();
        byte what = payload.ReadByte();
        string key = payload.ReadString();
        if (what == Line)
        {
            OnLine(room, key, payload.ReadUInt16(), message.FromSlot);
            return;
        }
        if (what == Ask)
        {
            // a client's player set off a scene: the host starts it for everyone, once
            if (!_session.IsHost)
                return;
            if (_started.Contains(room.ToString("x8") + ":" + key))
            {
                // it began here before that client was in the room (its start was dropped there): again, for it
                Write(room, key, Start);
                _session.Send(message.FromSlot, MessageType.CutScene, _writer, Delivery.Reliable);
                CoopTrace.Write("scene-sent-again", room, what: key, slot: message.FromSlot);
                return;
            }
            var scene = room == CoopWorld.CurrentRoom() ? Find(Map.Instance, key) : null;
            if (scene == null)
            {
                // the host is somewhere else (or has no such scene): that client plays its own
                Write(room, key, YourOwn);
                _session.Send(message.FromSlot, MessageType.CutScene, _writer, Delivery.Reliable);
                CoopTrace.Write("scene-not-found", room, what: key, slot: message.FromSlot);
                return;
            }
            CoopTrace.Write("scene-asked-for", room, what: key, slot: message.FromSlot);
            scene.Run();
            return;
        }
        if (_session.IsHost)
            return;
        if (what == Start && (Twice(room.ToString("x8") + ":" + key, Time.unscaledTime) || _waiting.Exists(w => w.Room == room && w.Key == key)))
        {
            CoopTrace.Write("scene-twice", room, what: key);
            return;
        }
        _waiting.Add((room, key, Time.unscaledTime + 20f));
        Tick();
    }

    /// <summary>A client: starts the host's scenes as soon as this game is in their room.</summary>
    public void Tick()
    {
        for (int i = _waiting.Count - 1; i >= 0; i--)
        {
            var (room, key, until) = _waiting[i];
            if (room != CoopWorld.CurrentRoom() || CoopWorld.Current == null || !CoopWorld.Current.RoomLoaded)
            {
                if (Time.unscaledTime > until)
                {
                    _waiting.RemoveAt(i);
                    CoopTrace.Write("scene-dropped", room, what: key);
                }
                continue;
            }
            _waiting.RemoveAt(i);
            var scene = Find(Map.Instance, key);
            if (scene == null)
            {
                CoopTrace.Write("scene-not-found", room, what: key);
                continue;
            }
            _started.Add(room.ToString("x8") + ":" + key);
            Started(room.ToString("x8") + ":" + key, Time.unscaledTime);
            BeginTalk(room, key);
            _applying = true;
            try
            {
                scene.Run();
                CoopTrace.Write("scene-played", room, what: key);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"[Coop] the host's scene {key} didn't start here: {e.Message}");
                CoopTrace.Write("scene-failed", room, what: key);
            }
            finally
            {
                _applying = false;
            }
        }
    }
}

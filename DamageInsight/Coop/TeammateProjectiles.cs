using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Characters;
using Characters.Projectiles;
using HarmonyLib;
using UnityEngine;

namespace DamageInsight.Coop;

/// <summary>
/// A teammate's projectiles come from its owner's game (COOP_LOG 47). Many skull moves pick their projectile from the
/// player's own state (Davy Jones' cannonball queue, Alchemist's flasks, Clown's swap), which a copy doesn't have, so
/// a copy replaying the move fired the wrong ones or none (random-skull bots, COOP_LOG 46f). Now:
/// - the owner's game sends every projectile its player fires (TeammateEventKind.Projectile: prefab name, offset from
///   the player, the Fire arguments), on the same timeline as the moves;
/// - a copy's own operations fire nothing (their projectile goes back to its pool at once);
/// - the copy's game fires the sent projectile from the copy (its hits do nothing here: CoopCopies).
/// The prefab is found by name among the projectile references of the copy's gear (reflection, once per gear) or
/// among projectiles this game has fired.
/// </summary>
internal static class TeammateProjectiles
{
    /// <summary>This game's player (CoopRuntime.Follow); its projectiles are sent.</summary>
    internal static Character Local;

    /// <summary>Sends a shot of the local player (set by CoopRuntime while a session runs).</summary>
    internal static Action<TeammateEvent> Send;

    private static bool _replaying;

    /// <summary>For the test probe's census (while it runs): the local player's shots sent, a teammate's shots shown
    /// from its copy, and those this game couldn't show (no such projectile), by name since the last census.</summary>
    internal static readonly Dictionary<string, int> Sent = new(StringComparer.Ordinal), ShownFromCopy = new(StringComparer.Ordinal),
        Lost = new(StringComparer.Ordinal);

    private static void Tally(Dictionary<string, int> counts, string name)
    {
        if (!Testing.CoopProbe.On || counts.Count >= 500)
            return;
        counts.TryGetValue(name, out int n);
        counts[name] = n + 1;
    }

    /// <summary>Whether a projectile fired by <paramref name="owner"/> is shown on this game (the probe's census).</summary>
    internal static bool Shown(Character owner) => _replaying || !CoopCopies.BelongsToCopy(owner);

    /// <summary>This game's player, one of its minions (the Recruit's buddy archer, the Alchemist's golem) or its
    /// quintessence's character (<see cref="CoopCopies.PlayerBehind"/>).</summary>
    private static bool IsLocals(Character owner)
    {
        if (Local == null || owner == null)
            return false;
        if (owner == Local)
            return true;
        if (owner.type == Character.Type.Player)
            return false;
        return CoopCopies.PlayerBehind(owner) == Local;
    }

    // ------------------------------------------------------------------ both sides: Projectile.Fire

    private static readonly AccessTools.FieldRef<PoolObject, int> PoolIndex = AccessTools.FieldRefAccess<PoolObject, int>("_poolIndex");

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Fire))]
    public static class OnFire
    {
        private static bool Prefix(Projectile __instance, Character owner, float direction, bool flipX, bool flipY,
            float speedMultiplier, float delay)
        {
            if (_replaying || owner == null || __instance == null)
                return true;
            try
            {
                if (CoopCopies.BelongsToCopy(owner))
                {
                    // The copy's own move (or its minion's): its owner's game sends what it really fired.
                    Remember(__instance);
                    var pooled = __instance.reusable;
                    if (pooled != null && PoolIndex(pooled) >= 0)
                        pooled.Despawn();
                    else
                        __instance.gameObject.SetActive(false);
                    return false;
                }
                if (Send != null && IsLocals(owner))
                {
                    Remember(__instance);
                    // from the player even for a minion's shot: the copy stands where the player does
                    var offset = __instance.transform.position - Local.transform.position;
                    var scale = __instance.transform.localScale; // before Fire flips it
                    string name = Loadout.PrefabName(__instance.name);
                    Send(TeammateEvent.ForProjectile(name, offset.x, offset.y, direction,
                        (flipX ? ShotFlags.FlipX : 0) | (flipY ? ShotFlags.FlipY : 0), speedMultiplier, scale.x, scale.y, delay));
                    Tally(Sent, name);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("[Coop] projectile not handled: " + e.Message);
            }
            return true;
        }
    }

    // ------------------------------------------------------------------ the copy's side

    private static readonly HashSet<string> MissingLogged = new(StringComparer.Ordinal);

    /// <summary>Fires the teammate's shot from its copy; false when this game knows no such projectile.</summary>
    internal static bool Fire(Character copy, TeammateEvent e)
    {
        if (copy == null || e == null || e.ProjectileName.Length == 0)
            return false;
        var prefab = Find(copy, e.ProjectileName);
        if (prefab == null || prefab.reusable == null)
        {
            Tally(Lost, e.ProjectileName);
            if (MissingLogged.Add(e.ProjectileName))
            {
                Plugin.Log.LogInfo("[Coop] teammate projectile not found on this game: " + e.ProjectileName);
                CoopTrace.Write("teammate-shot-missing", CoopWorld.CurrentRoom(), what: e.ProjectileName);
            }
            return false;
        }
        _replaying = true;
        try
        {
            var position = copy.transform.position + new Vector3(e.OffsetX, e.OffsetY, 0f);
            var shot = prefab.reusable.Spawn(position).GetComponent<Projectile>();
            if (shot == null)
                return false;
            shot.transform.localScale = new Vector3(e.ScaleX, e.ScaleY, shot.transform.localScale.z);
            shot.Fire(copy, 0f, e.Direction, (e.Flags & ShotFlags.FlipX) != 0, (e.Flags & ShotFlags.FlipY) != 0,
                e.SpeedMultiplier, null, e.Delay);
            Tally(ShownFromCopy, e.ProjectileName);
            return true;
        }
        catch (Exception ex)
        {
            if (MissingLogged.Add("!" + e.ProjectileName))
                Plugin.Log.LogWarning($"[Coop] teammate projectile {e.ProjectileName} not fired: {ex.Message}");
            return false;
        }
        finally
        {
            _replaying = false;
        }
    }

    // ------------------------------------------------------------------ finding a projectile by name

    /// <summary>Projectiles by prefab name: fired on this game, or referenced by a copy's gear.</summary>
    private static readonly Dictionary<string, Projectile> Known = new(StringComparer.Ordinal);
    /// <summary>When each copy's gear was last searched, and the gear it had then.</summary>
    private static readonly Dictionary<Character, string> Scanned = new();

    private static void Remember(Projectile projectile)
    {
        string name = Loadout.PrefabName(projectile.name);
        if (name.Length > 0 && (!Known.TryGetValue(name, out var known) || known == null))
            Known[name] = projectile;
    }

    private static Projectile Find(Character copy, string name)
    {
        ScanLoadedGear();
        if (Known.TryGetValue(name, out var known))
        {
            if (known != null)
                return known;
            Known.Remove(name); // destroyed (its pool was released)
        }
        string gear = GearKey(copy);
        if (!Scanned.TryGetValue(copy, out string scannedGear) || scannedGear != gear)
        {
            if (Scanned.Count > 8)
                Scanned.Clear(); // copies long gone
            Scanned[copy] = gear;
            ScanCopy(copy); // its first part now, the rest over the next frames (Tick)
        }
        else
            ContinueSearches(MissBudgetMs); // a search still running may reach it now
        if (Known.TryGetValue(name, out known) && known != null)
            return known;
        return FromMemory(name);
    }

    /// <summary>When each name was last looked up among all loaded projectiles (at most every 20 s each).</summary>
    private static readonly Dictionary<string, float> MemoryTried = new(StringComparer.Ordinal);

    /// <summary>The last resort for a name no search reached (a prefab behind a reference the search doesn't open):
    /// every projectile this game has in memory, prefab or pooled shot. The skull sweep's misses (Genie_3's fairy
    /// lamp, High Warlock's basic attack, the buddy archer's swap shots, Thief's gold explosion, NIGHT_LOG
    /// 2026-11-16 step 6) came with searches that stopped at their old limit of 300 prefabs.</summary>
    private static Projectile FromMemory(string name)
    {
        float now = Time.realtimeSinceStartup;
        if (MemoryTried.TryGetValue(name, out float last) && now - last < 20f)
            return null;
        if (MemoryTried.Count > 200)
            MemoryTried.Clear();
        MemoryTried[name] = now;
        Projectile found = null;
        foreach (var p in Resources.FindObjectsOfTypeAll<Projectile>())
        {
            if (p == null || p.reusable == null || Loadout.PrefabName(p.name) != name)
                continue;
            found = p;
            if (!p.gameObject.scene.IsValid())
                break; // the prefab itself is best (a pooled shot may be in flight)
        }
        if (found != null)
        {
            Known[name] = found;
            Plugin.Log.LogInfo("[Coop] teammate projectile found in memory: " + name);
            CoopTrace.Write("teammate-shot-memory", CoopWorld.CurrentRoom(), what: name);
        }
        return found;
    }

    private static string GearKey(Character copy)
    {
        var weapons = copy.playerComponents?.inventory?.weapon?.weapons;
        if (weapons == null)
            return "";
        var key = new System.Text.StringBuilder();
        foreach (var weapon in weapons)
            key.Append(weapon != null ? weapon.GetInstanceID() : 0).Append(',');
        return key.ToString();
    }

    // ------------------------------------------------------------------ the teammate's items and quintessence

    /// <summary>The teammate's items and quintessence by name, loaded on this game (the request is kept: its prefab
    /// stays loaded) and searched once loaded. A copy carries only the teammate's skulls, so an item's shots (the
    /// plates of the skull sweep, COOP_LOG 58g: "no such projectile on the other game: PlateProjectile") were
    /// unknown here.</summary>
    private static readonly Dictionary<string, UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationHandle<GameObject>> GearLoads =
        new(StringComparer.Ordinal);
    private static readonly List<object> GearRequests = new();
    private static readonly HashSet<string> GearScanned = new(StringComparer.Ordinal);

    /// <summary>A teammate's item or quintessence prefab by name once it has loaded here (TeammateEssences), else null.</summary>
    internal static GameObject LoadedGear(string name) =>
        name != null && GearLoads.TryGetValue(name, out var handle) && handle.IsDone
        && handle.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded ? handle.Result : null;

    /// <summary>A teammate's loadout arrived (CoopRuntime): its items and quintessence start loading here.</summary>
    internal static void LoadGear(Loadout loadout)
    {
        var resource = GameResources.GearResource.instance;
        if (loadout == null || resource == null)
            return;
        foreach (string name in loadout.Items ?? Array.Empty<string>())
            if (!string.IsNullOrEmpty(name) && !GearLoads.ContainsKey(name))
            {
                var request = FindReference(resource.items, name)?.LoadAsync();
                if (request != null)
                {
                    GearLoads[name] = request.handle;
                    GearRequests.Add(request);
                }
            }
        if (!string.IsNullOrEmpty(loadout.Essence) && !GearLoads.ContainsKey(loadout.Essence))
        {
            var request = FindReference(resource.essences, loadout.Essence)?.LoadAsync();
            if (request != null)
            {
                GearLoads[loadout.Essence] = request.handle;
                GearRequests.Add(request);
            }
        }
        ScanLoadedGear();
    }

    /// <summary>
    /// An item's two inscriptions: their shots come from the inscription's own prefab (the burn inscription Arson's
    /// explosion), which the game loads only when the inscription is active for its player, never for a copy (the
    /// item sweep, COOP_LOG 58i: "no such projectile on the other game: Arson"). Loaded here by the same address
    /// the game uses (this game's player's synergy settings), without touching the game's own AssetReference.
    /// </summary>
    private static void LoadInscriptions(Characters.Gear.Items.Item item)
    {
        var settings = Singletons.Singleton<Services.Service>.Instance?.levelManager?.player?.playerComponents?.inventory?.synergy?._synergySettings?.settings;
        if (item == null || settings == null)
            return;
        foreach (var key in new[] { item.keyword1, item.keyword2 })
        {
            string name = "inscription " + key;
            if (GearLoads.ContainsKey(name))
                continue;
            var reference = settings[key]?.reference;
            if (reference == null || !reference.RuntimeKeyIsValid())
                continue;
            GearLoads[name] = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<GameObject>(reference.RuntimeKey);
        }
    }

    private static T FindReference<T>(IEnumerable<T> references, string name) where T : GameResources.GearReference
    {
        if (references == null)
            return null;
        foreach (var reference in references)
            if (reference != null && reference.name == name)
                return reference;
        return null;
    }

    /// <summary>Searches every teammate gear prefab that finished loading (once each).</summary>
    private static void ScanLoadedGear()
    {
        if (GearLoads.Count == GearScanned.Count)
            return;
        foreach (var pair in GearLoads)
        {
            if (GearScanned.Contains(pair.Key) || !pair.Value.IsDone)
                continue;
            GearScanned.Add(pair.Key);
            var prefab = pair.Value.Status == UnityEngine.ResourceManagement.AsyncOperations.AsyncOperationStatus.Succeeded ? pair.Value.Result : null;
            if (prefab != null)
            {
                Scan(prefab, "teammate's " + pair.Key);
                LoadInscriptions(prefab.GetComponent<Characters.Gear.Items.Item>());
            }
            break; // one per call: a search can take a few milliseconds
        }
    }

    /// <summary>Prefabs and data assets one search reads at most, and scripts at most. Until 2026-11-16 a search
    /// stopped at 300 prefabs, in one frame, deepest branch first: the sweepall run's searches stopped 26 times with
    /// 40-174 prefabs left, and the shots behind them were "not found" (NIGHT_LOG 2026-11-16 step 6). Now a search
    /// goes nearest first, works <see cref="FirstBudgetMs"/> at once and <see cref="FrameBudgetMs"/> a frame after.</summary>
    private const int MaxRoots = 6000, MaxParts = 400000;
    private const double FirstBudgetMs = 25, MissBudgetMs = 8, FrameBudgetMs = 3;

    private sealed class Search
    {
        public readonly string Label;
        public readonly HashSet<UnityEngine.Object> Visited = new();
        public readonly Queue<UnityEngine.Object> Pending = new();
        public readonly int Before = Known.Count;
        public int Roots, Parts, Frames;
        public double Ms;

        public Search(GameObject start, string label)
        {
            Label = label;
            Pending.Enqueue(start);
        }
    }

    private static readonly List<Search> Running = new();
    private static readonly System.Diagnostics.Stopwatch Watch = new();

    /// <summary>Once a frame while a session runs (CoopRuntime): the gear that finished loading, and a few
    /// milliseconds of the searches still running.</summary>
    internal static void Tick()
    {
        ScanLoadedGear();
        if (Running.Count > 0)
            ContinueSearches(FrameBudgetMs);
    }

    /// <summary>Every projectile prefab the copy's parts refer to: fields of operations and of their serialized
    /// helpers (Davy Jones' projectile per cannonball), the projectiles those projectiles fire, and the prefabs
    /// they spawn (a cannonball's hit summons an operation runner, which fires the Bombard volley: COOP_LOG 47k;
    /// a minion's prefab and its shots), and the data assets (ScriptableObjects) on the way.</summary>
    private static void ScanCopy(Character copy) => Scan(copy.gameObject, "teammate copy's");

    private static void Scan(GameObject start, string label)
    {
        if (start == null)
            return;
        var search = new Search(start, label);
        Running.Add(search);
        if (Step(search, FirstBudgetMs))
            Running.Remove(search);
    }

    private static void ContinueSearches(double budgetMs)
    {
        Watch.Restart();
        while (Running.Count > 0)
        {
            double left = budgetMs - Watch.Elapsed.TotalMilliseconds;
            if (left <= 0)
                break;
            if (Step(Running[0], left))
                Running.RemoveAt(0);
            else
                break;
        }
    }

    /// <summary>Works on a search for about <paramref name="budgetMs"/>; true when it is over (it logs one line).</summary>
    private static bool Step(Search s, double budgetMs)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        s.Frames++;
        while (s.Pending.Count > 0 && s.Roots < MaxRoots && s.Parts < MaxParts && clock.Elapsed.TotalMilliseconds < budgetMs)
        {
            var root = s.Pending.Dequeue();
            if (root == null || !s.Visited.Add(root))
                continue;
            s.Roots++;
            if (root is ScriptableObject data)
            {
                s.Parts++;
                Follow(s, data);
                continue;
            }
            if (root is not GameObject gameObject)
                continue;
            foreach (var component in gameObject.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null)
                    continue;
                s.Parts++;
                s.Visited.Add(component.gameObject); // a later reference into this hierarchy is searched already
                Follow(s, component);
            }
        }
        s.Ms += clock.Elapsed.TotalMilliseconds;
        bool over = s.Pending.Count == 0 || s.Roots >= MaxRoots || s.Parts >= MaxParts;
        if (over)
            Plugin.Log.LogInfo($"[Coop] {s.Label} projectiles: {Known.Count - s.Before} new, {Known.Count} known "
                + $"({s.Roots} prefabs, {s.Parts} scripts, {s.Ms:0} ms in {s.Frames} frame{(s.Frames == 1 ? "" : "s")}"
                + (s.Pending.Count > 0 ? $", stopped with {s.Pending.Count} left)" : ")"));
        return over;
    }

    private static void Follow(Search s, object holder)
    {
        foreach (var found in ReferencesIn(holder, 0))
        {
            UnityEngine.Object target;
            if (found is Projectile projectile)
            {
                Remember(projectile);
                target = projectile.gameObject;
            }
            else if (found is ScriptableObject data)
                target = data; // an asset: never part of a scene
            else
            {
                var gameObject = found is GameObject g ? g : ((Component)found).gameObject;
                // Only prefabs (assets, in no scene): what is in a scene is the copy's own part (searched already)
                // or belongs to someone else.
                if (gameObject == null || gameObject.scene.IsValid())
                    continue;
                target = gameObject;
            }
            if (!s.Visited.Contains(target))
                s.Pending.Enqueue(target);
        }
    }

    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Dictionary<Type, FieldInfo[]> RelevantFields = new();

    /// <summary>What a search follows: projectiles, and objects or scripts that may be a prefab (built-in parts
    /// like a Transform or a collider point into their own hierarchy).</summary>
    internal static bool Followed(Type type) => type == typeof(GameObject) || typeof(MonoBehaviour).IsAssignableFrom(type)
        || typeof(ScriptableObject).IsAssignableFrom(type);

    private static bool Alive(object item) => item is GameObject or MonoBehaviour or ScriptableObject && (UnityEngine.Object)item != null;

    /// <summary>The projectiles, objects and scripts a value's fields point to (lists and serialized helper classes
    /// are opened, up to four deep).</summary>
    private static IEnumerable<UnityEngine.Object> ReferencesIn(object value, int depth)
    {
        if (value == null || depth > 4)
            yield break;
        foreach (var field in FieldsOf(value.GetType()))
        {
            object content;
            try
            {
                content = field.GetValue(value);
            }
            catch
            {
                continue;
            }
            switch (content)
            {
                case null:
                    continue;
                case UnityEngine.Object reference:
                    if (Alive(reference))
                        yield return reference;
                    continue;
                case IEnumerable list when content is not string:
                    int n = 0;
                    foreach (var item in list)
                    {
                        if (++n > 64)
                            break;
                        if (item is UnityEngine.Object listed)
                        {
                            if (Alive(listed))
                                yield return listed;
                        }
                        else if (item != null)
                            foreach (var inner in ReferencesIn(item, depth + 1))
                                yield return inner;
                    }
                    continue;
                default:
                    foreach (var inner in ReferencesIn(content, depth + 1))
                        yield return inner;
                    continue;
            }
        }
    }

    /// <summary>The fields of a type (and its bases below MonoBehaviour) that can lead to a projectile: a projectile,
    /// an object or script (a prefab), a list of those or of helpers, or a serialized helper class or struct.</summary>
    internal static FieldInfo[] FieldsOf(Type type)
    {
        if (RelevantFields.TryGetValue(type, out var known))
            return known;
        var fields = new List<FieldInfo>();
        for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(ScriptableObject) && t != typeof(object); t = t.BaseType)
        {
            foreach (var field in t.GetFields(Fields))
            {
                var f = field.FieldType;
                if (field.IsLiteral || f.IsPrimitive || f.IsEnum || f == typeof(string) || typeof(Delegate).IsAssignableFrom(f)
                    || f.IsPointer || f == typeof(Vector2) || f == typeof(Vector3) || f == typeof(Color) || f == typeof(Quaternion))
                    continue;
                if (Followed(f) || (f.IsArray && f.GetElementType() is { } element && Opened(element)))
                {
                    fields.Add(field);
                    continue;
                }
                if (typeof(UnityEngine.Object).IsAssignableFrom(f))
                    continue;
                if (f.IsGenericType && typeof(IEnumerable).IsAssignableFrom(f))
                {
                    var args = f.GetGenericArguments();
                    if (args.Length == 1 && Opened(args[0]))
                        fields.Add(field);
                    continue;
                }
                if (f.IsSerializable || f.IsValueType)
                    fields.Add(field);
            }
        }
        return RelevantFields[type] = fields.ToArray();
    }

    /// <summary>Whether a list of this element type is read: followed references, or helpers that may hold some.</summary>
    private static bool Opened(Type element) =>
        Followed(element) || !typeof(UnityEngine.Object).IsAssignableFrom(element)
        && !element.IsPrimitive && !element.IsEnum && element != typeof(string);

    // ------------------------------------------------------------------ Davy Jones on a copy

    /// <summary>A copy's cannonball shot: its queue is empty (the shot's kind is the owner's), so the game's Run threw
    /// (7 x per room, COOP_LOG 46f). The owner's game sends the shot itself.</summary>
    [HarmonyPatch(typeof(Characters.Operations.Customs.DavyJones.FireCannonBall), nameof(Characters.Operations.Customs.DavyJones.FireCannonBall.Run), new[] { typeof(Character) })]
    public static class NoCopyCannonBall
    {
        private static bool Prefix(Character owner) => !CoopCopies.IsCopy(owner);
    }

    /// <summary>A copy's Davy Jones passive drew its cannonballs on THIS game's HUD (and hid the player's own when the
    /// copy swapped away). Without an owner yet (the skull was just loaded) nothing is drawn either: the HUD is drawn
    /// again when the passive attaches to its player.</summary>
    [HarmonyPatch(typeof(Characters.Abilities.Weapons.DavyJones.DavyJonesPassive), "UpdateHUD")]
    public static class NoCopyCannonHud
    {
        private static bool Prefix(Characters.Abilities.Weapons.DavyJones.DavyJonesPassive __instance) =>
            CoopCopies.Count == 0 || (__instance._owner != null && !CoopCopies.IsCopy(__instance._owner));
    }

    [HarmonyPatch(typeof(Characters.Abilities.Weapons.DavyJones.DavyJonesPassive.Instance), "OnAttach")]
    public static class NoCopyCannonHudShow
    {
        private static bool Prefix(Characters.Abilities.Weapons.DavyJones.DavyJonesPassive.Instance __instance) =>
            !CoopCopies.IsCopy(__instance.owner);
    }

    [HarmonyPatch(typeof(Characters.Abilities.Weapons.DavyJones.DavyJonesPassive.Instance), "OnDetach")]
    public static class NoCopyCannonHudHide
    {
        private static bool Prefix(Characters.Abilities.Weapons.DavyJones.DavyJonesPassive.Instance __instance) =>
            !CoopCopies.IsCopy(__instance.owner);
    }
}

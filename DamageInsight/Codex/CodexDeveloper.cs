using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DamageInsight.Recording;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace DamageInsight.Codex;

/// <summary>
/// Turns noted animations into picture sheets without ever blocking the game:
/// 1. Game thread, at most a millisecond or two per frame: each frame of each animation becomes a list of
///    textured triangles (sprites from their mesh, Spine bosses from posed skeleton copies), and the texture areas
///    those triangles use are requested from the GPU asynchronously (AsyncGPUReadback: no waiting).
/// 2. Background thread: all drawing (CaptureRenderer), PNG encoding and file writing.
/// Runs all the time, during play too; a big boss takes some seconds in the background.
/// </summary>
public static class CodexDeveloper
{
    /// <summary>What a job starts from: sprite frames noted on the kill, or Spine skeletons.</summary>
    public sealed class Request
    {
        public string Key = "";
        public List<(string label, List<(Sprite sprite, float duration)> frames)> SpriteClips = new();
        public List<SpineCapture.Part> Spine;

        /// <summary>Spine moves as the AI makes them (move → its animations); null groups by animation name.</summary>
        public List<(string label, List<string> parts)> SpineGroups;

        /// <summary>Adds these sets to the enemy's saved ones (its projectiles); same-named sets are replaced.</summary>
        public bool Append;
    }

    private sealed class Job
    {
        public Request Request;
        public readonly List<CapClip> Clips = new();
        public readonly List<PixelRegion> Regions = new();
        public readonly Dictionary<(int texture, int x, int y, int w, int h), int> RegionIndex = new();
        public readonly Dictionary<Texture2D, RenderTexture> Targets = new();
        public IEnumerator Geometry;
        public int PendingReadbacks;
        public Task<string> Finishing;
        public DateTime Started = DateTime.Now;
    }

    private static readonly Queue<Request> Queue = new();
    private static Job _job;

    public static int Waiting => Queue.Count + (_job != null ? 1 : 0);

    public static void Enqueue(Request request) => Queue.Enqueue(request);

    /// <summary>Raised on the game thread when an enemy's pictures were saved (key).</summary>
    public static event Action<string> Developed;

    /// <summary>Called every frame: does at most <paramref name="budgetMs"/> of game-thread work.</summary>
    public static void Pump(double budgetMs)
    {
        if (_job == null)
        {
            if (Queue.Count == 0)
                return;
            _job = new Job { Request = Queue.Dequeue() };
            _job.Geometry = _job.Request.Spine != null ? SpineGeometry(_job) : SpriteGeometry(_job);
        }
        var job = _job;
        try
        {
            if (job.Geometry != null)
            {
                var clock = System.Diagnostics.Stopwatch.StartNew();
                while (clock.Elapsed.TotalMilliseconds < budgetMs)
                {
                    if (!job.Geometry.MoveNext())
                    {
                        job.Geometry = null;
                        break;
                    }
                }
                return;
            }
            if (job.PendingReadbacks > 0)
                return; // the GPU is still sending texture areas
            if (job.Finishing == null)
            {
                foreach (var rt in job.Targets.Values)
                    RenderTexture.ReleaseTemporary(rt);
                job.Targets.Clear();
                string folder = CodexAnimations.FolderOf(job.Request.Key);
                job.Finishing = Task.Run(() => Finish(job, folder));
                return;
            }
            if (!job.Finishing.IsCompleted)
                return;
            string result = job.Finishing.IsFaulted ? "failed: " + job.Finishing.Exception?.GetBaseException().Message : job.Finishing.Result;
            Plugin.Log.LogInfo($"Codex: {job.Request.Key}: {result} ({(DateTime.Now - job.Started).TotalSeconds:0.0} s in the background).");
            _job = null;
            CodexAnimations.ForgetPortrait(job.Request.Key);
            Developed?.Invoke(job.Request.Key);
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Codex: developing {job.Request.Key} failed: {e}");
            foreach (var rt in job.Targets.Values)
                RenderTexture.ReleaseTemporary(rt);
            _job = null;
        }
    }

    // ---------------------------------------------------------------- game thread: triangles and texture areas

    /// <summary>Sprite animations: every frame is its sprite's mesh (pixels around the pivot = feet).</summary>
    private static IEnumerator SpriteGeometry(Job job)
    {
        foreach (var (label, frames) in job.Request.SpriteClips)
        {
            var clip = new CapClip { Label = label };
            foreach (var (sprite, duration) in frames)
            {
                if (sprite == null || sprite.texture == null)
                    continue; // unloaded since the kill
                var texture = sprite.texture;
                var vertices = sprite.vertices;
                var uv = sprite.uv;
                var triangles = sprite.triangles;
                float ppu = sprite.pixelsPerUnit;
                int region = RegionFor(job, texture, uv);
                var tris = new List<CapTri>(triangles.Length / 3);
                for (int t = 0; t + 2 < triangles.Length; t += 3)
                {
                    int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
                    tris.Add(new CapTri
                    {
                        A = vertices[a] * ppu, B = vertices[b] * ppu, C = vertices[c] * ppu,
                        UA = uv[a], UB = uv[b], UC = uv[c],
                        Region = region, Tint = Color.white,
                    });
                }
                clip.Frames.Add(tris);
                clip.Durations.Add(duration);
                yield return null;
            }
            if (clip.Frames.Count > 0)
                job.Clips.Add(clip);
        }
    }

    /// <summary>Spine figures: attacks grouped (intro + main + outro), each moment posed on skeleton copies.</summary>
    private static IEnumerator SpineGeometry(Job job)
    {
        var parts = job.Request.Spine;
        var skeletons = SpineCapture.CreateSkeletons(parts);
        float scale = SpineCapture.ScaleFor(SpineCapture.Pose(skeletons, null, 0f));
        foreach (var (label, names) in job.Request.SpineGroups ?? AnimationGrouping.Group(SpineCapture.AnimationNames(parts)))
        {
            var clip = new CapClip { Label = label };
            foreach (var name in names)
            {
                float duration = SpineCapture.Duration(parts, name);
                int count = Mathf.Clamp(Mathf.CeilToInt(duration * SpineCapture.Fps), 1, SpineCapture.MaxFramesPerAnimation);
                for (int i = 0; i < count; i++)
                {
                    var raw = SpineCapture.Pose(skeletons, name, i / SpineCapture.Fps);
                    var tris = new List<CapTri>(raw.Count);
                    var regionOf = new Dictionary<int, int>();
                    foreach (var r in raw)
                    {
                        if (!regionOf.TryGetValue(r.Attachment, out int region))
                            regionOf[r.Attachment] = region = RegionFor(job, r.Texture, AttachmentUvs(raw, r.Attachment));
                        tris.Add(new CapTri
                        {
                            A = r.A * scale, B = r.B * scale, C = r.C * scale, UA = r.UA, UB = r.UB, UC = r.UC,
                            Region = region, Tint = r.Tint, Additive = r.Additive, Premultiplied = r.Premultiplied,
                        });
                    }
                    clip.Frames.Add(tris);
                    clip.Durations.Add(1f / SpineCapture.Fps);
                    yield return null;
                }
            }
            if (clip.Frames.Count > 0)
                job.Clips.Add(clip);
        }
    }

    private static IEnumerable<Vector2> AttachmentUvs(List<SpineCapture.RawTri> raw, int attachment)
    {
        foreach (var t in raw)
            if (t.Attachment == attachment)
            {
                yield return t.UA;
                yield return t.UB;
                yield return t.UC;
            }
    }

    /// <summary>The pixel region covering <paramref name="uvs"/> in the texture; requested from the GPU once.</summary>
    private static int RegionFor(Job job, Texture2D texture, IEnumerable<Vector2> uvs)
    {
        float minU = 1, minV = 1, maxU = 0, maxV = 0;
        foreach (var p in uvs)
        {
            minU = Mathf.Min(minU, p.x); maxU = Mathf.Max(maxU, p.x);
            minV = Mathf.Min(minV, p.y); maxV = Mathf.Max(maxV, p.y);
        }
        int x0 = Mathf.Clamp(Mathf.FloorToInt(minU * texture.width) - 1, 0, texture.width - 1);
        int y0 = Mathf.Clamp(Mathf.FloorToInt(minV * texture.height) - 1, 0, texture.height - 1);
        int x1 = Mathf.Clamp(Mathf.CeilToInt(maxU * texture.width) + 1, x0 + 1, texture.width);
        int y1 = Mathf.Clamp(Mathf.CeilToInt(maxV * texture.height) + 1, y0 + 1, texture.height);
        var key = (texture.GetInstanceID(), x0, y0, x1 - x0, y1 - y0);
        if (job.RegionIndex.TryGetValue(key, out int index))
            return index;

        var region = new PixelRegion { X = x0, Y = y0, W = x1 - x0, H = y1 - y0, TextureW = texture.width, TextureH = texture.height };
        index = job.Regions.Count;
        job.Regions.Add(region);
        job.RegionIndex[key] = index;

        if (!job.Targets.TryGetValue(texture, out var rt))
            job.Targets[texture] = rt = SpriteCopy.ToRenderTexture(texture);
        job.PendingReadbacks++;
        AsyncGPUReadback.Request(rt, 0, region.X, region.W, region.Y, region.H, 0, 1, TextureFormat.RGBA32, request =>
        {
            job.PendingReadbacks--;
            if (!request.hasError)
                region.Pixels = request.GetData<Color32>().ToArray();
        });
        return index;
    }

    // ---------------------------------------------------------------- background thread: drawing and saving

    private static readonly Color32 Ink = new(0x4A, 0x33, 0x24, 0xFF);

    /// <summary>Transparent pixels next to the figure become ink, everything else transparent.</summary>
    public static Color32[] Outline(Color32[] pixels, int w, int h)
    {
        var result = new Color32[pixels.Length];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (pixels[y * w + x].a > 0)
                    continue;
                bool edge = (x > 0 && pixels[y * w + x - 1].a > 0) || (x < w - 1 && pixels[y * w + x + 1].a > 0) ||
                            (y > 0 && pixels[(y - 1) * w + x].a > 0) || (y < h - 1 && pixels[(y + 1) * w + x].a > 0);
                if (edge)
                    result[y * w + x] = Ink;
            }
        return result;
    }

    private static string Finish(Job job, string folder)
    {
        var sheets = new List<(CapClip clip, SheetData sheet)>();
        foreach (var clip in job.Clips)
        {
            var sheet = CaptureRenderer.Render(clip, job.Regions);
            if (sheet != null)
                sheets.Add((clip, sheet));
        }
        if (sheets.Count == 0)
            return "nothing visible, not saved";
        if (job.Request.Append)
            return Append(sheets, folder);
        // The idle animation first: it is the still picture and what the book shows when an entry opens.
        sheets = sheets.OrderBy(s => s.clip.Label.StartsWith("Idle", StringComparison.OrdinalIgnoreCase) ? 0 : 1).ToList();

        Directory.CreateDirectory(folder);
        foreach (var old in Directory.GetFiles(folder, "clip_*.png"))
            File.Delete(old);

        // Small still pictures for the list and the first discovery stages: the idle's first frame and its outline.
        var first = sheets[0].sheet;
        var still = new Color32[first.CellWidth * first.CellHeight];
        for (int y = 0; y < first.CellHeight; y++)
            Array.Copy(first.Pixels, (first.Height - first.CellHeight + y) * first.Width, still, y * first.CellWidth, first.CellWidth);
        File.WriteAllBytes(Path.Combine(folder, "portrait.png"),
            ImageConversion.EncodeArrayToPNG(still, GraphicsFormat.R8G8B8A8_UNorm, (uint)first.CellWidth, (uint)first.CellHeight));
        File.WriteAllBytes(Path.Combine(folder, "outline.png"),
            ImageConversion.EncodeArrayToPNG(Outline(still, first.CellWidth, first.CellHeight), GraphicsFormat.R8G8B8A8_UNorm, (uint)first.CellWidth, (uint)first.CellHeight));
        var entries = new List<string>();
        for (int i = 0; i < sheets.Count; i++)
            entries.Add(Save(folder, $"clip_{i}.png", sheets[i].clip, sheets[i].sheet));
        File.WriteAllText(Path.Combine(folder, "animations.json"), "{\"clips\":[" + string.Join(",", entries) + "]}", new UTF8Encoding(false));
        return $"developed {sheets.Count} animations ({string.Join(", ", sheets.Select(s => $"{s.clip.Label} {s.sheet.Count}"))})";
    }

    /// <summary>
    /// Adds sets to an enemy's saved animations (its projectiles, met after its capture); a set of the same name is
    /// replaced, every other one and the still pictures stay.
    /// </summary>
    private static string Append(List<(CapClip clip, SheetData sheet)> sheets, string folder)
    {
        string index = Path.Combine(folder, "animations.json");
        if (!File.Exists(index))
            return "no saved animations to add to";
        var labels = new HashSet<string>(sheets.Select(s => s.clip.Label));
        var entries = new List<string>();
        foreach (var node in Describe.GearDoc.ParseNode(File.ReadAllText(index, Encoding.UTF8)).List("clips"))
        {
            string label = node.Str("label") ?? "", file = node.Str("file") ?? "";
            if (labels.Contains(label))
            {
                if (File.Exists(Path.Combine(folder, file)))
                    File.Delete(Path.Combine(folder, file));
                continue;
            }
            entries.Add(Entry(label, file, (int)node.Num("cellW"), (int)node.Num("cellH"), (int)node.Num("cols"), (int)node.Num("count"),
                node.Numbers("durations").Select(d => (float)d)));
        }
        long stamp = DateTime.Now.Ticks;
        for (int i = 0; i < sheets.Count; i++)
            entries.Add(Save(folder, $"clip_{stamp}_{i}.png", sheets[i].clip, sheets[i].sheet));
        File.WriteAllText(index, "{\"clips\":[" + string.Join(",", entries) + "]}", new UTF8Encoding(false));
        return $"added {string.Join(", ", sheets.Select(s => $"{s.clip.Label} {s.sheet.Count}"))}";
    }

    /// <summary>Writes one sheet and returns its index entry.</summary>
    private static string Save(string folder, string file, CapClip clip, SheetData sheet)
    {
        var png = ImageConversion.EncodeArrayToPNG(sheet.Pixels, GraphicsFormat.R8G8B8A8_UNorm, (uint)sheet.Width, (uint)sheet.Height);
        File.WriteAllBytes(Path.Combine(folder, file), png);
        return Entry(clip.Label, file, sheet.CellWidth, sheet.CellHeight, sheet.Columns, sheet.Count, clip.Durations.Take(sheet.Count));
    }

    private static string Entry(string label, string file, int w, int h, int cols, int count, IEnumerable<float> durations) =>
        "{\"label\":" + LogJson.Quote(label) + ",\"file\":" + LogJson.Quote(file) +
        $",\"cellW\":{w},\"cellH\":{h},\"cols\":{cols},\"count\":{count},\"durations\":[" +
        string.Join(",", durations.Select(d => d.ToString("0.###", CultureInfo.InvariantCulture))) + "]}";
}

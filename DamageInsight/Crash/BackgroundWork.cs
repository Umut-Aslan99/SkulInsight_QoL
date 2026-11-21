using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace DamageInsight.Crash;

/// <summary>
/// Background jobs that call Unity (the Codex films' and animations' image encoders) must not run while the game
/// closes: Unity's native side goes away and the call crashes the game with Unity's crash window. Named with Unity's
/// symbols on 2026-11-16 (COOP_LOG 59, T.md Open 13): the "crash while closing" dumps of the co-op test copies were a
/// thread-pool thread in `ImageConversion.EncodeArrayToPNG` (the Codex's FightRecorder saving a boss film). So: jobs are
/// tracked, the game's quitting waits for them (at most <see cref="QuitWaitMs"/>), and a job that has not started its
/// encoding yet checks <see cref="Quitting"/> and stops.
/// </summary>
internal static class BackgroundWork
{
    public const int QuitWaitMs = 3000;

    private static readonly List<Task> Running = new();
    private static volatile bool _quitting;
    private static bool _hooked;

    /// <summary>True once the game started closing: a job must not start Unity work any more.</summary>
    public static bool Quitting => _quitting;

    /// <summary>Tracks a job (call on the game's thread); returns it for chaining.</summary>
    public static T Track<T>(T task) where T : Task
    {
        if (!_hooked)
        {
            _hooked = true;
            Application.quitting += WaitAtQuit;
        }
        lock (Running)
        {
            Running.RemoveAll(t => t.IsCompleted);
            Running.Add(task);
        }
        return task;
    }

    private static void WaitAtQuit()
    {
        _quitting = true;
        Task[] tasks;
        lock (Running)
        {
            Running.RemoveAll(t => t.IsCompleted);
            tasks = Running.ToArray();
        }
        if (tasks.Length == 0)
            return;
        try
        {
            Task.WaitAll(tasks, QuitWaitMs);
        }
        catch (Exception)
        {
            // a failed job is the job's own business (its continuation logs it)
        }
    }
}

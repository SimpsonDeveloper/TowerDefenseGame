using System.Collections.Generic;
using Godot;

namespace towerdefensegame.scripts.world.enemies;

/// <summary>
/// Off-thread executor for <see cref="EnemyApproachResolver"/>. One pending job
/// per enemy (keyed by Godot instance ID); each job runs on Godot's
/// <see cref="WorkerThreadPool"/> and exposes its result for the enemy to drain
/// on a later physics tick.
///
/// Lifecycle:
///   • Caller (main thread) builds a candidate snapshot, calls <see cref="Submit"/>.
///   • Worker thread runs <see cref="EnemyApproachResolver.Resolve"/> using the
///     snapshot — no scene-graph access.
///   • Caller polls <see cref="TryConsume"/> each physics tick; on completion
///     the result is delivered and the slot freed for the next submit.
///   • On enemy destruction the caller invokes <see cref="Cancel"/> to block on
///     the in-flight task before its memory is freed.
///
/// Autoloaded — see project.godot. Access via <see cref="Instance"/>.
/// </summary>
public partial class EnemyPathfindService : Node
{
    public static EnemyPathfindService Instance { get; private set; }

    /// <summary>
    /// Maximum number of successful drains via <see cref="TryConsume"/> per
    /// physics frame, summed across all enemies. Caps frame-time spikes when
    /// many results land simultaneously; over-budget enemies retry next frame.
    /// </summary>
    [Export] public int MaxDrainsPerPhysicsFrame { get; set; } = 32;

    private sealed class Job
    {
        public ulong SubmittedAtMsec;
        public Vector2 EnemyPos;
        public Rid NavMap;
        public float Standoff;
        public List<ApproachCandidate> Candidates;
        public PocketReachabilityIndex.Snapshot? Probe;
        public ApproachResult Result;
        public long TaskId;

        public void Run()
        {
            Result = EnemyApproachResolver.Resolve(EnemyPos, NavMap, Standoff, Candidates, Probe);
        }
    }

    private readonly Dictionary<ulong, Job> _jobs = new();
    private ulong _lastDrainFrame;
    private int _drainsThisFrame;

    public override void _EnterTree()
    {
        Instance = this;
    }

    public override void _ExitTree()
    {
        // Block on every in-flight task so its captured Job (and thus the
        // candidate list it reads) outlives the worker.
        foreach (Job job in _jobs.Values)
            WorkerThreadPool.WaitForTaskCompletion(job.TaskId);
        _jobs.Clear();
        if (Instance == this) Instance = null;
    }

    /// <summary>True while a job submitted for <paramref name="enemyId"/> is
    /// either running or completed-but-unconsumed.</summary>
    public bool HasJob(ulong enemyId) => _jobs.ContainsKey(enemyId);

    /// <summary>
    /// Queues an approach resolve for <paramref name="enemyId"/>. Caller must
    /// ensure <see cref="HasJob"/> is false first; submitting on top of an
    /// existing job is a logic error and will overwrite (leak) the prior job.
    /// </summary>
    public void Submit(
        ulong enemyId, Vector2 enemyPos, Rid navMap, float standoff,
        List<ApproachCandidate> candidates,
        PocketReachabilityIndex.Snapshot? probe = null)
    {
        Job job = new()
        {
            SubmittedAtMsec = Time.GetTicksMsec(),
            EnemyPos = enemyPos,
            NavMap = navMap,
            Standoff = standoff,
            Candidates = candidates,
            Probe = probe,
        };
        job.TaskId = WorkerThreadPool.AddTask(Callable.From(job.Run));
        _jobs[enemyId] = job;
    }

    /// <summary>
    /// If a job for <paramref name="enemyId"/> has finished and the per-frame
    /// drain budget hasn't been exhausted, delivers its result and frees the
    /// slot. <paramref name="ageMsec"/> is the wall-clock latency between
    /// <see cref="Submit"/> and this drain — the controller uses it to discard
    /// results that took too long to compute. Returns false when no job is
    /// queued, the job is still running, or the budget is full.
    /// </summary>
    public bool TryConsume(ulong enemyId, out ApproachResult result, out ulong ageMsec)
    {
        result = default; ageMsec = 0;

        ulong frame = Engine.GetPhysicsFrames();
        if (frame != _lastDrainFrame)
        {
            _lastDrainFrame = frame;
            _drainsThisFrame = 0;
        }
        if (_drainsThisFrame >= MaxDrainsPerPhysicsFrame) return false;

        if (!_jobs.TryGetValue(enemyId, out Job job)) return false;
        if (!WorkerThreadPool.IsTaskCompleted(job.TaskId)) return false;
        WorkerThreadPool.WaitForTaskCompletion(job.TaskId);
        result = job.Result;
        ageMsec = Time.GetTicksMsec() - job.SubmittedAtMsec;
        _jobs.Remove(enemyId);
        _drainsThisFrame++;
        return true;
    }

    /// <summary>
    /// Block until the enemy's pending job completes, then drop the result.
    /// Use when the enemy is being destroyed so the worker doesn't outlive
    /// any state it might be reading.
    /// </summary>
    public void Cancel(ulong enemyId)
    {
        if (!_jobs.TryGetValue(enemyId, out Job job)) return;
        WorkerThreadPool.WaitForTaskCompletion(job.TaskId);
        _jobs.Remove(enemyId);
    }
}

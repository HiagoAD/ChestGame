using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Pooling.Demo
{
    // Runs one FrameBudgetedLoop per lane, every one reading the same IGameClock and the same
    // budget, all started together under one CancellationTokenSource and awaited through
    // UniTask.WhenAll. The equal budget and the shared clock are the entire mechanism: nothing here
    // paces a lane against the others, so a strategy that places a unit more cheaply gets further.
    //
    // Four lanes contend for the same frames, so no lane's elapsed time here is what it would cost
    // running alone. The simultaneous view proves the ordering between strategies, not any one
    // standalone number; solo mode, which runs exactly one lane, answers that.
    public sealed class PoolRace<T> : IPoolRaceController where T : Component
    {
        private readonly IReadOnlyList<PoolRaceLane<T>> _lanes;
        private readonly IGameClock _clock;
        private readonly double _budgetMilliseconds;
        private readonly CancellationToken _externalToken;

        private CancellationTokenSource _raceCancellation;
        private bool _disposed;

        public bool IsRunning => _raceCancellation != null;
        public RaceResult? LastResult { get; private set; }

        public event Action<RaceResult> OnRaceCompleted;

        // externalToken links every race's own token, so a race in flight when the owner is torn
        // down unwinds instead of filling into lanes that are going away.
        public PoolRace(IReadOnlyList<PoolRaceLane<T>> lanes, IGameClock clock, double budgetMilliseconds,
            CancellationToken externalToken = default)
        {
            if (lanes == null || lanes.Count == 0) throw PoolRaceException.NoLanes();
            if (clock == null) throw PoolRaceException.NoClock();
            if (budgetMilliseconds <= 0) throw PoolRaceException.BudgetNotPositive(budgetMilliseconds);

            HashSet<PoolStrategy> seenStrategies = new();
            foreach (PoolRaceLane<T> lane in lanes)
            {
                if (!seenStrategies.Add(lane.Strategy)) throw PoolRaceException.DuplicateStrategy(lane.Strategy);
            }

            _lanes = lanes;
            _clock = clock;
            _budgetMilliseconds = budgetMilliseconds;
            _externalToken = externalToken;
        }

        // Cancels whatever is running and prepares every lane, not only the ones this run will
        // use, so switching from all four to solo does not leave the other three still holding what
        // they placed last time.
        public void StartRace(int boardSize, FillMode fillMode, bool solo, PoolStrategy soloStrategy)
        {
            if (_disposed) throw PoolRaceException.Disposed();
            if (boardSize < 1) throw PoolRaceException.CountBelowOne(boardSize);

            IReadOnlyList<PoolRaceLane<T>> running = solo ? SoloLane(soloStrategy) : _lanes;

            CancelRace();
            PrepareLanes(running, fillMode, boardSize);

            // Captured in a local because the field can move on to a newer race while this one is
            // still in flight. The completion path compares against this local before touching the
            // field.
            CancellationTokenSource ownCancellation = CancellationTokenSource.CreateLinkedTokenSource(_externalToken);
            _raceCancellation = ownCancellation;
            RunRaceAsync(running, boardSize, fillMode, solo, ownCancellation).Forget();
        }

        public void CancelRace()
        {
            if (_raceCancellation == null) return;

            _raceCancellation.Cancel();
            _raceCancellation.Dispose();
            _raceCancellation = null;
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            CancelRace();
            foreach (PoolRaceLane<T> lane in _lanes) lane.Pool.Dispose();
        }

        private IReadOnlyList<PoolRaceLane<T>> SoloLane(PoolStrategy strategy)
        {
            foreach (PoolRaceLane<T> lane in _lanes)
            {
                if (lane.Strategy == strategy) return new[] { lane };
            }
            throw PoolRaceException.UnknownSoloStrategy(strategy);
        }

        // Every lane's handed-out set comes back first, regardless of mode: the previous race's
        // placements have to be back in their pool before this one can say what it placed.
        //
        // What happens to what is now parked is the whole difference between the three modes. Cold
        // and Prewarmed both trim it away, so a repeated cold run still pays for a real miss;
        // Prewarmed pays that same cost ahead of the timed fill instead of inside it. Reuse trims
        // nothing, so a pooled lane's Get calls are hits against real stock and it can report zero
        // instantiations - what ChestsMinigameView's own NewGame does against the real board.
        private void PrepareLanes(IReadOnlyList<PoolRaceLane<T>> running, FillMode fillMode, int boardSize)
        {
            foreach (PoolRaceLane<T> lane in _lanes) lane.Pool.ReleaseAll();

            switch (fillMode)
            {
                case FillMode.Cold:
                    foreach (PoolRaceLane<T> lane in _lanes) lane.Pool.Trim();
                    break;

                case FillMode.Prewarmed:
                    foreach (PoolRaceLane<T> lane in _lanes) lane.Pool.Trim();
                    foreach (PoolRaceLane<T> lane in running) lane.Pool.Prewarm(boardSize);
                    break;

                case FillMode.Reuse:
                    // Deliberately nothing further. DirectSpawner has nowhere to have parked
                    // anything - ReleaseAll above destroyed what it was handed - so it still
                    // instantiates the whole board, which is the contrast this mode exists to show.
                    break;
            }
        }

        private async UniTaskVoid RunRaceAsync(IReadOnlyList<PoolRaceLane<T>> running, int boardSize, FillMode fillMode, bool solo,
            CancellationTokenSource ownCancellation)
        {
            int laneCount = running.Count;
            UniTask<LaneMetrics>[] tasks = new UniTask<LaneMetrics>[laneCount];

            for (int i = 0; i < laneCount; i++)
            {
                tasks[i] = RunLaneAsync(running[i], boardSize, ownCancellation.Token);
            }

            LaneMetrics[] metrics;
            try
            {
                metrics = await UniTask.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Deliberately nothing, for the reason ChestsMinigameView.FillBoardAsync leaves its
                // own catch empty: the only two things that cancel a race are the next one, which
                // prepares every lane before it starts, and teardown, where this resumes after
                // Dispose has destroyed these pools. A result built here would read counters
                // mid-collapse.
                return;
            }

            // Only publish, and only clear the field, if this is still the race the panel is
            // waiting on. WhenAll completing and this continuation resuming are not the same instant
            // - the continuation is queued on the player loop - so a button press landing in that
            // gap can already have installed a newer race's source in the field. Disposing that one
            // would leave the new race uncancellable, and publishing would drop a superseded race's
            // numbers over it.
            if (!ReferenceEquals(_raceCancellation, ownCancellation)) return;

            _raceCancellation.Dispose();
            _raceCancellation = null;

            RaceResult result = new(metrics, solo, fillMode);
            LastResult = result;
            OnRaceCompleted?.Invoke(result);
        }

        // One lane, start to finish. That lane's numbers are read from inside this task, at the
        // moment its own fill completes: reading them after the sibling tasks have been awaited
        // together would give every lane the slowest lane's finish time.
        private async UniTask<LaneMetrics> RunLaneAsync(PoolRaceLane<T> lane, int boardSize, CancellationToken cancellationToken)
        {
            FrameCountingClock clock = new(_clock);
            double startedAtMilliseconds = _clock.ElapsedMilliseconds;
            int createdBefore = lane.Pool.CreatedCount;
            int destroyedBefore = lane.Pool.DestroyedCount;

            FrameBudgetedLoop loop = new(clock, _budgetMilliseconds);
            await loop.RunAsync(boardSize, index => lane.Pool.Get(lane.FillParent), cancellationToken);

            return new LaneMetrics(
                lane.Strategy,
                boardSize,
                lane.Pool.ActiveCount,
                lane.Pool.CreatedCount - createdBefore,
                lane.Pool.DestroyedCount - destroyedBefore,
                _clock.ElapsedMilliseconds - startedAtMilliseconds,
                clock.FramesUsed);
        }
    }
}

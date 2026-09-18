using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// Runs one <see cref="FrameBudgetedLoop"/> per lane, every one reading the same
    /// <see cref="IGameClock"/> and the same frame budget, all started together under one
    /// <see cref="CancellationTokenSource"/> and awaited through <c>UniTask.WhenAll</c>. Nothing
    /// paces a lane against the others, so a strategy that places a unit more cheaply gets further.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "PoolRace, and why simultaneous lanes are not solo timings".
    /// </remarks>
    public sealed class PoolRace<T> : IPoolRaceController where T : Component
    {
        /// <summary>
        /// The largest selectable board size. Every lane's pool is bounded to this once, so
        /// switching board size between races never has to rebuild a pool, only trim it.
        /// </summary>
        public const int MaxBoardSize = 2000;

        private static readonly int[] BoardSizeValues = { 8, 100, 500, MaxBoardSize };

        private readonly IReadOnlyList<PoolRaceLane<T>> _lanes;
        private readonly IGameClock _clock;
        private readonly double _budgetMilliseconds;
        private readonly CancellationToken _externalToken;

        private CancellationTokenSource _raceCancellation;
        private bool _disposed;

        private int _boardSizeIndex = 1;
        private FillMode _fillMode = FillMode.Cold;
        private bool _solo;
        private PoolStrategy _soloStrategy = PoolStrategy.ActivationPool;
        private float _peakFrameSeconds;

        public IReadOnlyList<int> BoardSizes => BoardSizeValues;
        public int BoardSizeIndex => _boardSizeIndex;
        public FillMode FillMode => _fillMode;
        public bool Solo => _solo;
        public PoolStrategy SoloStrategy => _soloStrategy;

        public bool IsRunning => _raceCancellation != null;
        public RaceResult? LastResult { get; private set; }
        public float PeakFrameSeconds => _peakFrameSeconds;

        public event Action OnSelectionChanged;
        public event Action<RaceResult> OnRaceCompleted;

        /// <summary>
        /// Creates a race over the given lanes.
        /// </summary>
        /// <param name="lanes">The lanes to race. Must be non-empty, with a distinct <see cref="PoolStrategy"/> per lane.</param>
        /// <param name="clock">Clock shared by every lane's fill loop.</param>
        /// <param name="budgetMilliseconds">Per-frame time budget passed to each lane's <see cref="FrameBudgetedLoop"/>. Must be positive.</param>
        /// <param name="externalToken">
        /// Linked into every race this instance starts. Canceling it cancels any race in progress.
        /// </param>
        /// <exception cref="PoolRaceException">
        /// When <paramref name="lanes"/> is null or empty, <paramref name="clock"/> is null,
        /// <paramref name="budgetMilliseconds"/> is not positive, or two lanes share a strategy.
        /// </exception>
        /// <remarks>
        /// See docs/pooling.md, "PoolRace's cancellation and identity traps".
        /// </remarks>
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

        /// <summary>
        /// Cancels whatever race is running, then prepares every lane for the new one - not only
        /// the lanes this run will use - and starts the race.
        /// </summary>
        /// <param name="boardSize">Number of instances each running lane fills. Must be at least 1.</param>
        /// <param name="fillMode">How each lane's pool is prepared before the timed fill begins.</param>
        /// <param name="solo">When true, only <paramref name="soloStrategy"/>'s lane runs.</param>
        /// <param name="soloStrategy">The lane to run alone when <paramref name="solo"/> is true; ignored otherwise.</param>
        /// <exception cref="PoolRaceException">
        /// When this instance is disposed, <paramref name="boardSize"/> is less than 1, or
        /// <paramref name="solo"/> is true and no lane matches <paramref name="soloStrategy"/>.
        /// </exception>
        /// <remarks>
        /// See docs/pooling.md, "PoolRace's cancellation and identity traps".
        /// </remarks>
        public void StartRace(int boardSize, FillMode fillMode, bool solo, PoolStrategy soloStrategy)
        {
            if (_disposed) throw PoolRaceException.Disposed();
            if (boardSize < 1) throw PoolRaceException.CountBelowOne(boardSize);

            IReadOnlyList<PoolRaceLane<T>> running = solo ? SoloLane(soloStrategy) : _lanes;

            CancelRace();
            PrepareLanes(running, fillMode, boardSize);

            _peakFrameSeconds = 0f;
            CancellationTokenSource ownCancellation = CancellationTokenSource.CreateLinkedTokenSource(_externalToken);
            _raceCancellation = ownCancellation;
            RunRaceAsync(running, boardSize, fillMode, solo, ownCancellation).Forget();
        }

        /// <summary>Starts a race over <see cref="BoardSizeIndex"/>, <see cref="FillMode"/>, <see cref="Solo"/> and <see cref="SoloStrategy"/>.</summary>
        public void StartRace() => StartRace(BoardSizeValues[_boardSizeIndex], _fillMode, _solo, _soloStrategy);

        public void CancelRace()
        {
            if (_raceCancellation == null) return;

            _raceCancellation.Cancel();
            _raceCancellation.Dispose();
            _raceCancellation = null;
        }

        public void SetBoardSize(int index)
        {
            _boardSizeIndex = index;
            OnSelectionChanged?.Invoke();
        }

        public void CycleFillMode()
        {
            _fillMode = _fillMode switch
            {
                FillMode.Cold => FillMode.Prewarmed,
                FillMode.Prewarmed => FillMode.Reuse,
                _ => FillMode.Cold
            };
            OnSelectionChanged?.Invoke();
        }

        public void ToggleSolo()
        {
            _solo = !_solo;
            OnSelectionChanged?.Invoke();
        }

        public void SetSoloStrategy(PoolStrategy strategy)
        {
            _soloStrategy = strategy;
            OnSelectionChanged?.Invoke();
        }

        public void Tick(float deltaTimeSeconds)
        {
            if (!IsRunning) return;

            _peakFrameSeconds = Mathf.Max(_peakFrameSeconds, deltaTimeSeconds);
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

        /// <remarks>
        /// See docs/pooling.md, "The demo's fill modes and what they measure".
        /// </remarks>
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
                    break;
            }
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolRace's cancellation and identity traps".
        /// </remarks>
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
                return;
            }

            if (!ReferenceEquals(_raceCancellation, ownCancellation)) return;

            _raceCancellation.Dispose();
            _raceCancellation = null;

            RaceResult result = new(metrics, solo, fillMode);
            LastResult = result;
            OnRaceCompleted?.Invoke(result);
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolRace's cancellation and identity traps".
        /// </remarks>
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

using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// Wraps an <see cref="IGameClock"/> to count the frames one lane's own fill yielded on,
    /// without touching <c>FrameBudgetedLoop</c> or the wrapped clock itself.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "The demo's fill modes and what they measure".
    /// </remarks>
    internal sealed class FrameCountingClock : IGameClock
    {
        private readonly IGameClock _inner;

        /// <summary>
        /// Number of frames this clock has counted, starting at one: a fill that never yields
        /// still ran inside the first frame.
        /// </summary>
        public int FramesUsed { get; private set; } = 1;

        public FrameCountingClock(IGameClock inner)
        {
            _inner = inner;
        }

        public float DeltaTime => _inner.DeltaTime;
        public double ElapsedMilliseconds => _inner.ElapsedMilliseconds;

        public async UniTask NextFrame(CancellationToken cancellationToken)
        {
            await _inner.NextFrame(cancellationToken);
            FramesUsed++;
        }

        public UniTask Delay(int milliseconds, CancellationToken cancellationToken) =>
            _inner.Delay(milliseconds, cancellationToken);
    }
}

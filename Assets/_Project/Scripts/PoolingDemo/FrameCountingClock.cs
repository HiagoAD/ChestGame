using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Pooling.Demo
{
    // Wraps a clock to count the frames one lane's own fill yielded on, without touching
    // FrameBudgetedLoop or IGameClock. Every lane gets its own instance over the same underlying
    // clock, so four lanes sharing one player loop each answer that independently.
    //
    // Starts at one rather than zero: a fill that never yields still ran inside the first frame.
    internal sealed class FrameCountingClock : IGameClock
    {
        private readonly IGameClock _inner;

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

using System.Threading;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Pooling.Demo
{
    // Wraps a clock to count the frames one lane's own fill actually yielded on, without touching
    // FrameBudgetedLoop or IGameClock to get it. Every lane gets its own instance over the same
    // underlying clock, so four lanes sharing one player loop each still answer "how many frames did
    // my own fill use" independently - which is exactly the number that differs between a cheap
    // strategy and an expensive one running under the same budget.
    //
    // Starts at one rather than zero: a fill that never yields still ran inside the first frame, and
    // a lane that placed everything without ever waiting has not used zero frames of it.
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

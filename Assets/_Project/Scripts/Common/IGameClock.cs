using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Common
{
    // Seam over the parts of the engine that make asynchronous gameplay tick: frame advance and
    // elapsed time. Abstracting the clock is what lets the chest-opening flow, including its
    // cancellation paths, be driven deterministically from a unit test instead of raced against a
    // real stopwatch.
    public interface IGameClock
    {
        // Seconds elapsed during the frame that just advanced.
        float DeltaTime { get; }

        // Completes on the next frame, the async equivalent of `yield return null`.
        UniTask NextFrame(CancellationToken cancellationToken);

        // Completes once the given duration has elapsed.
        UniTask Delay(int milliseconds, CancellationToken cancellationToken);
    }
}

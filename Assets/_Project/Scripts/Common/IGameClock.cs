using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Common
{
    // Seam over frame advance and elapsed time, so the chest-opening flow and its cancellation
    // paths can be driven from a unit test instead of raced against a real stopwatch.
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

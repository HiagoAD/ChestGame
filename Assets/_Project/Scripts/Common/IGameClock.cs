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

        // Milliseconds since the game started, moving forward inside a frame as well as between
        // them. DeltaTime cannot answer that: it is fixed for the whole of a frame, so work
        // measuring how much of the current frame it has spent has nothing to read from it.
        double ElapsedMilliseconds { get; }

        // Completes on the next frame, the async equivalent of `yield return null`.
        UniTask NextFrame(CancellationToken cancellationToken);

        // Completes once the given duration has elapsed.
        UniTask Delay(int milliseconds, CancellationToken cancellationToken);
    }
}

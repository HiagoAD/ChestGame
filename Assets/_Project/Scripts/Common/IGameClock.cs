using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// Seam over frame advance and elapsed time.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Engine seams: clock and random".
    /// </remarks>
    public interface IGameClock
    {
        /// <summary>
        /// Seconds elapsed during the frame that just advanced.
        /// </summary>
        float DeltaTime { get; }

        /// <summary>
        /// Milliseconds since the game started, moving forward inside a frame as well as between
        /// them.
        /// </summary>
        /// <remarks>
        /// See docs/architecture.md, "Engine seams: clock and random".
        /// </remarks>
        double ElapsedMilliseconds { get; }

        /// <summary>
        /// Completes on the next frame, the async equivalent of <c>yield return null</c>.
        /// </summary>
        UniTask NextFrame(CancellationToken cancellationToken);

        /// <summary>
        /// Completes once the given duration has elapsed.
        /// </summary>
        UniTask Delay(int milliseconds, CancellationToken cancellationToken);
    }
}

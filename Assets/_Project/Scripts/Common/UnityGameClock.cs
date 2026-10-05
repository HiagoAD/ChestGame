using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Common
{
    /// <summary>
    /// The production clock.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Engine seams: clock and random".
    /// See docs/saving.md, "A frozen clock is a frozen save".
    /// </remarks>
    public class UnityGameClock : IGameClock
    {
        public float DeltaTime => Time.deltaTime;

        /// <remarks>
        /// See docs/saving.md, "A frozen clock is a frozen save".
        /// </remarks>
        public double ElapsedMilliseconds => Time.realtimeSinceStartupAsDouble * 1000d;

        public UniTask NextFrame(CancellationToken cancellationToken) => UniTask.Yield(cancellationToken);

        /// <remarks>
        /// See docs/saving.md, "A frozen clock is a frozen save".
        /// </remarks>
        public UniTask Delay(int milliseconds, CancellationToken cancellationToken) =>
            UniTask.Delay(milliseconds, cancellationToken: cancellationToken);
    }
}

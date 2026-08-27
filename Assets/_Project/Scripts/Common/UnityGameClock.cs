using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Common
{
    // The production clock. Both waits respect Time.timeScale, so pausing the game pauses any chest
    // mid-open.
    public class UnityGameClock : IGameClock
    {
        public float DeltaTime => Time.deltaTime;

        // Realtime rather than Time.time, which is scaled and only moves once per frame. A frame
        // budget is about how long this frame is really taking, so it has to keep reading true while
        // the game is paused and while a single frame is still running.
        public double ElapsedMilliseconds => Time.realtimeSinceStartupAsDouble * 1000d;

        public UniTask NextFrame(CancellationToken cancellationToken) => UniTask.Yield(cancellationToken);

        public UniTask Delay(int milliseconds, CancellationToken cancellationToken) =>
            UniTask.Delay(milliseconds, cancellationToken: cancellationToken);
    }
}

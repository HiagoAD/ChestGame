using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Common
{
    // The production clock. Both waits respect Time.timeScale, so setting it to zero stops every
    // caller waiting on this clock, not only animation. Anything that must keep running while the
    // game is paused needs a different clock or an unscaled wait.
    public class UnityGameClock : IGameClock
    {
        public float DeltaTime => Time.deltaTime;

        // Realtime rather than Time.time, which is scaled and only moves once per frame. A frame
        // budget is about how long this frame is really taking, so it has to keep reading true while
        // the game is paused and while a single frame is still running.
        public double ElapsedMilliseconds => Time.realtimeSinceStartupAsDouble * 1000d;

        public UniTask NextFrame(CancellationToken cancellationToken) => UniTask.Yield(cancellationToken);

        // ignoreTimeScale is left at its false default - the choice the header warns about - so
        // that a future change to it shows up as an argument appearing rather than a silent edit.
        public UniTask Delay(int milliseconds, CancellationToken cancellationToken) =>
            UniTask.Delay(milliseconds, cancellationToken: cancellationToken);
    }
}

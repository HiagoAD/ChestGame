using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Common
{
    // The production clock. Both waits respect Time.timeScale, so pausing the game pauses any chest
    // mid-open.
    //
    // The same fact is a latent trap for anything else that ever waits on Delay for a reason that
    // has nothing to do with a chest: SaveScheduler<T>'s coalescing window is IGameClock.Delay under
    // it, so a future pause menu that sets Time.timeScale to 0 would stop currency (and anything
    // else built on SaveScheduler<T>) from persisting at all until unpause - MarkDirty still records
    // what changed, but the window that turns it into a real write never elapses. Nothing in this
    // game sets Time.timeScale today, so this is not a live bug, only a place a future change can
    // introduce one without touching a single line here. See docs/saving.md, "A frozen clock is a
    // frozen save".
    public class UnityGameClock : IGameClock
    {
        public float DeltaTime => Time.deltaTime;

        // Realtime rather than Time.time, which is scaled and only moves once per frame. A frame
        // budget is about how long this frame is really taking, so it has to keep reading true while
        // the game is paused and while a single frame is still running.
        public double ElapsedMilliseconds => Time.realtimeSinceStartupAsDouble * 1000d;

        public UniTask NextFrame(CancellationToken cancellationToken) => UniTask.Yield(cancellationToken);

        // ignoreTimeScale defaults to false on this overload - the choice this type's own header
        // warns about - rather than passed explicitly, so a future reader diffing this file for
        // "what changed" sees the argument appear, not a default silently keep meaning what it
        // always meant.
        public UniTask Delay(int milliseconds, CancellationToken cancellationToken) =>
            UniTask.Delay(milliseconds, cancellationToken: cancellationToken);
    }
}

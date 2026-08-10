using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Common
{
    // The production clock: the Unity player loop. Both waits respect Time.timeScale, so pausing
    // the game pauses any chest mid-open.
    public class UnityGameClock : IGameClock
    {
        public float DeltaTime => Time.deltaTime;

        public UniTask NextFrame(CancellationToken cancellationToken) => UniTask.Yield(cancellationToken);

        public UniTask Delay(int milliseconds, CancellationToken cancellationToken) =>
            UniTask.Delay(milliseconds, cancellationToken: cancellationToken);
    }
}

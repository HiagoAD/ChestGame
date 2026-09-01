using System;

namespace Company.ChestGame.Pooling.Demo
{
    // The non-generic face of PoolRace<T>, so the MonoBehaviour panel can hold one field and wire
    // one set of buttons regardless of which prefab type it was handed. The generic type only has to
    // exist where a real prefab is, never on anything that sits on a GameObject.
    public interface IPoolRaceController : IDisposable
    {
        bool IsRunning { get; }
        RaceResult? LastResult { get; }

        event Action<RaceResult> OnRaceCompleted;

        void StartRace(int boardSize, FillMode fillMode, bool solo, PoolStrategy soloStrategy);
        void CancelRace();
    }
}

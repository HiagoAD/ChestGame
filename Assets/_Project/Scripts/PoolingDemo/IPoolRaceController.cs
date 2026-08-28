using System;

namespace Company.ChestGame.Pooling.Demo
{
    // The non-generic face of PoolRace<T>, so the MonoBehaviour panel that builds the controls can
    // hold one field and wire one set of buttons regardless of which prefab type it was handed. The
    // generic type only has to exist where a real prefab is - the static factory that builds a race
    // for a given T - never in anything that has to sit on a GameObject.
    public interface IPoolRaceController : IDisposable
    {
        bool IsRunning { get; }
        RaceResult? LastResult { get; }

        event Action<RaceResult> OnRaceCompleted;

        void StartRace(int boardSize, FillMode fillMode, bool solo, PoolStrategy soloStrategy);
        void CancelRace();
    }
}

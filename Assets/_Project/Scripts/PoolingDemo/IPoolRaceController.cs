using System;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// The non-generic face of <see cref="PoolRace{T}"/>, so a <c>MonoBehaviour</c> panel can hold
    /// one field and wire one set of buttons regardless of which prefab type the race was built
    /// for.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "IPoolRaceController, and why the panel is not generic".
    /// </remarks>
    public interface IPoolRaceController : IDisposable
    {
        bool IsRunning { get; }
        RaceResult? LastResult { get; }

        event Action<RaceResult> OnRaceCompleted;

        void StartRace(int boardSize, FillMode fillMode, bool solo, PoolStrategy soloStrategy);
        void CancelRace();
    }
}

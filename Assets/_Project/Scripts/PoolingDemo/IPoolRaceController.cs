using System;
using System.Collections.Generic;
using Company.ChestGame.Mvc;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// The non-generic face of <see cref="PoolRace{T}"/>: the board-size, fill-mode and
    /// solo/strategy selection, the running state, and the peak-frame measurement, so a
    /// <c>MonoBehaviour</c> panel can hold one field and wire one set of buttons regardless of
    /// which prefab type the race was built for.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md. See docs/pooling.md, "IPoolRaceController, and why the panel is not generic".
    /// </remarks>
    public interface IPoolRaceController : IController
    {
        /// <summary>The board sizes selectable through <see cref="SetBoardSize"/>.</summary>
        IReadOnlyList<int> BoardSizes { get; }

        /// <summary>Index into <see cref="BoardSizes"/> that the next <see cref="StartRace"/> races.</summary>
        int BoardSizeIndex { get; }

        /// <summary>How the next <see cref="StartRace"/> prepares each lane's pool.</summary>
        FillMode FillMode { get; }

        /// <summary>Whether the next <see cref="StartRace"/> runs only <see cref="SoloStrategy"/>'s lane.</summary>
        bool Solo { get; }

        /// <summary>The lane <see cref="StartRace"/> runs alone when <see cref="Solo"/> is true.</summary>
        PoolStrategy SoloStrategy { get; }

        bool IsRunning { get; }
        RaceResult? LastResult { get; }

        /// <summary>
        /// The largest per-frame device time <see cref="Tick"/> has recorded since the current or
        /// most recently started race began.
        /// </summary>
        float PeakFrameSeconds { get; }

        /// <summary>Raised after <see cref="BoardSizeIndex"/>, <see cref="FillMode"/>, <see cref="Solo"/> or <see cref="SoloStrategy"/> changes.</summary>
        event Action OnSelectionChanged;

        event Action<RaceResult> OnRaceCompleted;

        void SetBoardSize(int index);

        /// <summary>Cycles <see cref="FillMode"/> Cold -&gt; Prewarmed -&gt; Reuse -&gt; Cold.</summary>
        void CycleFillMode();

        void ToggleSolo();
        void SetSoloStrategy(PoolStrategy strategy);

        /// <summary>Starts a race over the currently selected board size, fill mode, and solo/strategy.</summary>
        void StartRace();

        void CancelRace();

        /// <summary>
        /// Advances the peak-frame measurement by one real device frame. A no-op while
        /// <see cref="IsRunning"/> is false.
        /// </summary>
        /// <param name="deltaTimeSeconds">Seconds the caller's own last frame took.</param>
        void Tick(float deltaTimeSeconds);
    }
}

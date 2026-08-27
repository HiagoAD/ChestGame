using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Common
{
    // Runs a fixed number of units of work across as many frames as they take, yielding whenever the
    // work done since this frame started has passed a time budget. What a screen filling itself with
    // hundreds of objects uses so the fill costs several cheap frames instead of one visible hitch.
    //
    // Budgeted by elapsed time rather than by a count per frame, and that is the whole reason it
    // exists rather than a detail of it. A count makes every caller finish in the same number of
    // frames whatever a unit costs it, which is exactly the difference anyone comparing two ways of
    // doing the same work is trying to see.
    //
    // It knows nothing about what the work is: a unit is an index and an Action, so pools, prefabs
    // and UI all stay on the caller's side of the line.
    public class FrameBudgetedLoop
    {
        private readonly IGameClock _clock;
        private readonly double _budgetMilliseconds;

        public FrameBudgetedLoop(IGameClock clock, double budgetMilliseconds)
        {
            if (clock == null) throw FrameBudgetException.NoClock();
            if (budgetMilliseconds <= 0) throw FrameBudgetException.BudgetNotPositive(budgetMilliseconds);

            _clock = clock;
            _budgetMilliseconds = budgetMilliseconds;
        }

        // Split from the async half below so a bad call throws where it was made. An async method
        // captures everything it throws into the task it returns, and a caller that forgets that
        // task - which is what a fill started from a MonoBehaviour does - would never see it.
        public UniTask RunAsync(int count, Action<int> step, CancellationToken cancellationToken)
        {
            if (step == null) throw FrameBudgetException.NoStep();
            if (count < 0) throw FrameBudgetException.NegativeCount(count);

            return RunCoreAsync(count, step, cancellationToken);
        }

        private async UniTask RunCoreAsync(int count, Action<int> step, CancellationToken cancellationToken)
        {
            double frameStarted = _clock.ElapsedMilliseconds;

            for (int index = 0; index < count; index++)
            {
                // Before the unit rather than after it, so a token cancelled while the previous unit
                // was running gets no further work out of the loop.
                cancellationToken.ThrowIfCancellationRequested();

                step(index);

                // Nothing left to place, so a yield here would buy a frame to do nothing in.
                if (index + 1 == count) break;

                // The budget is read after a unit has run and never before, which is what makes
                // every frame place at least one. The other order would let a unit that costs more
                // than the whole budget yield for ever and place nothing.
                if (_clock.ElapsedMilliseconds - frameStarted < _budgetMilliseconds) continue;

                await _clock.NextFrame(cancellationToken);
                frameStarted = _clock.ElapsedMilliseconds;
            }
        }
    }
}

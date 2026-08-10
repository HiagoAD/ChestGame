using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Rewards;
using Cysharp.Threading.Tasks;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Minigame.Chests.Internal
{
    public class ChestsMinigameController : MinigameControllerBase
    {
        public enum State
        {
            NotStarted,
            Playing,
            Ended
        }

        public event Action<State> OnStateChange;
        public event Action<bool> OnGameFinished;
        public event Action<int> OnAttemptsChanged;

        public State CurrentState
        {
            get => _state;
            private set
            {
                _state = value;
                OnStateChange?.Invoke(_state);
            }
        }


        public int Attempts {
            get => _attempts;
            private set
            {
                _attempts = value;
                OnAttemptsChanged?.Invoke(_attempts);
            }
        }

        public int TotalAttempts { get; private set; }
        public IReadOnlyList<ChestsMinigameChestModel> Chests { get; private set; }



        private int _timeToOpenChestMiliseconds;
        private CancellationTokenSource _openingCancelationTokenSource;
        private IRewardsManager _rewardsManager;
        private IRandomProvider _random;
        private IGameClock _clock;

        private State _state = State.NotStarted;
        private int _attempts = 0;


        [Inject]
        public void Inject(IGameConfig gameConfig, IRewardsManager rewardsManager, IRandomProvider random, IGameClock clock)
        {
            _timeToOpenChestMiliseconds = gameConfig.TimeToOpenChestMiliseconds;
            TotalAttempts = gameConfig.AttempsCount;

            _rewardsManager = rewardsManager;
            _random = random;
            _clock = clock;

            List<ChestsMinigameChestModel> chests = new();
            for (int i = 0; i < gameConfig.ChestCount; i++)
            {
                chests.Add(new());
            }
            Chests = chests.AsReadOnly();
        }

        public override void Dispose()
        {
            CancelOpeningToken();
            _rewardsManager = null;
            _random = null;
            _clock = null;
            OnStateChange = null;
            OnGameFinished = null;
            OnAttemptsChanged = null;
        }

        // As a new game starts, if some chest was opening, cancels it.
        // Closes all chests, to support restart games. This approach doesn't
        // support the number of chests changing between games.       
        public override void NewGame()
        {
            CancelOpeningToken();

            Attempts = 0;

            foreach(var chest in Chests)
            {
                chest.SetClosed();
            }

            CurrentState = State.Playing;
        }

        // When a chest is clicked, checks if the game is still active,
        // and if the chest can be unlocked. If so, cancels any opening chest,
        // and spawns the two tasks that handles its states (opening and open).
        // A very small optimization that could be done is to save the delay at
        // the start. Doing this way it gives support for the time varying between
        // pulls/games
        public void OnChestClicked(ChestsMinigameChestModel chest)
        {
            if (CurrentState != State.Playing) return;
            if (chest.CurrentState != ChestsMinigameChestModel.State.Closed) return;

            CancelOpeningToken();

            _openingCancelationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _openingCancelationTokenSource.Token;
            cancellationToken.Register(() => { chest.SetClosed(); });

            UniTask[] tasks = new UniTask[2]
            {
                UpdateOpeningProgress(chest, _timeToOpenChestMiliseconds, cancellationToken),
                WaitAndOpenChest(chest, _timeToOpenChestMiliseconds, cancellationToken)
            };

            UniTask.WhenAll(tasks).Forget();
        }

        // Because of the Unity archtecture, even if two touches were registered at the same time,
        // they would be handled in series, one after the other, avoiding the need of a true multithreading
        // solution with locks
        private void CancelOpeningToken()
        {
            if (_openingCancelationTokenSource != null)
            {
                _openingCancelationTokenSource.Cancel();
                ClearCancellationToken();
            }
        }

        private void ClearCancellationToken()
        {
            _openingCancelationTokenSource?.Dispose();
            _openingCancelationTokenSource = null;
        }

        // The task that handles the opening state. IGameClock.NextFrame lasts exactly one update
        // loop, the same way a `yield return null` does on a Coroutine, so the time between loops
        // is one frame's delta.
        private async UniTask UpdateOpeningProgress(ChestsMinigameChestModel chest, int millisecondsDelay, CancellationToken cancellationToken)
        {
            float totalTime = millisecondsDelay / 1000f;
            float passedTime = 0;
            while (passedTime < totalTime)
            {
                chest.SetOpening(passedTime / totalTime);

                await _clock.NextFrame(cancellationToken);
                passedTime += _clock.DeltaTime;
            }
        }

        // The task that handles the open state, in a simple way, just setting a Delay, then
        // calling OpenChest
        private async UniTask WaitAndOpenChest(ChestsMinigameChestModel chest, int millisecondsDelay, CancellationToken cancellationToken)
        {
            await _clock.Delay(millisecondsDelay, cancellationToken);
            ClearCancellationToken();

            OpenChest(chest);
        }

        private void OpenChest(ChestsMinigameChestModel chest)
        {
            Attempts++;

            bool hasChestPrize = TryGiveChestPrize();
            chest.SetOpen(hasChestPrize);

            CheckEndGame(hasChestPrize);
        }

        // The prize location is calculated every run to avoid memory inspection.
        // Altho unrealistic, and if any anti-hacker efforts were to be done
        // there would need a lot more than this, was left for demonstration purposes.
        // The simpler approach would be to just save the winner chest index at the new game.
        //
        // The odds model exactly one prize hidden among the chests: with N chests and k already
        // opened empty, the one being opened now holds it with probability 1/(N - k). Attempts has
        // already been incremented for the current chest at this point, so k is (Attempts - 1) and
        // the divisor is (Chests.Count - Attempts + 1). Dropping the +1 would make the odds reach
        // certainty one chest early, so the final chest could never hold the prize.
        private bool TryGiveChestPrize()
        {
            float prizeChance = 1 / (float)(Chests.Count - Attempts + 1);
            if (prizeChance >= _random.Value)
            {

                return true;
            }
            return false;
        }

        private void CheckEndGame(bool hasChestPrize)
        {
            if (hasChestPrize)
            {
                CurrentState = State.Ended;
                _rewardsManager.GiveRandomCurrencyReward("ChestsMinigame");
                OnGameFinished?.Invoke(true);
            }
            else if (Attempts >= TotalAttempts)
            {
                CurrentState = State.Ended;
                OnGameFinished?.Invoke(false);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
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


        // Handed over by ChestsMinigameSO before injection. It has to land first, because the chest
        // list is sized from it.
        public void Configure(ChestsMinigameConfig config)
        {
            _timeToOpenChestMiliseconds = config.TimeToOpenChestMiliseconds;
            TotalAttempts = config.AttempsCount;

            List<ChestsMinigameChestModel> chests = new();
            for (int i = 0; i < config.ChestCount; i++)
            {
                chests.Add(new());
            }
            Chests = chests.AsReadOnly();
        }

        [Inject]
        public void Inject(IRewardsManager rewardsManager, IRandomProvider random, IGameClock clock)
        {
            _rewardsManager = rewardsManager;
            _random = random;
            _clock = clock;
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

        // Supports restarts, but not the number of chests changing between games.
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

        // Spawns the two tasks that drive the chest, opening and open, under one token. The delay is
        // read per click rather than cached, which supports the time varying between pulls.
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

        // No locks needed: Unity handles two simultaneous touches in series, one after the other.
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

        // IGameClock.NextFrame lasts exactly one update loop, the way `yield return null` does on a
        // coroutine, so the time between loops is one frame's delta.
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

        // The prize location is drawn per attempt rather than stored, to avoid memory inspection.
        //
        // The odds model exactly one prize among the chests: with N chests and k already opened
        // empty, this one holds it with probability 1/(N - k). Attempts is already incremented by
        // here, so k is (Attempts - 1). Dropping the +1 makes the odds reach certainty one chest
        // early and the last chest could never hold the prize.
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

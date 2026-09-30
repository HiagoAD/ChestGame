using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Rewards;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;
using UnityEngine;
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

        private ISaveFlushRegistry _flushRegistry;
        private SaveScheduler<ChestsRunSaveDocument> _scheduler;

        /// <summary>
        /// Loaded once during <see cref="Inject"/>, consumed by the first <see cref="NewGame"/> call
        /// after it.
        /// </summary>
        private ChestsRunSaveDocument _pendingRestore;

        private State _state = State.NotStarted;
        private int _attempts = 0;


        /// <summary>
        /// Call before <see cref="Inject"/> and before <see cref="NewGame"/>: the chest list is
        /// sized from this, and nothing works until it has run.
        /// </summary>
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

        /// <summary>
        /// Call after <see cref="Configure"/>. Builds the save scheduler this controller owns and
        /// registers it for flushing, so <see cref="Dispose"/> must run when the minigame ends or
        /// that registration outlives it.
        /// </summary>
        /// <exception cref="SaveException">
        /// <paramref name="saveService"/> does not complete on the calling thread.
        /// </exception>
        /// <remarks>
        /// See docs/saving.md, "A scheduler the composition root cannot name has to register itself".
        /// </remarks>
        [Inject]
        public void Inject(IRewardsManager rewardsManager, IRandomProvider random, IGameClock clock,
            ISaveService saveService, ISaveFlushRegistry saveFlushRegistry)
        {
            _rewardsManager = rewardsManager;
            _random = random;
            _clock = clock;

            if (!saveService.CompletesOnCallingThread) throw SaveException.SynchronousLoadNeedsNonHoppingStore();

            _flushRegistry = saveFlushRegistry;
            _scheduler = new SaveScheduler<ChestsRunSaveDocument>(saveService, ChestsRunSaveDocument.SaveKey, clock);

            try
            {
                _pendingRestore = saveService
                    .LoadAsync<ChestsRunSaveDocument>(ChestsRunSaveDocument.SaveKey, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (SaveException exception)
            {
                Debug.LogError($"The saved chests run could not be read and is being discarded: {exception.Message}");
                _pendingRestore = null;
            }

            _flushRegistry.Register(_scheduler);
        }

        /// <remarks>
        /// See docs/saving.md, "A scheduler the composition root cannot name has to register itself".
        /// </remarks>
        public override void Dispose()
        {
            CancelOpeningToken();
            _rewardsManager = null;
            _random = null;
            _clock = null;
            OnStateChange = null;
            OnGameFinished = null;
            OnAttemptsChanged = null;

            _flushRegistry?.Unregister(_scheduler);
            _scheduler?.Dispose();
            _flushRegistry = null;
            _scheduler = null;
        }

        /// <summary>
        /// Starts a round. Safe to call repeatedly, and safe after <see cref="Dispose"/>.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "Restore, discard, and why it lives in NewGame()".
        /// See docs/minigames.md, "The controller".
        /// </remarks>
        public override void NewGame()
        {
            CancelOpeningToken();

            Attempts = 0;

            foreach(var chest in Chests)
            {
                chest.SetClosed();
            }

            ChestsRunSaveDocument restore = _pendingRestore;
            _pendingRestore = null;

            if (restore != null && !ShouldDiscardRestore(restore))
            {
                RestoreFrom(restore);
            }
            else
            {
                _scheduler?.MarkDirty(new ChestsRunSaveDocument());
            }

            CurrentState = State.Playing;
        }

        /// <remarks>
        /// See docs/saving.md, "Restore, discard, and why it lives in NewGame()".
        /// </remarks>
        private bool ShouldDiscardRestore(ChestsRunSaveDocument document)
        {
            List<int> indices = document.OpenedChestIndices;

            if (indices == null) return true;
            if (document.ChestCount != Chests.Count) return true;
            if (indices.Count >= TotalAttempts) return true;

            HashSet<int> seen = new();
            foreach (int index in indices)
            {
                if (index < 0 || index >= Chests.Count) return true;
                if (!seen.Add(index)) return true;
            }

            return false;
        }

        /// <summary>
        /// Must run on a board whose chests are all closed. The document must be one
        /// <c>ShouldDiscardRestore</c> accepted: this dereferences <c>OpenedChestIndices</c> without a null check.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "Restore, discard, and why it lives in NewGame()".
        /// </remarks>
        private void RestoreFrom(ChestsRunSaveDocument document)
        {
            foreach (int index in document.OpenedChestIndices)
            {
                Chests[index].SetOpen(false);
            }

            Attempts = document.OpenedChestIndices.Count;
        }

        /// <summary>
        /// Only ever records chests opened empty.
        /// </summary>
        /// <remarks>
        /// See docs/minigames.md, "Prize odds".
        /// </remarks>
        private ChestsRunSaveDocument BuildCurrentRunDocument()
        {
            List<int> opened = new();
            for (int i = 0; i < Chests.Count; i++)
            {
                if (Chests[i].CurrentState == ChestsMinigameChestModel.State.Open_Empty) opened.Add(i);
            }

            return new ChestsRunSaveDocument { ChestCount = Chests.Count, OpenedChestIndices = opened };
        }

        /// <remarks>
        /// See docs/minigames.md, "The controller".
        /// </remarks>
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

        /// <remarks>
        /// See docs/minigames.md, "The controller".
        /// </remarks>
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

        /// <remarks>
        /// See docs/minigames.md, "The controller".
        /// </remarks>
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

            _scheduler?.MarkDirty(BuildCurrentRunDocument());

            CheckEndGame(hasChestPrize);
        }

        /// <remarks>
        /// See docs/minigames.md, "Prize odds".
        /// </remarks>
        private bool TryGiveChestPrize()
        {
            float prizeChance = 1 / (float)(Chests.Count - Attempts + 1);
            if (prizeChance >= _random.Value)
            {

                return true;
            }
            return false;
        }

        /// <remarks>
        /// See docs/minigames.md, "The controller".
        /// </remarks>
        private void CheckEndGame(bool hasChestPrize)
        {
            if (hasChestPrize)
            {
                CurrentState = State.Ended;
                _scheduler?.MarkDirty(new ChestsRunSaveDocument());
                _rewardsManager.GiveRandomCurrencyReward("ChestsMinigame");
                OnGameFinished?.Invoke(true);
            }
            else if (Attempts >= TotalAttempts)
            {
                CurrentState = State.Ended;
                _scheduler?.MarkDirty(new ChestsRunSaveDocument());
                OnGameFinished?.Invoke(false);
            }
        }
    }
}

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

        // Loaded once during Inject, consumed by the first NewGame call after it.
        private ChestsRunSaveDocument _pendingRestore;

        private State _state = State.NotStarted;
        private int _attempts = 0;


        // Call before Inject and before NewGame: the chest list is sized from this, and nothing
        // works until it has run.
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

        // Call after Configure. Builds the save scheduler this controller owns and registers it for
        // flushing, so Dispose must run when the minigame ends or that registration outlives it.
        [Inject]
        public void Inject(IRewardsManager rewardsManager, IRandomProvider random, IGameClock clock,
            ISaveService saveService, ISaveFlushRegistry saveFlushRegistry)
        {
            _rewardsManager = rewardsManager;
            _random = random;
            _clock = clock;

            // The restore below blocks on a load, which is only safe when the service never leaves
            // the calling thread. Refused here rather than deadlocking later.
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
            // An unreadable run is discarded rather than fatal: the next NewGame overwrites it, so
            // the save repairs itself instead of refusing to open the minigame on every launch.
            // Only this failure is caught; a wiring mistake still propagates.
            catch (SaveException exception)
            {
                Debug.LogError($"The saved chests run could not be read and is being discarded: {exception.Message}");
                _pendingRestore = null;
            }

            // Last on purpose: anything above throwing must not leave a registration behind for a
            // controller that never finished being injected and so will never be disposed.
            _flushRegistry.Register(_scheduler);
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

            // Null-conditional so a second Dispose is still a no-op.
            _flushRegistry?.Unregister(_scheduler);
            _scheduler?.Dispose();
            _flushRegistry = null;
            _scheduler = null;
        }

        // Starts a round. The first call after Inject resumes a saved run if one still fits this
        // configuration and discards it otherwise; every later call always starts fresh, so a
        // restart is never itself resumable. Safe to call repeatedly, and safe after Dispose.
        //
        // Supports restarts, but not the number of chests changing between rounds.
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

        // Each condition is a state a stored run can legitimately be in: a configuration that
        // changed between sessions, an edited or truncated save, or a run that had already ended.
        private bool ShouldDiscardRestore(ChestsRunSaveDocument document)
        {
            List<int> indices = document.OpenedChestIndices ?? new List<int>();

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

        // Must run on a board whose chests are all closed: opening one that is already open is a
        // no-op, so restoring onto anything else silently does nothing.
        private void RestoreFrom(ChestsRunSaveDocument document)
        {
            foreach (int index in document.OpenedChestIndices)
            {
                Chests[index].SetOpen(false);
            }

            Attempts = document.OpenedChestIndices.Count;
        }

        // Only ever records chests opened empty, so a stored run cannot say where the prize is.
        // Keep it that way: a round that finds the prize ends in the same call, and stores nothing.
        private ChestsRunSaveDocument BuildCurrentRunDocument()
        {
            List<int> opened = new();
            for (int i = 0; i < Chests.Count; i++)
            {
                if (Chests[i].CurrentState == ChestsMinigameChestModel.State.Open_Empty) opened.Add(i);
            }

            return new ChestsRunSaveDocument { ChestCount = Chests.Count, OpenedChestIndices = opened };
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

            _scheduler?.MarkDirty(BuildCurrentRunDocument());

            CheckEndGame(hasChestPrize);
        }

        // Drawn per attempt and never stored anywhere, which is what keeps the prize location out
        // of memory and out of any save.
        //
        // With N chests and k already opened empty, this one holds the prize with probability
        // 1/(N - k). Attempts is already incremented by here, so k is (Attempts - 1). Dropping that
        // +1 makes the odds reach certainty one chest early and the last chest can never win.
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
                // Overwrites what opening the chest just recorded, so a finished run never resumes.
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

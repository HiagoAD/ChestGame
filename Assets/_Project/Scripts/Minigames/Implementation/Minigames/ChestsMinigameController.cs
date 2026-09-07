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

        // Loaded once during Inject, consumed by the first NewGame() call after it - see
        // docs/saving.md.
        private ChestsRunSaveDocument _pendingRestore;

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

        // Core deliberately does not reference Company.ChestGame.Minigame.Chests, so this controller
        // cannot be registered - and its scheduler cannot be resolved - by GameLifetimeScope. It
        // builds its own SaveScheduler<ChestsRunSaveDocument> instead and registers it with
        // ISaveFlushRegistry directly, exactly what that registry exists for. See docs/saving.md.
        [Inject]
        public void Inject(IRewardsManager rewardsManager, IRandomProvider random, IGameClock clock,
            ISaveService saveService, ISaveFlushRegistry saveFlushRegistry)
        {
            _rewardsManager = rewardsManager;
            _random = random;
            _clock = clock;

            // Mirrors CurrencyResourceBankSaveHandle.Load(): safe to block on only when nothing in
            // the composition ever leaves the calling thread. See docs/saving.md.
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
            // A run this build cannot read is discarded, not fatal: it holds no reward a player
            // earned - the win pays out through currency's own save - so starting fresh costs at
            // most an unfinished run, where letting it escape would refuse to open the minigame at
            // all, every time, with nothing that ever clears it. NewGame()'s discard branch
            // overwrites the unreadable document, so the next launch reads cleanly. Deliberately not
            // SaveMigrationException, which is a wiring mistake rather than a delivery failure.
            catch (SaveException exception)
            {
                Debug.LogError($"The saved chests run could not be read and is being discarded: {exception.Message}");
                _pendingRestore = null;
            }

            // Last, so nothing above throwing can leave a scheduler registered for the lifetime of
            // the process against a controller that never finished being injected - MinigameContainer
            // does not Dispose() a controller whose BeginAsync failed.
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

            // Null-conditional for the same reason every other field here is nulled and never
            // re-read: Dispose() was idempotent before this phase and IDisposable requires it to
            // stay that way.
            _flushRegistry?.Unregister(_scheduler);
            _scheduler?.Dispose();
            _flushRegistry = null;
            _scheduler = null;
        }

        // Supports restarts, but not the number of chests changing between games. The first call
        // after Inject also resolves whatever run was pending from Load(): restored if it still
        // fits this configuration, discarded otherwise. Every later call discards whatever is
        // currently stored before resetting, so a restart is never itself resumable. _scheduler is
        // accessed through ?. below, not because it can be null before Inject runs, but because
        // Dispose_DropsEveryEventSubscriber calls NewGame() after Dispose() to prove every
        // subscriber was cleared - the same reason this method never touched _rewardsManager,
        // _random or _clock either.
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

        // Each condition is a real state a saved run can legitimately be in, not defensive
        // paranoia: a server-side config change, a hand-edited or truncated save, or a run that had
        // already ended before the process went away.
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

        // Runs after every chest above has already been SetClosed(): SetOpen returns early on a
        // chest that is already open, so restoring has to land on a closed board. Every restored
        // chest is opened empty - decision #9 again, from the other side: nothing here could name
        // one as the prize chest even if it wanted to.
        private void RestoreFrom(ChestsRunSaveDocument document)
        {
            foreach (int index in document.OpenedChestIndices)
            {
                Chests[index].SetOpen(false);
            }

            Attempts = document.OpenedChestIndices.Count;
        }

        // The only shape a mid-run save is ever built from: every opened chest is Open_Empty by
        // construction (CheckEndGame overwrites this with an empty document the same call that
        // opens the prize chest), so this can never name where the prize is.
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
                // A finished run must never resume - overwrites whatever OpenChest just marked
                // dirty, including the prize chest's own index.
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

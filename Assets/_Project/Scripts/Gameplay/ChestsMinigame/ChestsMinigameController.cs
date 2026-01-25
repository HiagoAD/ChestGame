using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

using Random = UnityEngine.Random;

namespace Company.ChestGame.Gameplay.ChestsMinigame
{
    public class ChestsMinigameController : MonoBehaviour
    {
        private enum State
        {
            NotStarted,
            Playing,
            Ended
        }

        [SerializeField] private ChestsMinigameChestView _chestPrefab;
        [SerializeField] private Transform _chestsParent;
        [SerializeField] private TextMeshProUGUI _attemptsText;
        [SerializeField] private TextMeshProUGUI _controlMessage;

        private State _currentState;
        private int _attempts;

        private int _chestCount;
        private int _timeToOpenChestMiliseconds;
        private int _totalAttempts;

        private List<ChestsMinigameChestView> _chestInstances;
        private CancellationTokenSource _openingCancelationTokenSource;
        private Action<bool> _onGameFinished;

        private bool _isSet;

        private void Awake()
        {
            _currentState = State.NotStarted;
            UpdateAttemptsText(true);
            SetControlMessage(null);
        }

        public void Set(int chestCount, int timeToOpenChestMiliseconds, int totalAttempts, Action<bool> gameFinishedCallback)
        {
            _chestCount = chestCount;
            _timeToOpenChestMiliseconds = timeToOpenChestMiliseconds;
            _totalAttempts = totalAttempts;
            _onGameFinished = gameFinishedCallback;
            _isSet = true;
        }

        // As a new game starts, if some chest was opening, cancels it.
        // If the list of instances is null, indicating a new game,
        // the chests are instantiated, else it resets
        // the state of the currently spawned ones. This approach doesn't
        // support the number of chests changing between games.       
        public void NewGame()
        {
            Debug.Assert(_isSet, "Chests Minigame Not Set");

            CancelOpeningToken();

            if (_chestInstances == null)
            {
                _chestInstances = new();

                for (int i = 0; i < _chestCount; i++)
                {
                    ChestsMinigameChestView instance = Instantiate(_chestPrefab, _chestsParent);
                    instance.SetClickCallback(OnChestClicked);
                    _chestInstances.Add(instance);
                }
            }
            else
            {
                foreach (ChestsMinigameChestView chest in _chestInstances)
                {
                    chest.SetClosed();
                }
            }

            _attempts = 0;
            UpdateAttemptsText();

            SetControlMessage(null);

            _currentState = State.Playing;
        }

        // When a chest is clicked, checks if the game is still active,
        // and if the chest can be unlocked. If so, cancels any opening chest,
        // and spawns the two tasks that handles its states (opening and open).
        // A very small optimization that could be done is to save the delay at
        // the start. Doing this way it gives support for the time varying between
        // pulls/games
        private void OnChestClicked(ChestsMinigameChestView chest)
        {
            if (_currentState != State.Playing) return;
            if (chest.CurrentState != ChestsMinigameChestView.State.Closed) return;

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

        // The task that handles the opening state. Relies on the UniTask.Yield documentation
        // that guarantees that it lasts for a update loop, working the same way as a yield return null
        // on a Coroutine, respecting Time.timeScale. This way, the time between loops is Time.deltaTime
        private async UniTask UpdateOpeningProgress(ChestsMinigameChestView chest, int millisecondsDelay, CancellationToken cancellationToken)
        {
            float totalTime = millisecondsDelay / 1000f;
            float passedTime = 0;
            while (passedTime < totalTime)
            {
                chest.SetOpening(passedTime / totalTime);

                await UniTask.Yield(cancellationToken: cancellationToken);
                passedTime += Time.deltaTime;
            }
        }

        // The task that handles the open state, in a simple way, just setting a Delay, then 
        // calling OpenChest
        private async UniTask WaitAndOpenChest(ChestsMinigameChestView chest, int millisecondsDelay, CancellationToken cancellationToken)
        {
            await UniTask.Delay(millisecondsDelay: millisecondsDelay, cancellationToken: cancellationToken);
            ClearCancellationToken();

            OpenChest(chest);
        }

        private void OpenChest(ChestsMinigameChestView chest)
        {
            _attempts++;
            UpdateAttemptsText();

            bool hasChestPrize = TryGiveChestPrize();
            chest.SetOpen(hasChestPrize);

            CheckEndGame(hasChestPrize);
        }

        // This should a class by itself, but for simplicity is left here.
        // The prize location is calculated every run to avoid memory inspection.
        // Altho unrealistic, and if any anti-hacker efforts were to be done
        // there would need a lot more than this, was left for demonstration purposes.
        // The simpler approach would be to just save the winner chest index at the new game.
        private bool TryGiveChestPrize()
        {
            float prizeChance = 1 / (float)(_chestCount - _attempts);
            if (prizeChance >= Random.value)
            {

                return true;
            }
            return false;
        }

        private void CheckEndGame(bool hasChestPrize)
        {
            if (hasChestPrize)
            {
                _currentState = State.Ended;
                SetControlMessage("You won!");
                _onGameFinished?.Invoke(true);
            }
            else if (_attempts >= _totalAttempts)
            {
                _currentState = State.Ended;
                SetControlMessage("Game Over! Out of attempts!");
                _onGameFinished?.Invoke(false);
            }

        }

        private void UpdateAttemptsText(bool empty = false)
        {
            _attemptsText.text = empty ? "" : $"Attempts: {_attempts} / {_totalAttempts}";
        }

        private void SetControlMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                _controlMessage.gameObject.SetActive(false);
                return;
            }
            else
            {
                _controlMessage.text = message;
                _controlMessage.gameObject.SetActive(true);
            }
        }
    }
}

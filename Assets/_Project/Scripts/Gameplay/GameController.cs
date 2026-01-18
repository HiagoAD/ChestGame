using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using Company.ChestGame.Config;
using Company.ChestGame.Currency;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

using Random = UnityEngine.Random;

namespace Company.ChestGame.Gameplay
{
    // The main class of the game, controls everything, and should be split on a proper game,
    // with the logic of the minigame being handled somewhere else, and only instantiating 
    // the minigame prefab. But as this project is so simple, doing this way avoids boilerplates.
    // 
    // The key area here is to control async the chest state. To display this control better,
    // a weird approach was taken, where two concurrent async tasks run in parallel, one
    // updating every frame the slider inside the chest, and the other waiting until the
    // timer finishes to open the chest, with both being controlled by the same cancellation
    // token to ensure that they behave in sync with each other.
    //
    // This game doesn't have persistence. At each new game, a the amount of attemps is reset.

    public class GameController : MonoBehaviour
    {
        private enum State
        {
            NotStarted,
            Playing,
            Ended
        }

        [SerializeField] private Chest _chestPrefab;
        [SerializeField] private Transform _chestsParent;
        [SerializeField] private Button _startButton;
        [SerializeField] private TextMeshProUGUI _attemptsText;
        [SerializeField] private TextMeshProUGUI _controlMessage;

        private State _currentState;

        private CurrencyManager _currencyManager;
        private GameConfig _gameConfig;
        private List<Chest> _chestInstances;

        private int _attempts;
        private CancellationTokenSource _openingCancelationTokenSource;


        [Inject]
        private void Inject(CurrencyManager currencyManager, GameConfig gameConfig)
        {
            _currencyManager = currencyManager;
            _gameConfig = gameConfig;
        }

        private void Awake()
        {
            _startButton.onClick.AddListener(NewGame);
            UpdateAttemptsText(true);
            SetControlMessage(null);
            _currentState = State.NotStarted;
        }

        // As a new game starts, if some chest was opening, cancels it.
        // If the list of instances is null, indicating a new game,
        // the chests are instantiated, else it resets
        // the state of the currently spawned ones. This approach doesn't
        // support the number of chests changing between games.        
        private void NewGame()
        {
            CancelOpeningToken();
            if (_chestInstances == null)
            {
                _chestInstances = new();

                for (int i = 0; i < _gameConfig.ChestCount; i++)
                {
                    Chest instance = Instantiate(_chestPrefab, _chestsParent);
                    instance.SetClickCallback(OnChestClicked);
                    _chestInstances.Add(instance);
                }
            }
            else
            {
                foreach (Chest chest in _chestInstances)
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
        private void OnChestClicked(Chest chest)
        {
            if (_currentState != State.Playing) return;
            if (chest.CurrentState != Chest.State.Closed) return;

            CancelOpeningToken();

            _openingCancelationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _openingCancelationTokenSource.Token;
            cancellationToken.Register(() => { chest.SetClosed(); });

            int millisecondsDelay = (int)_gameConfig.TimeToOpenChest.TotalMilliseconds;

            UniTask[] tasks = new UniTask[2]
            {
                UpdateOpeningProgress(chest, millisecondsDelay, cancellationToken),
                WaitAndOpenChest(chest, millisecondsDelay, cancellationToken)
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
        private async UniTask UpdateOpeningProgress(Chest chest, int millisecondsDelay, CancellationToken cancellationToken)
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
        private async UniTask WaitAndOpenChest(Chest chest, int millisecondsDelay, CancellationToken cancellationToken)
        {
            await UniTask.Delay(millisecondsDelay: millisecondsDelay, cancellationToken: cancellationToken);
            ClearCancellationToken();

            OpenChest(chest);
        }

        private void OpenChest(Chest chest)
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
            float prizeChance = 1 / (float)(_gameConfig.ChestCount - _attempts);
            if (prizeChance >= Random.value)
            {
                CurrencyType currencyType = (CurrencyType)Random.Range(0, Enum.GetValues(typeof(CurrencyType)).Length);

                long amount = currencyType switch
                {
                    CurrencyType.Coins => _gameConfig.CoinsReward,
                    CurrencyType.Gems => _gameConfig.GemsReward,
                    _ => throw new NotImplementedException()
                };

                _currencyManager.AddCurrency(currencyType, amount, "ChestOpen");

                SetControlMessage($"You Won! +{amount} {currencyType}");
                return true;
            }
            return false;
        }

        private void CheckEndGame(bool hasChestPrize)
        {
            if(hasChestPrize)
            {
                _currentState = State.Ended;
            }
            else if (_attempts >= _gameConfig.AttempsCount)
            {
                _currentState = State.Ended;
                SetControlMessage("Game Over! Out of attempts!");
            }
        }

        private void UpdateAttemptsText(bool empty = false)
        {
            _attemptsText.text = empty ? "" : $"Attempts: {_attempts} / {_gameConfig.AttempsCount}";
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

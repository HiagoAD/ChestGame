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

        private void OnChestClicked(Chest chest)
        {
            if (_currentState != State.Playing) return;
            if (chest.CurrentState != Chest.State.Closed) return;

            CancelOpeningToken();

            _openingCancelationTokenSource = new CancellationTokenSource();
            CancellationToken cancellationToken = _openingCancelationTokenSource.Token;
            cancellationToken.Register(() => { chest.SetClosed(); });

            UniTask[] tasks = new UniTask[2]
            {
                UpdateOpeningProgress(chest, (int)_gameConfig.TimeToOpenChest.TotalMilliseconds, cancellationToken),
                WaitAndOpenChest(chest, (int)_gameConfig.TimeToOpenChest.TotalMilliseconds, cancellationToken)
            };

            UniTask.WhenAll(tasks).Forget();
        }

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
        private async UniTask WaitAndOpenChest(Chest chest, int millisecondsDelay, CancellationToken cancellationToken)
        {
            await UniTask.Delay(millisecondsDelay: millisecondsDelay, cancellationToken: cancellationToken);
            ClearCancellationToken();

            _attempts++;
            UpdateAttemptsText();

            chest.SetOpen(ChestHasPrize());

            if(_currentState == State.Playing && _attempts >= _gameConfig.AttempsCount)
            {
                _currentState = State.Ended;
                SetControlMessage("Game Over! Out of attempts!");
            }
        }

        private bool ChestHasPrize()
        {
            float prizeChance = 1 / (float)(_gameConfig.ChestCount - _attempts);
            if (prizeChance >= Random.value)
            {
                CurrencyType currencyType = (CurrencyType)Random.Range(0, Enum.GetValues(typeof(CurrencyType)).Length);

                _currencyManager.AddCurrency(currencyType, 10, "ChestOpen");

                _currentState = State.Ended;
                SetControlMessage($"You Won! +10 {currencyType}");
                return true;
            }
            return false;
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

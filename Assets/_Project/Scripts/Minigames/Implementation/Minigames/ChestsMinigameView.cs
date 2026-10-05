using System;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Pooling;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using VContainer;

namespace Company.ChestGame.Minigame.Chests.Internal
{
    public class ChestsMinigameView : MinigameViewBase<ChestsMinigameController>
    {
        /// <summary>
        /// Milliseconds of frame time the board fill may spend before yielding to the next frame.
        /// </summary>
        /// <remarks>
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// </remarks>
        private const double FillBudgetMilliseconds = 2d;

        [SerializeField] private ChestsMinigameChestElementView _chestPrefab;
        [SerializeField] private Transform _chestsParent;
        [SerializeField] private TextMeshProUGUI _attemptsText;
        [SerializeField] private TextMeshProUGUI _controlMessage;

        /// <summary>
        /// Pool implementation used to acquire and release chest instances, authored on the prefab.
        /// </summary>
        /// <remarks>
        /// See docs/design-decisions.md, "Why ParkedPool is the default".
        /// </remarks>
        [SerializeField] private PoolStrategy _poolStrategy = PoolStrategy.ParkedPool;

        private readonly List<ChestsMinigameChestElementView> _chestInstances = new();

        private IGameClock _clock;
        private IPrefabPool<ChestsMinigameChestElementView> _pool;
        private FrameBudgetedLoop _fill;
        private CancellationTokenSource _fillCancellation;

        /// <summary>
        /// Supplies the game clock used to budget the board fill.
        /// </summary>
        /// <remarks>
        /// See docs/minigames.md, "The views".
        /// </remarks>
        [Inject]
        public void Inject(IGameClock clock)
        {
            _clock = clock;
        }

        private void Awake()
        {
            UpdateAttemptsText(true);
            SetControlMessage(null);
        }

        /// <remarks>
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// </remarks>
        protected override void OnControllerSet()
        {
            _pool = CreatePool(Controller.Chests.Count);

            _fill = new FrameBudgetedLoop(_clock, FillBudgetMilliseconds);

            Controller.OnStateChange += OnControllerStateChanged;
            Controller.OnGameFinished += OnGameFinished;
            Controller.OnAttemptsChanged += UpdateAttemptsText;
        }

        /// <remarks>
        /// See docs/minigames.md, "The views".
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// </remarks>
        private void OnDestroy()
        {
            CancelFill();

            foreach (ChestsMinigameChestElementView instance in _chestInstances)
            {
                if (instance != null) instance.Release();
            }
            _chestInstances.Clear();

            _pool?.Dispose();
            _pool = null;

            if (Controller == null) return;

            Controller.OnStateChange -= OnControllerStateChanged;
            Controller.OnGameFinished -= OnGameFinished;
            Controller.OnAttemptsChanged -= UpdateAttemptsText;
        }

        private void OnControllerStateChanged(ChestsMinigameController.State state)
        {
            if (state == ChestsMinigameController.State.Playing)
            {
                StartGame();
            }
        }

        /// <remarks>
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// </remarks>
        private void StartGame()
        {
            RebuildBoard();

            UpdateAttemptsText();
            SetControlMessage(null);
        }

        /// <remarks>
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// </remarks>
        private void RebuildBoard()
        {
            CancelFill();
            ReleaseBoard();

            _fillCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            FillBoardAsync(_fillCancellation.Token).Forget();
        }

        /// <remarks>
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// </remarks>
        private async UniTaskVoid FillBoardAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _fill.RunAsync(Controller.Chests.Count, AcquireChest, cancellationToken);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void AcquireChest(int index)
        {
            ChestsMinigameChestElementView instance = _pool.Get(_chestsParent);
            instance.Init(Controller.Chests[index], Controller.OnChestClicked);

            _chestInstances.Add(instance);
        }

        /// <remarks>
        /// See docs/minigames.md, "A chest has two lifetimes now".
        /// </remarks>
        private void ReleaseBoard()
        {
            foreach (ChestsMinigameChestElementView instance in _chestInstances)
            {
                instance.Release();
                _pool.Release(instance);
            }

            _chestInstances.Clear();
        }

        private void CancelFill()
        {
            if (_fillCancellation == null) return;

            _fillCancellation.Cancel();
            _fillCancellation.Dispose();
            _fillCancellation = null;
        }

        /// <remarks>
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// </remarks>
        private IPrefabPool<ChestsMinigameChestElementView> CreatePool(int boardSize) =>
            PoolFactory.Create(_poolStrategy, _chestPrefab, transform, boardSize, "ChestPool");

        private void OnGameFinished(bool won)
        {
            string message = won ? "You won!" : "Game Over! Out of attempts!";
            SetControlMessage(message);
        }

        private void UpdateAttemptsText(int _) => UpdateAttemptsText();
        private void UpdateAttemptsText(bool empty = false)
        {
            _attemptsText.text = empty ? "" : $"Attempts: {Controller.Attempts} / {Controller.TotalAttempts}";
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

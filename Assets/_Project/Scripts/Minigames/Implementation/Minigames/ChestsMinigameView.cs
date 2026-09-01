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
    public class ChestsMinigameView : MinigameViewBase
    {
        // How much of a frame the board fill may spend before it yields. A constant rather than an
        // authored field, because a serialized zero would be a board that takes one frame per chest.
        private const double FillBudgetMilliseconds = 2d;

        [SerializeField] private ChestsMinigameChestElementView _chestPrefab;
        [SerializeField] private Transform _chestsParent;
        [SerializeField] private TextMeshProUGUI _attemptsText;
        [SerializeField] private TextMeshProUGUI _controlMessage;

        // Measured fastest on the rebuild this view does every NewGame. Numbers and reasoning in
        // docs/design-decisions.md.
        [SerializeField] private PoolStrategy _poolStrategy = PoolStrategy.ParkedPool;

        private readonly List<ChestsMinigameChestElementView> _chestInstances = new();

        private IGameClock _clock;
        private IPrefabPool<ChestsMinigameChestElementView> _pool;
        private FrameBudgetedLoop _fill;
        private CancellationTokenSource _fillCancellation;
        private ChestsMinigameController _controller;

        // The clock is injected here rather than on the controller because the fill is this view's,
        // and it is the seam that lets a test decide when a frame happens.
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

        public override void SetController(MinigameControllerBase controller)
        {
            Debug.Assert(controller is ChestsMinigameController, $"Wrong controller type, ChestsMinigameController expected, got {controller.GetType()} instead");

            _controller = (ChestsMinigameController)controller;

            // Here rather than in Awake because the bound is the board size, which comes off the
            // controller. Injection has landed by now: the container instantiates and injects this
            // view before it hands the controller over.
            _pool = CreatePool(_controller.Chests.Count);

            // Built here rather than per fill, so a view that was never injected says so now rather
            // than from inside a forgotten async task later.
            _fill = new FrameBudgetedLoop(_clock, FillBudgetMilliseconds);

            _controller.OnStateChange += OnControllerStateChanged;
            _controller.OnGameFinished += OnGameFinished;
            _controller.OnAttemptsChanged += UpdateAttemptsText;
        }

        // The controller normally clears these in Dispose first, but a view torn down on its own
        // must not leave handlers behind either.
        private void OnDestroy()
        {
            CancelFill();

            // Now rather than at end of frame: Object.Destroy is deferred, so an instance the pool
            // is about to destroy goes on listening for the rest of this frame, and the models
            // outlive the view.
            foreach (ChestsMinigameChestElementView instance in _chestInstances)
            {
                if (instance != null) instance.Release();
            }
            _chestInstances.Clear();

            // The chest prefab lives in the chests bundle and MinigameContainer.End releases that,
            // so an instance outliving this view would be holding assets that can be unloaded.
            _pool?.Dispose();
            _pool = null;

            if (_controller == null) return;

            _controller.OnStateChange -= OnControllerStateChanged;
            _controller.OnGameFinished -= OnGameFinished;
            _controller.OnAttemptsChanged -= UpdateAttemptsText;
        }

        private void OnControllerStateChanged(ChestsMinigameController.State state)
        {
            if (state == ChestsMinigameController.State.Playing)
            {
                StartGame();
            }
        }

        // Every new game hands the whole board back and takes it again, rather than keeping the
        // board it built the first time - see docs/design-decisions.md for why that trade changed.
        private void StartGame()
        {
            RebuildBoard();

            UpdateAttemptsText();
            SetControlMessage(null);
        }

        private void RebuildBoard()
        {
            CancelFill();
            ReleaseBoard();

            // One source per fill, cancelled by the next one and by teardown, and linked to the
            // destroy token so a view torn down mid-fill unwinds instead of filling into a board
            // that is going away.
            _fillCancellation = CancellationTokenSource.CreateLinkedTokenSource(this.GetCancellationTokenOnDestroy());
            FillBoardAsync(_fillCancellation.Token).Forget();
        }

        // Through the budgeted loop rather than in one go, so a large board costs a few cheap frames
        // instead of one long one.
        private async UniTaskVoid FillBoardAsync(CancellationToken cancellationToken)
        {
            try
            {
                await _fill.RunAsync(_controller.Chests.Count, AcquireChest, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Deliberately nothing, and handing back what a cancelled fill had already placed
                // would be wrong. The only two things that cancel a fill are the next fill, which
                // releases the whole board before it starts anyway, and teardown, where this
                // resumes after OnDestroy has disposed the pool. Nothing promises an order between
                // OnDestroy and the destroy token, so a release from here would hit a disposed pool
                // or destroyed instances.
            }
        }

        private void AcquireChest(int index)
        {
            ChestsMinigameChestElementView instance = _pool.Get(_chestsParent);
            instance.Init(_controller.Chests[index], _controller.OnChestClicked);

            _chestInstances.Add(instance);
        }

        // The model goes first and the instance second. A parked instance is still alive - under
        // ParkedPool not even deactivated - so one handed back still holding its model would go on
        // following a chest it is no longer showing.
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

        // The bound is the board size: the board is handed back whole and taken again whole, so
        // there is never more than one board's worth to park. The holder goes under this view rather
        // than under _chestsParent, which carries the GridLayoutGroup - see PoolFactory.CreateHolder
        // for why parking under a layout group is most of the cost pooling was meant to remove.
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
            _attemptsText.text = empty ? "" : $"Attempts: {_controller.Attempts} / {_controller.TotalAttempts}";
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

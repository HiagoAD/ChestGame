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
        // How much of a frame the board fill may spend before it yields. Small enough that a fill
        // never reads as a hitch, large enough that a board this size still lands within a frame or
        // two on a phone. A constant rather than an authored field: nothing is measured yet, and a
        // serialized zero would be a board that takes one frame per chest.
        private const double FillBudgetMilliseconds = 2d;

        [SerializeField] private ChestsMinigameChestElementView _chestPrefab;
        [SerializeField] private Transform _chestsParent;
        [SerializeField] private TextMeshProUGUI _attemptsText;
        [SerializeField] private TextMeshProUGUI _controlMessage;

        // ActivationPool because it is the conventional answer, not because it measured best. What
        // should argue for changing it is a comparison, not a guess made before there was one.
        [SerializeField] private PoolStrategy _poolStrategy = PoolStrategy.ActivationPool;

        private readonly List<ChestsMinigameChestElementView> _chestInstances = new();

        private IGameClock _clock;
        private IPrefabPool<ChestsMinigameChestElementView> _pool;
        private FrameBudgetedLoop _fill;
        private CancellationTokenSource _fillCancellation;
        private ChestsMinigameController _controller;

        // The view is instantiated through the resolver, so it is injected the way everything else
        // in the game is. The clock is here rather than on the controller's because the fill is this
        // view's, and it is the seam that lets a test decide when a frame happens.
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

            // Here rather than in Awake because the bound is the board size and that comes off the
            // controller. Injection has landed by now too: the container instantiates and injects
            // this view before it hands the controller over.
            _pool = CreatePool(_controller.Chests.Count);

            // Built here rather than per fill so a view that was never injected says so at the one
            // moment a developer is looking, instead of from inside a forgotten async task later.
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

            // Now rather than at end of frame. Object.Destroy is deferred, so an instance the pool
            // is about to destroy goes on living and goes on listening for the rest of this frame,
            // and the models outlive the whole view.
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

        // Every new game hands the whole board back and takes it again. The rebuild used to be
        // skipped because it was expensive; making it cheap is what the pool is for, and a rebuild
        // that never happens is a saving nobody can measure.
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
            // that is going away. The shape mirrors the controller's opening token.
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
                // Deliberately nothing, and this is the part worth being explicit about. Handing
                // back what a cancelled fill had already placed is the obvious move and it is the
                // wrong one: the only two things that cancel a fill are the next fill, which
                // releases the whole board before it starts anyway, and teardown, where this resumes
                // after OnDestroy has already disposed the pool. Nothing promises an order between
                // OnDestroy and the destroy token, so a release from here would be a release against
                // a disposed pool, which throws, or against destroyed instances, which is worse.
            }
        }

        private void AcquireChest(int index)
        {
            ChestsMinigameChestElementView instance = _pool.Get(_chestsParent);
            instance.Init(_controller.Chests[index], _controller.OnChestClicked);

            _chestInstances.Add(instance);
        }

        // The model goes first and the instance second. A parked instance is still alive - under
        // ParkedPool it is not even deactivated - so one handed back still holding its model would
        // go on following a chest it is no longer showing.
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
        // there is never more than one board's worth to park.
        private IPrefabPool<ChestsMinigameChestElementView> CreatePool(int boardSize)
        {
            // The baseline holds nothing between a release and the next get, so it needs nowhere to
            // hold it. Building a holder for it anyway would leave an empty object in the hierarchy
            // saying this screen parks something.
            if (_poolStrategy == PoolStrategy.DirectSpawner)
            {
                return new DirectSpawner<ChestsMinigameChestElementView>(_chestPrefab);
            }

            Transform holder = CreatePoolHolder();

            switch (_poolStrategy)
            {
                case PoolStrategy.ParkedPool:
                    return new ParkedPool<ChestsMinigameChestElementView>(_chestPrefab, holder, boardSize);
                case PoolStrategy.UnityPool:
                    return new UnityPool<ChestsMinigameChestElementView>(_chestPrefab, holder, boardSize);

                // The fall-through is the default strategy rather than a throw, so a field left on an
                // enum member this switch has not heard of still comes up with a working board.
                default:
                    return new ActivationPool<ChestsMinigameChestElementView>(_chestPrefab, holder, boardSize);
            }
        }

        // Built at runtime, and deliberately not under _chestsParent: that carries the
        // GridLayoutGroup, and parking under a layout group makes every release and every get dirty
        // a rebuild, which is most of what pooling was supposed to save.
        //
        // Hidden with a Canvas component switched off. Deactivating it is not available at all -
        // ParkedPool refuses an inactive holder, because the hierarchy would deactivate everything
        // parked under it and fire exactly the OnDisable that pool exists to avoid. Moving it
        // off-screen would work, but it leaves the parked instances inside the parent canvas's batch
        // and rebuilding with it, and it rests on a coordinate no resolution can be trusted to keep
        // off the screen. A disabled Canvas draws nothing, keeps every GameObject under it active,
        // and cuts the subtree out of the canvas above. It carries no GraphicRaycaster either, so
        // nothing parked under it can be clicked.
        private Transform CreatePoolHolder()
        {
            GameObject holder = new("ChestPool", typeof(RectTransform), typeof(Canvas));

            // Under this view rather than at the scene root, so the holder and anything still parked
            // in it die with the view instead of outliving the bundle the prefab came from.
            holder.transform.SetParent(transform, false);
            holder.GetComponent<Canvas>().enabled = false;

            return holder.transform;
        }

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

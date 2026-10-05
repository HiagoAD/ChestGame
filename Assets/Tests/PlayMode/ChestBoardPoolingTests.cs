using System.Collections;
using System.Reflection;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Pooling;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Turns the board-is-rebuilt-from-scratch claim into an assertion: the same view, the same
    /// rounds, one serialized field different, and a count of how many chest objects the engine
    /// actually had to build.
    /// </summary>
    /// <remarks>
    /// See docs/design-decisions.md, "14. Pooling, and why the board is rebuilt rather than kept".
    /// See docs/testing.md, "What lives where".
    /// </remarks>
    public class ChestBoardPoolingTests
    {
        private const int BoardSize = 6;
        private const int Rounds = 3;

        /// <remarks>
        /// See docs/minigames.md, "The board is rebuilt every game".
        /// </remarks>
        private const int SettleFrames = BoardSize + 4;

        private GameObject _prefabObject;
        private GameObject _viewObject;
        private ChestsMinigameController _controller;

        /// <remarks>
        /// See docs/saving.md, "InMemoryStore".
        /// See docs/testing.md, "What lives where".
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            _controller = new ChestsMinigameController();
            _controller.Configure(ChestsMinigameConfig.Create(
                chestCount: BoardSize, attempsCount: BoardSize, timeToOpenChestMiliseconds: 1000));

            ISaveService saveService = new SaveService(new JsonCodec(), new NoProtection(), new InMemoryStore());
            _controller.Inject(new FakeRewardsManager(), new FakeRandomProvider(), new UnityGameClock(), saveService, new SaveFlushRegistry());
        }

        [TearDown]
        public void TearDown()
        {
            _controller.Dispose();

            if (_viewObject != null) Object.Destroy(_viewObject);
            if (_prefabObject != null) Object.Destroy(_prefabObject);
        }

        [UnityTest]
        public IEnumerator UnderAPool_ReplayingTheBoardReusesTheSameChests()
        {
            BuildView(PoolStrategy.ActivationPool);

            yield return PlayRounds(Rounds);

            Assert.AreEqual(BoardSize, SpawnProbe.Instantiations,
                $"three rounds off one board: a pool that built {BoardSize * Rounds} chest objects is not pooling, it is a pool-shaped object that spawns");
            Assert.AreEqual(BoardSize, LiveChestsUnderTheView(),
                "and it has to be holding exactly one board, not a board per round it forgot to hand back");
        }

        /// <remarks>
        /// See docs/design-decisions.md, "14. Pooling, and why the board is rebuilt rather than kept".
        /// </remarks>
        [UnityTest]
        public IEnumerator UnderTheBaseline_ReplayingTheBoardRebuildsItEveryTime()
        {
            BuildView(PoolStrategy.DirectSpawner);

            yield return PlayRounds(Rounds);

            Assert.AreEqual(BoardSize * Rounds, SpawnProbe.Instantiations,
                "the baseline instantiates on every get, and that is exactly the cost the pool above removes");
            Assert.AreEqual(BoardSize, LiveChestsUnderTheView(),
                "it destroys what it releases, so it leaks nothing either: the difference between the two is what a round costs, not what it leaves behind");
        }

        /// <remarks>
        /// See docs/minigames.md, "A chest has two lifetimes now".
        /// See docs/design-decisions.md, "14. Pooling, and why the board is rebuilt rather than kept".
        /// </remarks>
        [UnityTest]
        public IEnumerator AfterARebuild_EachChestModelStillDrivesExactlyOneView()
        {
            BuildView(PoolStrategy.ActivationPool);

            yield return PlayRounds(2);

            _controller.Chests[0].SetOpening(0.5f);

            Assert.AreEqual(1, ChestsShowingTheirTimer(),
                "one chest opened, so one chest on the board shows a timer; two means a reused view is still following the chest it used to show");
        }

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// See docs/design-decisions.md, "14. Pooling, and why the board is rebuilt rather than kept".
        /// </remarks>
        private ChestsMinigameView BuildView(PoolStrategy strategy)
        {
            ChestsMinigameChestElementView prefab = BuildChestPrefab();

            _viewObject = new GameObject("ChestsView", typeof(RectTransform), typeof(Canvas));
            _viewObject.SetActive(false);

            ChestsMinigameView view = _viewObject.AddComponent<ChestsMinigameView>();
            Set(view, "_chestPrefab", prefab);
            Set(view, "_chestsParent", AddChild<RectTransform>(_viewObject, "Board"));
            Set(view, "_attemptsText", AddChild<TextMeshProUGUI>(_viewObject, "Attempts"));
            Set(view, "_controlMessage", AddChild<TextMeshProUGUI>(_viewObject, "Message"));
            Set(view, "_poolStrategy", strategy);

            _viewObject.SetActive(true);

            view.Inject(new UnityGameClock());
            view.SetController(_controller);

            SpawnProbe.Instantiations = 0;
            return view;
        }

        /// <remarks>
        /// See docs/design-decisions.md, "What the tests do and do not prove".
        /// </remarks>
        private ChestsMinigameChestElementView BuildChestPrefab()
        {
            _prefabObject = new GameObject("ChestPrefab", typeof(RectTransform));
            _prefabObject.SetActive(false);

            ChestsMinigameChestElementView chest = _prefabObject.AddComponent<ChestsMinigameChestElementView>();
            _prefabObject.AddComponent<SpawnProbe>();

            Set(chest, "_chestImage", AddChild<Image>(_prefabObject, "Image"));
            Set(chest, "_timerSlider", AddChild<Slider>(_prefabObject, "Slider"));
            Set(chest, "_button", AddChild<Button>(_prefabObject, "Button"));

            _prefabObject.SetActive(true);
            return chest;
        }

        private IEnumerator PlayRounds(int rounds)
        {
            for (int round = 0; round < rounds; round++)
            {
                _controller.NewGame();

                for (int frame = 0; frame < SettleFrames; frame++) yield return null;
            }
        }

        /// <remarks>
        /// See docs/design-decisions.md, "Why ParkedPool is the default".
        /// </remarks>
        private int LiveChestsUnderTheView() =>
            _viewObject.GetComponentsInChildren<ChestsMinigameChestElementView>(true).Length;

        private int ChestsShowingTheirTimer()
        {
            int showing = 0;
            foreach (ChestsMinigameChestElementView chest in _viewObject.GetComponentsInChildren<ChestsMinigameChestElementView>(true))
            {
                Slider slider = (Slider)typeof(ChestsMinigameChestElementView)
                    .GetField("_timerSlider", BindingFlags.NonPublic | BindingFlags.Instance)
                    .GetValue(chest);

                if (slider.gameObject.activeSelf) showing++;
            }
            return showing;
        }

        private static TComponent AddChild<TComponent>(GameObject parent, string name) where TComponent : Component
        {
            GameObject child = new(name, typeof(TComponent));
            child.transform.SetParent(parent.transform, false);
            return child.GetComponent<TComponent>();
        }

        private static void Set(object target, string fieldName, object value) =>
            target.GetType()
                .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance)
                .SetValue(target, value);

        /// <remarks>
        /// See docs/design-decisions.md, "What the tests do and do not prove".
        /// </remarks>
        public class SpawnProbe : MonoBehaviour
        {
            public static int Instantiations;

            private void Awake() => Instantiations++;
        }
    }
}

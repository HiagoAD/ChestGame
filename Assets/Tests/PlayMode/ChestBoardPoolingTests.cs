using System.Collections;
using System.Reflection;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Pooling;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Company.ChestGame.Tests.PlayMode
{
    // The board is rebuilt from scratch on every new game, which is what pooling pays for. This
    // turns that claim into an assertion: the same view, the same rounds, one serialized field
    // different, and a count of how many chest objects the engine actually had to build.
    //
    // Play mode rather than edit mode because Object.Destroy is deferred to end of frame, and "how
    // many of these exist" has a wrong answer until it has landed. Every count is read after the
    // fill and the destroys have settled.
    public class ChestBoardPoolingTests
    {
        private const int BoardSize = 6;
        private const int Rounds = 3;

        // The worst a time-budgeted fill can do is one chest a frame, which a cold first frame after
        // a domain reload can actually produce, plus room for the deferred destroys to land.
        private const int SettleFrames = BoardSize + 4;

        private GameObject _prefabObject;
        private GameObject _viewObject;
        private ChestsMinigameController _controller;

        [SetUp]
        public void SetUp()
        {
            _controller = new ChestsMinigameController();
            _controller.Configure(ChestsMinigameConfig.Create(
                chestCount: BoardSize, attempsCount: BoardSize, timeToOpenChestMiliseconds: 1000));
            _controller.Inject(new FakeRewardsManager(), new FakeRandomProvider(), new UnityGameClock());
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

        [UnityTest]
        public IEnumerator UnderTheBaseline_ReplayingTheBoardRebuildsItEveryTime()
        {
            // What makes the number above mean anything: if this ever comes back equal to a single
            // board, the comparison rests on a pool measured against a pool.
            BuildView(PoolStrategy.DirectSpawner);

            yield return PlayRounds(Rounds);

            Assert.AreEqual(BoardSize * Rounds, SpawnProbe.Instantiations,
                "the baseline instantiates on every get, and that is exactly the cost the pool above removes");
            Assert.AreEqual(BoardSize, LiveChestsUnderTheView(),
                "it destroys what it releases, so it leaks nothing either: the difference between the two is what a round costs, not what it leaves behind");
        }

        [UnityTest]
        public IEnumerator AfterARebuild_EachChestModelStillDrivesExactlyOneView()
        {
            // Why a released view has to let go of its model rather than rely on being destroyed.
            // The pool hands instances back in reverse order, so an instance that kept its old
            // subscription shows one chest while still listening to another, and one model then
            // lights up two views.
            BuildView(PoolStrategy.ActivationPool);

            yield return PlayRounds(2);

            _controller.Chests[0].SetOpening(0.5f);

            Assert.AreEqual(1, ChestsShowingTheirTimer(),
                "one chest opened, so one chest on the board shows a timer; two means a reused view is still following the chest it used to show");
        }

        // --- The rig ------------------------------------------------------------------------

        private ChestsMinigameView BuildView(PoolStrategy strategy)
        {
            ChestsMinigameChestElementView prefab = BuildChestPrefab();

            // A Canvas because this is a uGUI screen and TextMeshProUGUI expects to live under one.
            _viewObject = new GameObject("ChestsView", typeof(RectTransform), typeof(Canvas));
            _viewObject.SetActive(false);

            ChestsMinigameView view = _viewObject.AddComponent<ChestsMinigameView>();
            Set(view, "_chestPrefab", prefab);
            Set(view, "_chestsParent", AddChild<RectTransform>(_viewObject, "Board"));
            Set(view, "_attemptsText", AddChild<TextMeshProUGUI>(_viewObject, "Attempts"));
            Set(view, "_controlMessage", AddChild<TextMeshProUGUI>(_viewObject, "Message"));
            Set(view, "_poolStrategy", strategy);

            _viewObject.SetActive(true);

            // The order the container uses: the resolver instantiates and injects the view, then the
            // controller is handed over, then the shell starts a game.
            view.Inject(new UnityGameClock());
            view.SetController(_controller);

            // After the rig is standing, so the one Awake the source object ran on its own is not
            // counted as something the board built.
            SpawnProbe.Instantiations = 0;
            return view;
        }

        private ChestsMinigameChestElementView BuildChestPrefab()
        {
            _prefabObject = new GameObject("ChestPrefab", typeof(RectTransform));
            _prefabObject.SetActive(false);

            ChestsMinigameChestElementView chest = _prefabObject.AddComponent<ChestsMinigameChestElementView>();
            _prefabObject.AddComponent<SpawnProbe>();

            Set(chest, "_chestImage", AddChild<Image>(_prefabObject, "Image"));
            Set(chest, "_timerSlider", AddChild<Slider>(_prefabObject, "Slider"));
            Set(chest, "_button", AddChild<Button>(_prefabObject, "Button"));

            // Left active, like the real prefab's root: an inactive source would measure the rig
            // rather than the pools.
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

        // Everything under the view: the board and whatever is parked in the pool's holder, which is
        // a child of the view too. Inactive included, because that is how ActivationPool parks.
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

        // Counts how many chest objects the engine was actually asked to build. A pool's own
        // CreatedCount would only prove a field moved; an Awake proves Instantiate ran.
        public class SpawnProbe : MonoBehaviour
        {
            public static int Instantiations;

            private void Awake() => Instantiations++;
        }
    }
}

using System.Collections;
using Company.ChestGame.Common;
using Company.ChestGame.Pooling;
using Company.ChestGame.Pooling.Demo;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <remarks>
    /// See docs/pooling.md, "What only a real engine proves about pooling".
    /// </remarks>
    public class PoolRacePlayModeTests
    {
        private const double BudgetMilliseconds = 2d;
        private const int BoardSize = 12;
        private const int SettleFrames = BoardSize + 10;

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            if (_root != null) Object.Destroy(_root);
        }

        /// <remarks>
        /// See docs/pooling.md, "What only a real engine proves about pooling".
        /// </remarks>
        [UnityTest]
        public IEnumerator StartRace_AllFour_EachLaneEndsWithTheRequestedCount_CountedByARealProbe()
        {
            _root = new GameObject("Root", typeof(RectTransform), typeof(Canvas));

            Transform[] laneRoots =
            {
                NewChild("ActivationRoot"), NewChild("ParkedRoot"), NewChild("UnityPoolRoot"), NewChild("DirectRoot")
            };

            GameObject prefabObject = new("Prefab", typeof(RectTransform));
            SpawnProbe prefab = prefabObject.AddComponent<SpawnProbe>();

            PoolRaceLane<SpawnProbe>[] lanes = PoolRaceLaneFactory.BuildAll(prefab, laneRoots, maxSize: 50);
            PoolRace<SpawnProbe> race = new(lanes, new UnityGameClock(), BudgetMilliseconds);

            SpawnProbe.Instantiations = 0;

            race.StartRace(BoardSize, FillMode.Cold, solo: false, PoolStrategy.ActivationPool);

            for (int frame = 0; frame < SettleFrames && !race.LastResult.HasValue; frame++) yield return null;

            Assert.IsTrue(race.LastResult.HasValue, "the race did not settle within the frames allotted");

            Assert.AreEqual(BoardSize * lanes.Length, SpawnProbe.Instantiations,
                "a cold race across four lanes has to instantiate the full board on every lane - fewer real Awake calls than that means a lane silently reused something it should have built fresh");

            foreach (PoolRaceLane<SpawnProbe> lane in lanes)
            {
                Assert.AreEqual(BoardSize, lane.Pool.ActiveCount, $"{lane.Strategy}'s own count disagrees with the board size it was asked to fill");
                Assert.AreEqual(BoardSize, lane.FillParent.GetComponentsInChildren<SpawnProbe>(true).Length,
                    $"{lane.Strategy} does not actually hold {BoardSize} real instances under its fill parent");
            }
        }

        /// <remarks>
        /// See docs/pooling.md, "What only a real engine proves about pooling".
        /// </remarks>
        [UnityTest]
        public IEnumerator ParkedPool_ThroughARealCanvasHolder_KeepsParkedInstancesActiveAndHidesThem()
        {
            _root = new GameObject("Root", typeof(RectTransform), typeof(Canvas));
            Transform laneRoot = NewChild("ParkedLaneRoot");

            GameObject prefabObject = new("Prefab", typeof(RectTransform));
            SpawnProbe prefab = prefabObject.AddComponent<SpawnProbe>();

            PoolRaceLane<SpawnProbe> lane = PoolRaceLaneFactory.Build(PoolStrategy.ParkedPool, prefab, laneRoot, maxSize: 10);

            SpawnProbe a = lane.Pool.Get(lane.FillParent);
            SpawnProbe b = lane.Pool.Get(lane.FillParent);
            Assert.IsTrue(a.gameObject.activeSelf, "guard: a handed-out instance starts active");

            lane.Pool.Release(a);
            lane.Pool.Release(b);

            yield return null;

            Transform holder = a.transform.parent;
            Assert.AreEqual(holder, b.transform.parent, "both released instances should park under the same holder");

            Canvas holderCanvas = holder.GetComponent<Canvas>();
            Assert.IsNotNull(holderCanvas,
                "ParkedPool's holder is built as a Canvas switched off, the way ChestsMinigameView.CreatePoolHolder builds its own");
            Assert.IsFalse(holderCanvas.enabled,
                "the holder's Canvas has to be disabled - that is the only thing hiding a parked instance without deactivating it");

            Assert.IsTrue(a.gameObject.activeSelf,
                "a parked instance must stay active - that is the entire difference ParkedPool exists to demonstrate over ActivationPool");
            Assert.IsTrue(b.gameObject.activeSelf);
        }

        private Transform NewChild(string name)
        {
            GameObject go = new(name, typeof(RectTransform));
            go.transform.SetParent(_root.transform, false);
            return go.transform;
        }

        /// <summary>
        /// Counts how many instances the engine actually constructed, the same technique as
        /// <see cref="ChestBoardPoolingTests.SpawnProbe"/>.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "What only a real engine proves about pooling".
        /// </remarks>
        public class SpawnProbe : MonoBehaviour
        {
            public static int Instantiations;

            private void Awake() => Instantiations++;
        }
    }
}

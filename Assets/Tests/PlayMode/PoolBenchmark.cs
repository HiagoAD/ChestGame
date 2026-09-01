using System.Collections;
using System.Diagnostics;
using System.Text;
using Company.ChestGame.Pooling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    // A measurement, not a behaviour test. Everything this suite asserts about pooling is a count,
    // because a count is exact and a stopwatch on a shared CI machine is not - a timing assertion
    // would be the flaky test docs/testing.md's play-mode rule exists to prevent.
    //
    // But "pooling is worth doing" is a claim about time, so this runs the real strategies against a
    // real prefab on the real player loop and writes the numbers to the log for a human to quote
    // into docs/design-decisions.md. The only assertions are deterministic ones: what a rebuild
    // instantiates, which is the mechanism the timings follow from.
    public class PoolBenchmark
    {
        // Big enough that the difference is real rather than noise, small enough that the play-mode
        // suite stays the thin layer docs/testing.md says it is.
        private const int BoardSize = 500;

        private GameObject _prefabObject;
        private GameObject _holderObject;
        private GameObject _parentObject;

        [SetUp]
        public void SetUp()
        {
            // Shaped like the real chest prefab rather than a bare Transform: an Image, a Slider and
            // a Button under a root. What Instantiate costs is a whole object graph, so a
            // one-component prefab would flatter the baseline.
            _prefabObject = new GameObject("BenchChest", typeof(RectTransform), typeof(Image));
            AddChild<Image>(_prefabObject, "Icon");
            AddChild<Slider>(_prefabObject, "Timer");
            AddChild<Button>(_prefabObject, "Hit");

            _holderObject = new GameObject("Holder", typeof(RectTransform), typeof(Canvas));
            _holderObject.GetComponent<Canvas>().enabled = false;

            _parentObject = new GameObject("Board", typeof(RectTransform));
        }

        [TearDown]
        public void TearDown()
        {
            if (_prefabObject != null) Object.Destroy(_prefabObject);
            if (_holderObject != null) Object.Destroy(_holderObject);
            if (_parentObject != null) Object.Destroy(_parentObject);
        }

        private static void AddChild<TComponent>(GameObject parent, string name) where TComponent : Component
        {
            GameObject child = new(name, typeof(TComponent));
            child.transform.SetParent(parent.transform, false);
        }

        private RectTransform Prefab => (RectTransform)_prefabObject.transform;
        private Transform Holder => _holderObject.transform;
        private Transform Parent => _parentObject.transform;

        [UnityTest]
        public IEnumerator Measure_WhatARebuildCostsUnderEachStrategy()
        {
            StringBuilder report = new();
            report.AppendLine($"pool benchmark: {BoardSize} instances per fill, Unity {Application.unityVersion}");
            report.AppendLine("strategy        first fill (ms)   rebuild (ms)   rebuild instantiates");

            yield return MeasureOne(PoolStrategy.DirectSpawner, report);
            yield return MeasureOne(PoolStrategy.ActivationPool, report);
            yield return MeasureOne(PoolStrategy.ParkedPool, report);
            yield return MeasureOne(PoolStrategy.UnityPool, report);

            // The whole point of the class: a human reads this out of the run log and quotes it.
            Debug.Log(report.ToString());
        }

        // First fill is every strategy's cold case and no pool should be expected to win it. The
        // rebuild is what the game does on every NewGame, and where a pool stops paying Instantiate.
        private IEnumerator MeasureOne(PoolStrategy strategy, StringBuilder report)
        {
            IPrefabPool<RectTransform> pool = Build(strategy);
            RectTransform[] placed = new RectTransform[BoardSize];

            // A frame of its own before each measured stretch, so the previous strategy's deferred
            // destroys have landed and are not being timed as part of this one.
            yield return null;

            Stopwatch firstFill = Stopwatch.StartNew();
            for (int i = 0; i < BoardSize; i++) placed[i] = pool.Get(Parent);
            firstFill.Stop();

            yield return null;

            int createdBeforeRebuild = pool.CreatedCount;

            Stopwatch rebuild = Stopwatch.StartNew();
            for (int i = 0; i < BoardSize; i++) pool.Release(placed[i]);
            for (int i = 0; i < BoardSize; i++) placed[i] = pool.Get(Parent);
            rebuild.Stop();

            int instantiatedByRebuild = pool.CreatedCount - createdBeforeRebuild;

            report.AppendLine(
                $"{strategy,-15} {firstFill.Elapsed.TotalMilliseconds,13:F1}   {rebuild.Elapsed.TotalMilliseconds,12:F1}   {instantiatedByRebuild,20}");

            // The deterministic half, and the only thing asserted: a pool that stops calling
            // Instantiate on a rebuild is the whole mechanism behind the timings, and unlike a
            // stopwatch it cannot come out differently on a slow machine.
            int expected = strategy == PoolStrategy.DirectSpawner ? BoardSize : 0;
            Assert.AreEqual(expected, instantiatedByRebuild,
                $"{strategy} instantiated {instantiatedByRebuild} on a rebuild of an already-filled board");

            pool.Dispose();
            yield return null;
        }

        private IPrefabPool<RectTransform> Build(PoolStrategy strategy) => strategy switch
        {
            PoolStrategy.DirectSpawner => new DirectSpawner<RectTransform>(Prefab),
            PoolStrategy.ParkedPool => new ParkedPool<RectTransform>(Prefab, Holder, BoardSize),
            PoolStrategy.UnityPool => new UnityPool<RectTransform>(Prefab, Holder, BoardSize),
            _ => new ActivationPool<RectTransform>(Prefab, Holder, BoardSize)
        };
    }
}

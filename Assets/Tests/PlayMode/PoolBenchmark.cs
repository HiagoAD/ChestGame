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
    /// <summary>
    /// Measures what a board rebuild costs under each pool strategy and logs the numbers for
    /// <c>docs/design-decisions.md</c>.
    /// </summary>
    /// <remarks>
    /// See docs/design-decisions.md, "What the tests do and do not prove".
    /// See docs/pooling.md, "What PoolBenchmark measures and why".
    /// </remarks>
    public class PoolBenchmark
    {
        private const int BoardSize = 500;

        private GameObject _prefabObject;
        private GameObject _holderObject;
        private GameObject _parentObject;

        /// <remarks>
        /// See docs/pooling.md, "What PoolBenchmark measures and why".
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
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

            Debug.Log(report.ToString());
        }

        /// <remarks>
        /// See docs/design-decisions.md, "14. Pooling, and why the board is rebuilt rather than kept".
        /// See docs/pooling.md, "What PoolBenchmark measures and why".
        /// </remarks>
        private IEnumerator MeasureOne(PoolStrategy strategy, StringBuilder report)
        {
            IPrefabPool<RectTransform> pool = Build(strategy);
            RectTransform[] placed = new RectTransform[BoardSize];

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

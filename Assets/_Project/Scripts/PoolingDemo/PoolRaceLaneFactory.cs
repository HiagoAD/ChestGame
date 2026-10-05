using UnityEngine;
using UnityEngine.UI;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// Builds the four real lanes a race needs from a prefab, each with its own holder and its own
    /// fill parent.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "PoolRaceLaneFactory, and why the fill parent is not the holder".
    /// </remarks>
    public static class PoolRaceLaneFactory
    {
        /// <remarks>
        /// Order is fixed and matches the demo's lane columns.
        /// See docs/pooling.md, "PoolRaceLaneFactory, and why the fill parent is not the holder".
        /// </remarks>
        public static readonly PoolStrategy[] AllStrategies =
        {
            PoolStrategy.ActivationPool,
            PoolStrategy.ParkedPool,
            PoolStrategy.UnityPool,
            PoolStrategy.DirectSpawner
        };

        private const int ColumnsPerLane = 20;
        private const float CellSize = 12f;

        /// <summary>
        /// Builds one lane per entry in <see cref="AllStrategies"/>.
        /// </summary>
        /// <param name="prefab">Prefab every lane fills with.</param>
        /// <param name="laneRoots">
        /// Must have the same length as <see cref="AllStrategies"/>. <c>laneRoots[i]</c> is where
        /// <c>AllStrategies[i]</c>'s holder and fill parent are built.
        /// </param>
        /// <param name="maxSize">Maximum size passed to each lane's pool.</param>
        public static PoolRaceLane<T>[] BuildAll<T>(T prefab, Transform[] laneRoots, int maxSize) where T : Component
        {
            PoolRaceLane<T>[] lanes = new PoolRaceLane<T>[AllStrategies.Length];
            for (int i = 0; i < AllStrategies.Length; i++)
            {
                lanes[i] = Build(AllStrategies[i], prefab, laneRoots[i], maxSize);
            }
            return lanes;
        }

        public static PoolRaceLane<T> Build<T>(PoolStrategy strategy, T prefab, Transform laneRoot, int maxSize) where T : Component
        {
            Transform fillParent = CreateFillParent(laneRoot);
            IPrefabPool<T> pool = PoolFactory.Create(strategy, prefab, laneRoot, maxSize, "Holder");

            return new PoolRaceLane<T>(strategy, pool, fillParent);
        }

        /// <remarks>
        /// See docs/pooling.md, "PoolRaceLaneFactory, and why the fill parent is not the holder".
        /// </remarks>
        private static Transform CreateFillParent(Transform laneRoot)
        {
            GameObject fillParent = new("Fill", typeof(RectTransform), typeof(GridLayoutGroup));
            fillParent.transform.SetParent(laneRoot, false);

            RectTransform fillRect = (RectTransform)fillParent.transform;
            fillRect.anchorMin = Vector2.zero;
            fillRect.anchorMax = Vector2.one;
            fillRect.offsetMin = Vector2.zero;
            fillRect.offsetMax = Vector2.zero;

            GridLayoutGroup grid = fillParent.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(CellSize, CellSize);
            grid.spacing = new Vector2(1f, 1f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = ColumnsPerLane;

            return fillParent.transform;
        }
    }
}

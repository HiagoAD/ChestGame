using UnityEngine;
using UnityEngine.UI;

namespace Company.ChestGame.Pooling.Demo
{
    // Builds the four real lanes a race needs from a prefab and, for each lane, the transform it is
    // allowed to build under. Every lane gets its own holder and its own fill parent, so no lane's
    // pool can dirty another lane's layout by parking into it.
    //
    // The pool and its holder both come from PoolFactory, the same call ChestsMinigameView makes.
    // The fill parent is this class's own: it carries a GridLayoutGroup, which is what turns a
    // lane's growth into visible motion, and it must never be the holder, or parking would dirty a
    // rebuild no race needs to pay for.
    public static class PoolRaceLaneFactory
    {
        // Fixed so the UI and the pools agree on which strategy sits in which column.
        public static readonly PoolStrategy[] AllStrategies =
        {
            PoolStrategy.ActivationPool,
            PoolStrategy.ParkedPool,
            PoolStrategy.UnityPool,
            PoolStrategy.DirectSpawner
        };

        private const int ColumnsPerLane = 20;
        private const float CellSize = 12f;

        // laneRoots has to align with AllStrategies: laneRoots[i] is where AllStrategies[i]'s holder
        // and fill parent are built.
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

        private static Transform CreateFillParent(Transform laneRoot)
        {
            GameObject fillParent = new("Fill", typeof(RectTransform), typeof(GridLayoutGroup));
            fillParent.transform.SetParent(laneRoot, false);

            // Stretched rather than left at its default centered rect: laneRoot is the masked slot
            // the panel built and carries no layout group, so nothing else would give this a width
            // to lay a grid out inside.
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

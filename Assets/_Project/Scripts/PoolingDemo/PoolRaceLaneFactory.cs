using UnityEngine;
using UnityEngine.UI;

namespace Company.ChestGame.Pooling.Demo
{
    // Builds the four real lanes a race needs from nothing but a prefab and, for each lane, the
    // transform it is allowed to build under. Every lane gets its own holder and its own fill parent,
    // so no lane's pool can dirty another lane's layout by parking into it, and no lane's growth can
    // be mistaken for another's.
    //
    // The holder mirrors ChestsMinigameView.CreatePoolHolder exactly: a child Canvas switched off,
    // because ParkedPool refuses an inactive holder outright and a disabled Canvas is the one way to
    // hide a subtree without deactivating anything under it. The fill parent carries a
    // GridLayoutGroup instead, on purpose - that is what turns a lane's growth into visible motion -
    // and it must never be the holder, or parking would dirty a rebuild for no reason a race needs to
    // pay.
    public static class PoolRaceLaneFactory
    {
        // Fixed so the UI and the pools agree on which strategy sits in which column without either
        // side having to ask the other.
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
            IPrefabPool<T> pool = CreatePool(strategy, prefab, laneRoot, maxSize);

            return new PoolRaceLane<T>(strategy, pool, fillParent);
        }

        private static IPrefabPool<T> CreatePool<T>(PoolStrategy strategy, T prefab, Transform laneRoot, int maxSize) where T : Component
        {
            // The baseline holds nothing between a release and the next get, so it needs nowhere to
            // hold it. Building a holder for it anyway would leave an empty object in the hierarchy
            // claiming this lane parks something.
            if (strategy == PoolStrategy.DirectSpawner) return new DirectSpawner<T>(prefab);

            Transform holder = CreateHolder(laneRoot);
            return strategy switch
            {
                PoolStrategy.ParkedPool => new ParkedPool<T>(prefab, holder, maxSize),
                PoolStrategy.UnityPool => new UnityPool<T>(prefab, holder, maxSize),
                _ => new ActivationPool<T>(prefab, holder, maxSize)
            };
        }

        private static Transform CreateHolder(Transform laneRoot)
        {
            GameObject holder = new("Holder", typeof(RectTransform), typeof(Canvas));
            holder.transform.SetParent(laneRoot, false);
            holder.GetComponent<Canvas>().enabled = false;
            return holder.transform;
        }

        private static Transform CreateFillParent(Transform laneRoot)
        {
            GameObject fillParent = new("Fill", typeof(RectTransform), typeof(GridLayoutGroup));
            fillParent.transform.SetParent(laneRoot, false);

            // Stretched rather than left at its default centered rect: laneRoot has no layout group
            // of its own to size this against - it is the masked slot the panel built - so nothing
            // else would ever give it a width to lay a grid out inside.
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

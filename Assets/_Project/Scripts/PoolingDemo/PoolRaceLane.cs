using UnityEngine;

namespace Company.ChestGame.Pooling.Demo
{
    /// <summary>
    /// One lane's wiring: which strategy it demonstrates, the pool that implements it, and the
    /// transform a race parents placed instances into.
    /// </summary>
    public sealed class PoolRaceLane<T> where T : Component
    {
        public PoolStrategy Strategy { get; }
        public IPrefabPool<T> Pool { get; }
        public Transform FillParent { get; }

        public PoolRaceLane(PoolStrategy strategy, IPrefabPool<T> pool, Transform fillParent)
        {
            if (pool == null) throw PoolRaceException.NoPool();
            if (fillParent == null) throw PoolRaceException.NoFillParent();

            Strategy = strategy;
            Pool = pool;
            FillParent = fillParent;
        }
    }
}

using UnityEngine;

namespace Company.ChestGame.Pooling.Demo
{
    // One lane's wiring: which strategy it demonstrates, the pool that implements it, and the
    // transform a race parents placed instances under while it is running. The pool already carries
    // its own holder, or none at all for the baseline, so a lane needs nothing more than this to run
    // its own frame-budgeted fill independently of the other three.
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

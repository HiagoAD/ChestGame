using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Company.ChestGame.Pooling
{
    // ActivationPool's strategy over the engine's own ObjectPool instead of a hand-rolled stack.
    // Once ObjectPool owns the stack, the bound and the create/destroy callbacks, what is left here
    // is the parenting, the counters and the two rejections the seam promises. ObjectPool ships
    // with UnityEngine.CoreModule, so this costs no package reference.
    public class UnityPool<T> : IPrefabPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _holder;
        private readonly int _maxSize;
        private readonly ObjectPool<T> _pool;

        // ObjectPool's own active count reads zero after a Clear with instances still handed out,
        // because Clear resets the total it derives that from. This set has to exist anyway to
        // answer "did this pool hand that out", so ActiveCount comes from here.
        private readonly HashSet<T> _handedOut = new();

        private readonly List<T> _scratch = new();
        private bool _disposed;

        public int CreatedCount { get; private set; }
        public int DestroyedCount { get; private set; }
        public int ActiveCount => _handedOut.Count;
        public int AvailableCount => _pool.CountInactive;

        public UnityPool(T prefab, Transform holder, int maxSize)
        {
            if (prefab == null) throw PoolException.NoPrefab();
            if (holder == null) throw PoolException.NoHolder();
            if (maxSize < 1) throw PoolException.MaxSizeBelowOne(maxSize);

            _prefab = prefab;
            _holder = holder;
            _maxSize = maxSize;

            // collectionCheck stays on as a second net under this class's own release check. No
            // actionOnGet, because activating has to happen after the parent is set and ObjectPool
            // fires that callback before it hands the instance back.
            _pool = new ObjectPool<T>(Create, actionOnRelease: Park, actionOnDestroy: DestroyInstance,
                collectionCheck: true, defaultCapacity: maxSize, maxSize: maxSize);
        }

        public T Get(Transform parent)
        {
            if (_disposed) throw PoolException.Disposed();

            T instance = _pool.Get();

            // A miss here pays for a park it does not need: ObjectPool's factory callback is handed
            // no context, so Create has to park the instance and these two lines undo it.
            // ActivationPool avoids that by branching on the miss; the wrapper cannot, and
            // docs/design-decisions.md keeps what it costs visible.
            //
            // Reparent before activating, for the reason ActivationPool.Get gives.
            instance.transform.SetParent(parent, false);
            instance.gameObject.SetActive(true);

            _handedOut.Add(instance);
            return instance;
        }

        public void Release(T instance)
        {
            // This check has to be the one that reports: ObjectPool's own collection check throws a
            // bare InvalidOperationException, which names nothing. PoolException subclasses it, so
            // what a caller catches stays specific.
            //
            // Remove before the null check, not after. Unity's overloaded equality makes a
            // destroyed instance read as null, so the other order short-circuits past Remove and
            // strands the dead entry in the set forever.
            if (!_handedOut.Remove(instance) || instance == null) throw PoolException.NotHandedOut(instance);

            // Past the bound ObjectPool destroys the surplus itself, but fires actionOnRelease
            // first - so the instance gets parked, switched off and reparented, on its way to being
            // destroyed. Checking here skips that and matches what the hand-rolled pools do.
            if (_pool.CountInactive >= _maxSize)
            {
                DestroyInstance(instance);
                return;
            }

            _pool.Release(instance);
        }

        public void ReleaseAll()
        {
            // Snapshot, because Release edits the set being walked. The list is reused rather than
            // allocated per call: PoolRace.PrepareLanes does four of these per Run press.
            _scratch.Clear();
            _scratch.AddRange(_handedOut);

            // Every instance, then the first failure. A bare foreach would strand every instance
            // after the first bad entry.
            PoolException failure = null;
            foreach (T instance in _scratch)
            {
                try
                {
                    Release(instance);
                }
                catch (PoolException e)
                {
                    failure ??= e;
                }
            }

            if (failure != null) throw failure;
        }

        public void Prewarm(int count)
        {
            if (_disposed) throw PoolException.Disposed();
            if (_pool.CountInactive + count > _maxSize)
            {
                throw PoolException.PrewarmPastMaxSize(count, _pool.CountInactive, _maxSize);
            }

            // Created directly rather than taken and given back. ObjectPool.Get pops existing stock
            // before it calls the factory, so get-then-release on a pool already holding k pops
            // those k and puts them straight back, creating only count - k. The other three always
            // create.
            for (int i = 0; i < count; i++) _pool.Release(Create());
        }

        // Clear destroys everything parked through actionOnDestroy and leaves what is handed out
        // alone. To zero rather than down to max size, for the reason ActivationPool.Trim gives.
        public void Trim() => _pool.Clear();

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // Destroyed directly rather than released first, which would park them under the
            // holder only for the next line to destroy them.
            foreach (T instance in new List<T>(_handedOut)) DestroyInstance(instance);
            _handedOut.Clear();

            // Disposing clears the pool, which destroys what is parked through the same callback.
            _pool.Dispose();
        }

        private T Create()
        {
            T instance = Object.Instantiate(_prefab);
            Park(instance);

            CreatedCount++;
            return instance;
        }

        // Also ObjectPool's actionOnRelease. worldPositionStays is false, so a RectTransform keeps
        // the anchored layout it was authored with.
        private void Park(T instance)
        {
            instance.gameObject.SetActive(false);
            instance.transform.SetParent(_holder, false);
        }

        // The GameObject, not the component: destroying the component alone leaves an empty object
        // behind, and on a Transform the engine refuses outright.
        private void DestroyInstance(T instance)
        {
            // An instance handed out can be destroyed behind the pool's back, and .gameObject on a
            // destroyed Component throws MissingReferenceException.
            if (instance == null) return;

            DestroyedCount++;
            Object.Destroy(instance.gameObject);
        }
    }
}

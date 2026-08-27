using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Company.ChestGame.Pooling
{
    // ActivationPool's strategy over the engine's own ObjectPool instead of a hand-rolled stack.
    // It is here to make the point that the hand-rolled one is usually not worth writing: once
    // ObjectPool owns the stack, the bound and the create/destroy callbacks, what is left is the
    // parenting, the counters and the two rejections the seam promises. ObjectPool ships with
    // UnityEngine.CoreModule, so this costs no package reference.
    public class UnityPool<T> : IPrefabPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _holder;
        private readonly int _maxSize;
        private readonly ObjectPool<T> _pool;

        // ObjectPool tracks an active count of its own, but Clear resets the total it derives that
        // from, so reading ActiveCount off the wrapped pool would report zero after a Trim with
        // instances still out. This set has to exist anyway to answer "did this pool hand that
        // out", so the count comes from here.
        private readonly HashSet<T> _handedOut = new();

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

            // collectionCheck stays on as a second net under this class's own release check. It
            // costs a linear scan of the parked instances per release, which against a bounded UI
            // pool is a handful of reference compares, and it is what would catch a double release
            // this class somehow let through.
            //
            // There is no actionOnGet because activating has to happen after the parent is set, and
            // ObjectPool fires that callback before it hands the instance back. defaultCapacity is
            // the bound, since the stack can never hold more than that.
            _pool = new ObjectPool<T>(Create, actionOnRelease: Park, actionOnDestroy: DestroyInstance,
                collectionCheck: true, defaultCapacity: maxSize, maxSize: maxSize);
        }

        public T Get(Transform parent)
        {
            if (_disposed) throw PoolException.Disposed();

            T instance = _pool.Get();

            // A miss here pays for a park it does not need. ObjectPool's factory callback is handed
            // no context, so it cannot know whether the instance it is building is about to be
            // handed out or parked, which means Create has to park it and these two lines have to
            // undo that. ActivationPool avoids it by branching on the miss; the wrapper cannot, and
            // that is worth leaving visible when the numbers come out rather than papering over.
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
            // bare InvalidOperationException, which names nothing and which a test could only assert
            // loosely. PoolException is a subclass of that, so what a caller catches stays specific.
            if (instance == null || !_handedOut.Remove(instance)) throw PoolException.NotHandedOut(instance);

            // Past the bound ObjectPool destroys the surplus itself, through actionOnDestroy.
            _pool.Release(instance);
        }

        public void ReleaseAll()
        {
            // Snapshot, because Release edits the set being walked.
            foreach (T instance in new List<T>(_handedOut)) Release(instance);
        }

        public void Prewarm(int count)
        {
            if (_disposed) throw PoolException.Disposed();
            if (_pool.CountInactive + count > _maxSize)
            {
                throw PoolException.PrewarmPastMaxSize(count, _pool.CountInactive, _maxSize);
            }

            // ObjectPool has no prewarm, so warming is taking count instances and giving them all
            // back. All of them first: getting and releasing one at a time would hand the same
            // instance back every time and warm exactly one.
            T[] warming = new T[count];
            for (int i = 0; i < count; i++) warming[i] = _pool.Get();
            for (int i = 0; i < count; i++) _pool.Release(warming[i]);
        }

        // Clear destroys everything parked through actionOnDestroy and leaves what is handed out
        // alone, which is a trim exactly. To zero rather than down to max size, for the reason
        // ActivationPool.Trim gives.
        public void Trim() => _pool.Clear();

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            // The handed-out ones are destroyed directly rather than released first, which would
            // park them under the holder only for the next line to destroy them.
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

        // Also ObjectPool's actionOnRelease. worldPositionStays is false so a RectTransform keeps
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
            DestroyedCount++;
            Object.Destroy(instance.gameObject);
        }
    }
}

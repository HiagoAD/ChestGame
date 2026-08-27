using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // The hand-rolled pool, and the version most projects end up writing: parked instances are
    // deactivated under a holder, and a get reactivates one under the caller's parent.
    //
    // What it costs is easy to miss. Every get and every release runs OnEnable and OnDisable down
    // the whole instance and dirties the canvas and every layout group above it. ParkedPool is the
    // same idea with that removed, which is the comparison the two are here to make.
    public class ActivationPool<T> : IPrefabPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _holder;
        private readonly int _maxSize;

        private readonly Stack<T> _parked = new();
        private readonly HashSet<T> _handedOut = new();

        private bool _disposed;

        public int CreatedCount { get; private set; }
        public int DestroyedCount { get; private set; }
        public int ActiveCount => _handedOut.Count;
        public int AvailableCount => _parked.Count;

        public ActivationPool(T prefab, Transform holder, int maxSize)
        {
            if (prefab == null) throw PoolException.NoPrefab();
            if (holder == null) throw PoolException.NoHolder();
            if (maxSize < 1) throw PoolException.MaxSizeBelowOne(maxSize);

            _prefab = prefab;
            _holder = holder;
            _maxSize = maxSize;
        }

        public T Get(Transform parent)
        {
            if (_disposed) throw PoolException.Disposed();

            T instance;
            if (_parked.Count > 0)
            {
                instance = _parked.Pop();

                // Reparent before activating, so OnEnable and the first layout pass already see the
                // parent the instance is going to live under. The other order rebuilds twice and
                // shows the instance for a frame wherever it was parked.
                instance.transform.SetParent(parent, false);
                instance.gameObject.SetActive(true);
            }
            else
            {
                // A miss goes straight to the caller's parent. Building it parked first would
                // reparent it to the holder and deactivate it, only for the two lines above to undo
                // both - and the first fill of a screen is nothing but misses, which is exactly the
                // stretch the comparison against DirectSpawner measures.
                instance = Create(parent);

                // Instantiate already hands back an active clone when the prefab root is active, so
                // in the normal case this fires nothing at all. It is here so a prefab authored
                // with an inactive root still comes out visible.
                instance.gameObject.SetActive(true);
            }

            _handedOut.Add(instance);
            return instance;
        }

        public void Release(T instance)
        {
            if (instance == null || !_handedOut.Remove(instance)) throw PoolException.NotHandedOut(instance);

            // The bound is a bound. A pool that grew on every release would be a leak that reads
            // like a feature, because nothing about a pool holding too much ever looks wrong.
            if (_parked.Count >= _maxSize)
            {
                DestroyInstance(instance);
                return;
            }

            instance.gameObject.SetActive(false);
            instance.transform.SetParent(_holder, false);
            _parked.Push(instance);
        }

        public void ReleaseAll()
        {
            // Snapshot, because Release edits the set being walked.
            foreach (T instance in new List<T>(_handedOut)) Release(instance);
        }

        public void Prewarm(int count)
        {
            if (_disposed) throw PoolException.Disposed();
            if (_parked.Count + count > _maxSize) throw PoolException.PrewarmPastMaxSize(count, _parked.Count, _maxSize);

            for (int i = 0; i < count; i++) _parked.Push(CreateIdle());
        }

        // To zero rather than down to max size. Release and Prewarm both refuse to park past the
        // bound, so there is never a surplus above it for a trim-to-max-size to find, and a method
        // that cannot do anything by construction is worse than no method.
        public void Trim()
        {
            while (_parked.Count > 0) DestroyInstance(_parked.Pop());
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            foreach (T instance in new List<T>(_handedOut)) DestroyInstance(instance);
            _handedOut.Clear();
            Trim();
        }

        private T Create(Transform parent)
        {
            T instance = Object.Instantiate(_prefab);

            // Parented here rather than through the Instantiate overload, so worldPositionStays is
            // visibly false and a RectTransform keeps the anchored layout it was authored with.
            instance.transform.SetParent(parent, false);

            CreatedCount++;
            return instance;
        }

        // An instance in this pool's idle state: under the holder and switched off, which is where
        // Release leaves one too. Named for what it produces rather than for Prewarm, its only
        // caller today, so anything later wanting a spare instance is not reading someone else's
        // intent. It hands the instance back rather than pushing it, because the caller owns
        // _parked and a Create that quietly added to it would do two things under a name that
        // promises one.
        private T CreateIdle()
        {
            T instance = Create(_holder);
            instance.gameObject.SetActive(false);
            return instance;
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

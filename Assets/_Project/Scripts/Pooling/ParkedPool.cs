using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // ActivationPool with the SetActive taken out. Toggling active is what makes a pool expensive
    // under uGUI: it runs OnEnable and OnDisable down the whole instance and dirties the canvas and
    // every layout group above it, twice per reuse. Parking by reparenting alone costs one
    // hierarchy change and nothing else.
    //
    // That saving is a uGUI one, so this is the strategy to reach for under a Canvas. Away from one
    // there is much less in it, and a parked instance that still renders and still ticks is a worse
    // trade than the OnDisable it avoids: pick ActivationPool for anything in world space.
    //
    // The price is that parked instances stay live: they still render and still tick. The holder is
    // where that is paid for and it belongs to the caller, which is why this class only reparents
    // to it. Somewhere off-screen works; so does a Canvas component switched off, which hides the
    // whole subtree without deactivating a single GameObject. What the holder must not be is
    // inactive, which the constructor refuses, because a child reparented under an inactive object
    // is deactivated by the hierarchy and fires exactly the OnDisable this class exists to avoid.
    public class ParkedPool<T> : IPrefabPool<T> where T : Component
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

        public ParkedPool(T prefab, Transform holder, int maxSize)
        {
            if (prefab == null) throw PoolException.NoPrefab();
            if (holder == null) throw PoolException.NoHolder();
            if (!holder.gameObject.activeInHierarchy) throw PoolException.InactiveHolder(holder);
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
                // The whole of a hit: one reparent, no activation, nothing woken up.
                instance = _parked.Pop();
                instance.transform.SetParent(parent, false);
            }
            else
            {
                // A miss goes straight to the caller's parent. Building it parked first would be a
                // reparent to the holder that the line above immediately undoes, and the first fill
                // of a screen is nothing but misses, which is exactly the stretch the comparison
                // against DirectSpawner measures.
                instance = Create(parent);
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

            // Parented in the same call it was instantiated in, so it never draws a frame at the
            // world origin where Instantiate left it. worldPositionStays is false so a
            // RectTransform keeps the anchored layout it was authored with.
            instance.transform.SetParent(parent, false);

            CreatedCount++;
            return instance;
        }

        // An instance in this pool's idle state: under the holder and still active, which is where
        // Release leaves one too. Named for what it produces rather than for Prewarm, its only
        // caller today, so anything later wanting a spare instance is not reading someone else's
        // intent. It hands the instance back rather than pushing it, because the caller owns
        // _parked and a Create that quietly added to it would do two things under a name that
        // promises one.
        private T CreateIdle() => Create(_holder);

        // The GameObject, not the component: destroying the component alone leaves an empty object
        // behind, and on a Transform the engine refuses outright.
        private void DestroyInstance(T instance)
        {
            DestroyedCount++;
            Object.Destroy(instance.gameObject);
        }
    }
}

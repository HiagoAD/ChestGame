using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // The hand-rolled pool, and the version most projects end up writing: parked instances are
    // deactivated under a holder, and a get reactivates one under the caller's parent. Every get
    // and every release therefore runs OnEnable and OnDisable down the whole instance and dirties
    // the canvas and every layout group above it. ParkedPool is the same idea with that removed -
    // docs/design-decisions.md has what the difference measures.
    public class ActivationPool<T> : IPrefabPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _holder;
        private readonly int _maxSize;

        private readonly Stack<T> _parked = new();
        private readonly HashSet<T> _handedOut = new();

        private readonly List<T> _scratch = new();
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
                // parent the instance will live under. The other order rebuilds twice and shows the
                // instance for a frame wherever it was parked.
                instance.transform.SetParent(parent, false);
                instance.gameObject.SetActive(true);
            }
            else
            {
                // A miss goes straight to the caller's parent: building it parked first would
                // reparent and deactivate it only for the two lines above to undo both.
                instance = Create(parent);

                // Here so a prefab authored with an inactive root still comes out visible.
                // Instantiate already returns an active clone in the normal case.
                instance.gameObject.SetActive(true);
            }

            _handedOut.Add(instance);
            return instance;
        }

        public void Release(T instance)
        {
            // Remove before the null check, not after. Unity's overloaded equality makes a
            // destroyed instance read as null, so the other order short-circuits past Remove and
            // strands the dead entry in the set forever.
            if (!_handedOut.Remove(instance) || instance == null) throw PoolException.NotHandedOut(instance);

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
            if (_parked.Count + count > _maxSize) throw PoolException.PrewarmPastMaxSize(count, _parked.Count, _maxSize);

            for (int i = 0; i < count; i++) _parked.Push(CreateIdle());
        }

        // To zero rather than down to max size: Release and Prewarm both refuse to park past the
        // bound, so there is never a surplus for a trim-to-max-size to find.
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

            // worldPositionStays is false, so a RectTransform keeps the anchored layout it was
            // authored with.
            instance.transform.SetParent(parent, false);

            CreatedCount++;
            return instance;
        }

        // This pool's idle state: under the holder and switched off, which is where Release leaves
        // one too. Hands the instance back rather than pushing it, so _parked stays the caller's.
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
            // An instance handed out can be destroyed behind the pool's back, and .gameObject on a
            // destroyed Component throws MissingReferenceException.
            if (instance == null) return;

            DestroyedCount++;
            Object.Destroy(instance.gameObject);
        }
    }
}

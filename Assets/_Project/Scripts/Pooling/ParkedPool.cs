using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // ActivationPool with the SetActive taken out: parking by reparenting alone costs one hierarchy
    // change instead of an OnEnable/OnDisable pass down the whole instance and a canvas rebuild
    // above it. That saving is a uGUI one, so pick ActivationPool for anything in world space -
    // docs/design-decisions.md has the numbers.
    //
    // The price is that parked instances stay live: they still render and still tick. Hiding them
    // is the holder's job and the holder belongs to the caller, which is why this class only
    // reparents to it. What the holder must not be is inactive, which the constructor refuses: the
    // hierarchy would deactivate everything parked under it and fire exactly the OnDisable this
    // class exists to avoid.
    public class ParkedPool<T> : IPrefabPool<T> where T : Component
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
                // A miss goes straight to the caller's parent: building it parked first would be a
                // reparent to the holder that Create immediately undoes.
                instance = Create(parent);

                // The miss path only: a parked instance is never deactivated, so a hit has nothing
                // to switch on. Here so a prefab authored with an inactive root still comes out
                // visible, the same guarantee ActivationPool.Get makes.
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

            // Parented in the same call it was instantiated in, so it never draws a frame at the
            // world origin where Instantiate left it. worldPositionStays is false, so a
            // RectTransform keeps the anchored layout it was authored with.
            instance.transform.SetParent(parent, false);

            CreatedCount++;
            return instance;
        }

        // This pool's idle state: under the holder and still active, which is where Release leaves
        // one too. Hands the instance back rather than pushing it, so _parked stays the caller's.
        private T CreateIdle() => Create(_holder);

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

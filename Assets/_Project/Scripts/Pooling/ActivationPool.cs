using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    /// <summary>
    /// The hand-rolled pool: parked instances are deactivated under a holder, and a get reactivates
    /// one under the caller's parent.
    /// </summary>
    /// <remarks>
    /// See docs/design-decisions.md, "Why ParkedPool is the default".
    /// </remarks>
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

        /// <remarks>
        /// See docs/pooling.md, "ActivationPool's traps".
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        public T Get(Transform parent)
        {
            if (_disposed) throw PoolException.Disposed();

            T instance;
            if (_parked.Count > 0)
            {
                instance = _parked.Pop();

                instance.transform.SetParent(parent, false);
                instance.gameObject.SetActive(true);
            }
            else
            {
                instance = Create(parent);

                instance.gameObject.SetActive(true);
            }

            _handedOut.Add(instance);
            return instance;
        }

        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        public void Release(T instance)
        {
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

        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        public void ReleaseAll()
        {
            _scratch.Clear();
            _scratch.AddRange(_handedOut);

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

        /// <remarks>
        /// See docs/design-decisions.md, "The seam itself".
        /// </remarks>
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

        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        private T Create(Transform parent)
        {
            T instance = Object.Instantiate(_prefab);

            instance.transform.SetParent(parent, false);

            CreatedCount++;
            return instance;
        }

        /// <remarks>
        /// See docs/pooling.md, "ActivationPool's traps".
        /// </remarks>
        private T CreateIdle()
        {
            T instance = Create(_holder);
            instance.gameObject.SetActive(false);
            return instance;
        }

        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        private void DestroyInstance(T instance)
        {
            if (instance == null) return;

            DestroyedCount++;
            Object.Destroy(instance.gameObject);
        }
    }
}

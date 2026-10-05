using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

namespace Company.ChestGame.Pooling
{
    /// <summary>
    /// <see cref="IPrefabPool{T}"/> implementation built on the engine's own
    /// <see cref="ObjectPool{T}"/> rather than a hand-rolled stack.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "UnityPool, and what wrapping ObjectPool costs".
    /// </remarks>
    public class UnityPool<T> : IPrefabPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _holder;
        private readonly int _maxSize;
        private readonly ObjectPool<T> _pool;

        private readonly HashSet<T> _handedOut = new();

        private readonly List<T> _scratch = new();
        private bool _disposed;

        public int CreatedCount { get; private set; }
        public int DestroyedCount { get; private set; }

        /// <summary>
        /// Number of instances currently handed out.
        /// </summary>
        /// <remarks>
        /// Reads from this pool's own handed-out set rather than from the underlying
        /// <see cref="ObjectPool{T}"/>.
        /// See docs/pooling.md, "UnityPool, and what wrapping ObjectPool costs".
        /// </remarks>
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

            _pool = new ObjectPool<T>(Create, actionOnRelease: Park, actionOnDestroy: DestroyInstance,
                collectionCheck: true, defaultCapacity: maxSize, maxSize: maxSize);
        }

        /// <summary>
        /// Hands back a pooled or newly created instance, reparented under <paramref name="parent"/>
        /// and active.
        /// </summary>
        /// <exception cref="PoolException">The pool has been disposed.</exception>
        /// <remarks>
        /// See docs/design-decisions.md, "Why ParkedPool is the default".
        /// See docs/pooling.md, "ActivationPool's traps".
        /// </remarks>
        public T Get(Transform parent)
        {
            if (_disposed) throw PoolException.Disposed();

            T instance = _pool.Get();

            instance.transform.SetParent(parent, false);
            instance.gameObject.SetActive(true);

            _handedOut.Add(instance);
            return instance;
        }

        /// <summary>
        /// Returns a previously handed-out instance to the pool.
        /// </summary>
        /// <exception cref="PoolException">
        /// <paramref name="instance"/> was not handed out by this pool, or has already been
        /// released.
        /// </exception>
        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// See docs/pooling.md, "UnityPool, and what wrapping ObjectPool costs".
        /// </remarks>
        public void Release(T instance)
        {
            if (!_handedOut.Remove(instance) || instance == null) throw PoolException.NotHandedOut(instance);

            if (_pool.CountInactive >= _maxSize)
            {
                DestroyInstance(instance);
                return;
            }

            _pool.Release(instance);
        }

        /// <summary>
        /// Releases every instance currently handed out.
        /// </summary>
        /// <exception cref="PoolException">
        /// Rethrown after every instance has been released, if releasing any of them failed.
        /// </exception>
        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// See docs/pooling.md, "UnityPool, and what wrapping ObjectPool costs".
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

        /// <summary>
        /// Creates and parks <paramref name="count"/> instances.
        /// </summary>
        /// <exception cref="PoolException">
        /// The pool has been disposed, or parking <paramref name="count"/> more instances would
        /// pass the pool's max size.
        /// </exception>
        /// <remarks>
        /// See docs/pooling.md, "UnityPool, and what wrapping ObjectPool costs".
        /// </remarks>
        public void Prewarm(int count)
        {
            if (_disposed) throw PoolException.Disposed();
            if (_pool.CountInactive + count > _maxSize)
            {
                throw PoolException.PrewarmPastMaxSize(count, _pool.CountInactive, _maxSize);
            }

            for (int i = 0; i < count; i++) _pool.Release(Create());
        }

        /// <summary>
        /// Destroys every parked instance. Instances currently handed out are left alone.
        /// </summary>
        /// <remarks>
        /// See docs/design-decisions.md, "The seam itself".
        /// </remarks>
        public void Trim() => _pool.Clear();

        /// <summary>
        /// Destroys every instance this pool owns, parked and handed out alike.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;

            foreach (T instance in new List<T>(_handedOut)) DestroyInstance(instance);
            _handedOut.Clear();

            _pool.Dispose();
        }

        private T Create()
        {
            T instance = Object.Instantiate(_prefab);
            Park(instance);

            CreatedCount++;
            return instance;
        }

        /// <remarks>
        /// Also this pool's <c>actionOnRelease</c> callback.
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        private void Park(T instance)
        {
            instance.gameObject.SetActive(false);
            instance.transform.SetParent(_holder, false);
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

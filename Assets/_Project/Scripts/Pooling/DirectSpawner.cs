using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    /// <summary>
    /// The baseline: Instantiate on the way out, Destroy on the way back, nothing kept in between.
    /// Honours the same contract as the other three implementations - the same counters, the same
    /// rejection of a release it never handed out - and differs from them in nothing but pooling
    /// nothing.
    /// </summary>
    /// <remarks>
    /// See docs/design-decisions.md, "Pooling, and why the board is rebuilt rather than kept".
    /// </remarks>
    public class DirectSpawner<T> : IPrefabPool<T> where T : Component
    {
        private readonly T _prefab;

        /// <remarks>
        /// See docs/pooling.md, "DirectSpawner, and why it still needs a handed-out set".
        /// </remarks>
        private readonly HashSet<T> _handedOut = new();

        private readonly List<T> _scratch = new();
        private bool _disposed;

        public int CreatedCount { get; private set; }
        public int DestroyedCount { get; private set; }
        public int ActiveCount => _handedOut.Count;

        /// <summary>
        /// Always zero.
        /// </summary>
        /// <remarks>
        /// See docs/pooling.md, "DirectSpawner, and why it still needs a handed-out set".
        /// </remarks>
        public int AvailableCount => 0;

        /// <remarks>
        /// See docs/pooling.md, "DirectSpawner, and why it still needs a handed-out set".
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        public DirectSpawner(T prefab)
        {
            if (prefab == null) throw PoolException.NoPrefab();

            _prefab = prefab;
        }

        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        public T Get(Transform parent)
        {
            if (_disposed) throw PoolException.Disposed();

            T instance = Object.Instantiate(_prefab);

            instance.transform.SetParent(parent, false);

            instance.gameObject.SetActive(true);

            CreatedCount++;
            _handedOut.Add(instance);
            return instance;
        }

        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        public void Release(T instance)
        {
            if (!_handedOut.Remove(instance) || instance == null) throw PoolException.NotHandedOut(instance);

            DestroyInstance(instance);
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

        /// <remarks>
        /// See docs/design-decisions.md, "The seam itself".
        /// </remarks>
        public void Prewarm(int count)
        {
            if (_disposed) throw PoolException.Disposed();
        }

        /// <remarks>
        /// See docs/design-decisions.md, "The seam itself".
        /// </remarks>
        public void Trim() { }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            ReleaseAll();
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

using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // The baseline: Instantiate on the way out, Destroy on the way back, nothing kept in between.
    // It exists so the comparison against the three pools is measured rather than asserted, which
    // only works if it implements the same contract honestly - the same counters, the same loud
    // rejection of a release it never handed out - and differs in nothing but pooling nothing.
    public class DirectSpawner<T> : IPrefabPool<T> where T : Component
    {
        private readonly T _prefab;

        // Handed out and not yet given back. The baseline keeps this for the same reason the pools
        // do: without it, releasing a foreign instance would destroy something it never owned.
        private readonly HashSet<T> _handedOut = new();

        private bool _disposed;

        public int CreatedCount { get; private set; }
        public int DestroyedCount { get; private set; }
        public int ActiveCount => _handedOut.Count;

        // Always zero, and not a stub: holding nothing between a release and the next get is the
        // whole of what this class is.
        public int AvailableCount => 0;

        // No holder and no max size, because it parks nothing and bounds nothing. Taking either
        // would be a parameter that does not do anything.
        public DirectSpawner(T prefab)
        {
            // The T constraint makes this use Unity's overloaded equality, which also catches a
            // prefab that was destroyed since the caller looked it up.
            if (prefab == null) throw PoolException.NoPrefab();

            _prefab = prefab;
        }

        public T Get(Transform parent)
        {
            if (_disposed) throw PoolException.Disposed();

            T instance = Object.Instantiate(_prefab);

            // Parented here rather than through the Instantiate overload, so worldPositionStays is
            // visibly false and a RectTransform keeps the anchored layout it was authored with.
            instance.transform.SetParent(parent, false);

            CreatedCount++;
            _handedOut.Add(instance);
            return instance;
        }

        public void Release(T instance)
        {
            if (instance == null || !_handedOut.Remove(instance)) throw PoolException.NotHandedOut(instance);

            DestroyInstance(instance);
        }

        public void ReleaseAll()
        {
            // Snapshot, because Release edits the set being walked.
            foreach (T instance in new List<T>(_handedOut)) Release(instance);
        }

        // There is nowhere to park an instance that is not being handed out, so warming here would
        // only leak count of them. Doing nothing rather than throwing is what lets code written
        // against the seam still run on the baseline - but a disposed pool still refuses, because
        // that half of the rule holds for every implementation and a caller should not have to know
        // which one it is holding to know whether the call was honoured.
        public void Prewarm(int count)
        {
            if (_disposed) throw PoolException.Disposed();
        }

        // Nothing held means nothing to trim. Unguarded, like Release: the pools call Trim from
        // inside their own Dispose.
        public void Trim() { }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            ReleaseAll();
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

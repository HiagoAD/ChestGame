using System.Collections.Generic;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // The baseline: Instantiate on the way out, Destroy on the way back, nothing kept in between.
    // It exists so the comparison against the three pools is measured rather than asserted, which
    // only works if it honours the same contract - the same counters, the same loud rejection of a
    // release it never handed out - and differs in nothing but pooling nothing.
    public class DirectSpawner<T> : IPrefabPool<T> where T : Component
    {
        private readonly T _prefab;

        // Handed out and not yet given back. Without it, releasing a foreign instance would
        // destroy something this class never owned.
        private readonly HashSet<T> _handedOut = new();

        private readonly List<T> _scratch = new();
        private bool _disposed;

        public int CreatedCount { get; private set; }
        public int DestroyedCount { get; private set; }
        public int ActiveCount => _handedOut.Count;

        // Always zero, and not a stub: holding nothing between a release and the next get is the
        // whole of what this class is.
        public int AvailableCount => 0;

        // No holder and no max size, because it parks nothing and bounds nothing.
        public DirectSpawner(T prefab)
        {
            // The T constraint makes this use Unity's overloaded equality, which also catches a
            // prefab destroyed since the caller looked it up.
            if (prefab == null) throw PoolException.NoPrefab();

            _prefab = prefab;
        }

        public T Get(Transform parent)
        {
            if (_disposed) throw PoolException.Disposed();

            T instance = Object.Instantiate(_prefab);

            // worldPositionStays is false, so a RectTransform keeps the anchored layout it was
            // authored with.
            instance.transform.SetParent(parent, false);

            // A prefab with an inactive root has to come out visible here too, the same guarantee
            // the pools make. Instantiate already returns an active clone in the normal case.
            instance.gameObject.SetActive(true);

            CreatedCount++;
            _handedOut.Add(instance);
            return instance;
        }

        public void Release(T instance)
        {
            // Remove before the null check, not after. Unity's overloaded equality makes a
            // destroyed instance read as null, so the other order short-circuits past Remove and
            // strands the dead entry in the set forever.
            if (!_handedOut.Remove(instance) || instance == null) throw PoolException.NotHandedOut(instance);

            DestroyInstance(instance);
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

        // Nowhere to park an instance that is not handed out, so warming here would only leak count
        // of them. Doing nothing rather than throwing is what lets code written against the seam
        // still run on the baseline - but a disposed pool still refuses, as on every implementation.
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
            // An instance handed out can be destroyed behind the pool's back, and .gameObject on a
            // destroyed Component throws MissingReferenceException.
            if (instance == null) return;

            DestroyedCount++;
            Object.Destroy(instance.gameObject);
        }
    }
}

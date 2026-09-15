using System;
using System.Collections.Generic;
using Company.ChestGame.Pooling;
using Company.ChestGame.Tests.Common;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// A pool whose Get() costs a chosen amount of the fake clock's time instead of whatever a real
    /// pool costs.
    /// </summary>
    /// <remarks>
    /// See docs/pooling.md, "How the race is measured".
    /// </remarks>
    public sealed class FakePrefabPool<T> : IPrefabPool<T> where T : Component
    {
        private readonly FakeGameClock _clock;
        private readonly double _costPerGetMilliseconds;
        private readonly Func<T> _factory;
        private readonly HashSet<T> _handedOut = new();

        public int CreatedCount { get; private set; }
        public int DestroyedCount { get; private set; }
        public int ActiveCount => _handedOut.Count;
        public int AvailableCount => 0;

        public FakePrefabPool(FakeGameClock clock, double costPerGetMilliseconds, Func<T> factory)
        {
            _clock = clock;
            _costPerGetMilliseconds = costPerGetMilliseconds;
            _factory = factory;
        }

        public T Get(Transform parent)
        {
            _clock.Spend(_costPerGetMilliseconds);

            T instance = _factory();
            instance.transform.SetParent(parent, false);

            CreatedCount++;
            _handedOut.Add(instance);
            return instance;
        }

        public void Release(T instance)
        {
            if (instance == null || !_handedOut.Remove(instance))
            {
                throw new InvalidOperationException("released an instance this fake never handed out");
            }

            DestroyedCount++;
        }

        public void ReleaseAll()
        {
            foreach (T instance in new List<T>(_handedOut)) Release(instance);
        }

        /// <remarks>
        /// A no-op: <see cref="Get"/> always pays its cost fresh, so nothing here would be warmed.
        /// See docs/pooling.md, "How the race is measured".
        /// </remarks>
        public void Prewarm(int count)
        {
        }

        public void Trim() { }
        public void Dispose() { }
    }
}

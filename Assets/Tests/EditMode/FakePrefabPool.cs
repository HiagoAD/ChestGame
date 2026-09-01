using System;
using System.Collections.Generic;
using Company.ChestGame.Pooling;
using Company.ChestGame.Tests.Common;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // A pool whose Get() costs a chosen amount of the fake clock's time instead of whatever a real
    // pool costs. FakeGameClock never advances on its own and there is no engine underneath to make
    // SetActive expensive, so the real pools are indistinguishable under it. This stands in for
    // "cheap" and "expensive", the way FrameBudgetedLoopTests' CostlyStep stands in for real work.
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

        public void Prewarm(int count)
        {
            // Nothing parks here - Get pays the cost fresh every time - so warming would only inflate
            // CreatedCount without the race seeing a hit. The tests that care about prewarming use
            // the real pools, where a hit is real.
        }

        public void Trim() { }
        public void Dispose() { }
    }
}

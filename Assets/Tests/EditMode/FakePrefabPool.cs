using System;
using System.Collections.Generic;
using Company.ChestGame.Pooling;
using Company.ChestGame.Tests.Common;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // A pool whose Get() costs a chosen, controllable amount of the fake clock's time instead of
    // whatever a real ActivationPool or ParkedPool actually costs. FakeGameClock never advances on
    // its own, so nothing about the real pools differs under it - there is no real engine underneath
    // to make SetActive expensive. This is what stands in for "cheap" and "expensive" in a suite
    // that cannot see one, the same way FrameBudgetedLoopTests' CostlyStep stands in for a real unit
    // of work.
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
            // Nothing parks here - Get already pays the cost fresh every time - so warming would only
            // inflate CreatedCount without the race ever seeing a hit. The tests that care about
            // prewarming exercise it against the real pools instead, where a hit is real.
        }

        public void Trim() { }
        public void Dispose() { }
    }
}

using System;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // Seam over where an instance comes from, so a screen that spawns prefabs can be handed a real
    // pool or the Instantiate/Destroy baseline without knowing which it got.
    //
    // Disposing destroys everything the pool owns, parked and handed out alike. After that, Get and
    // Prewarm throw PoolException, because a disposed pool that quietly instantiated again would be
    // a second pool nobody owns. Release and Trim stay callable and simply find nothing to do: an
    // implementation's own Dispose calls them once it has already marked itself disposed, so
    // guarding those two would make Dispose throw on the way out.
    //
    // Deliberately synchronous, and it knows nothing about frames. Spreading a large fill over
    // several frames is the caller's job through IGameClock: a pool that awaited internally would
    // hide that cost inside whichever call happened to trigger the fill.
    public interface IPrefabPool<T> : IDisposable where T : Component
    {
        // Everything this pool has ever instantiated and everything it has destroyed. Both keep
        // counting across a Trim or a refill, because what the comparison between implementations
        // is about is how much instantiating a strategy avoids, not what it is holding right now.
        int CreatedCount { get; }
        int DestroyedCount { get; }

        // Handed out, and parked ready to be handed out. Kept apart because a pool that is leaking
        // looks exactly like a busy one if you only count instances.
        int ActiveCount { get; }
        int AvailableCount { get; }

        // Reuses a parked instance or creates one, parents it to parent, and hands it over. Never
        // returns null: it either answers or throws.
        T Get(Transform parent);

        // Takes an instance back. Throws PoolException if this pool never handed it out, or handed
        // it out and has already taken it back. Accepting either would park the same instance twice
        // and hand it to two callers later, which is a bug that surfaces nowhere near the release
        // that caused it.
        void Release(T instance);

        // Takes back everything currently handed out, for a screen tearing down without a record of
        // what it spawned.
        void ReleaseAll();

        // Creates count parked instances up front, handing none of them out, so the first frame
        // that needs them instantiates nothing. Throws rather than warming fewer than asked.
        void Prewarm(int count);

        // Destroys every parked instance and leaves the handed-out ones running. What a scene
        // change or a memory warning calls.
        void Trim();
    }
}

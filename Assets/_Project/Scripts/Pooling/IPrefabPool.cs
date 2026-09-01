using System;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    // Seam over where an instance comes from, so a screen that spawns prefabs can be handed a real
    // pool or the Instantiate/Destroy baseline without knowing which it got. The four
    // implementations and how they compare are in docs/design-decisions.md.
    //
    // Disposing destroys everything the pool owns, parked and handed out alike. Get and Prewarm
    // throw PoolException afterwards. Release, ReleaseAll and Trim are left unguarded on purpose,
    // because an implementation's own Dispose calls them after marking itself disposed - guarding
    // them would make Dispose throw on the way out. ReleaseAll and Trim then find nothing to do;
    // a Release naming an instance reports NotHandedOut, the set having been emptied already.
    //
    // Synchronous, and it knows nothing about frames. Spreading a large fill over several frames is
    // the caller's job through IGameClock.
    public interface IPrefabPool<T> : IDisposable where T : Component
    {
        // Lifetime totals: both keep counting across a Trim or a refill.
        int CreatedCount { get; }
        int DestroyedCount { get; }

        // Handed out, and parked ready to be handed out.
        int ActiveCount { get; }
        int AvailableCount { get; }

        // Reuses a parked instance or creates one, parents it to parent, and hands it over. Never
        // returns null: it either answers or throws.
        T Get(Transform parent);

        // Takes an instance back. Throws PoolException if this pool never handed it out, or has
        // already taken it back: accepting either would park the same instance twice and hand it to
        // two callers later.
        void Release(T instance);

        // Takes back everything currently handed out, for a screen tearing down without a record of
        // what it spawned.
        void ReleaseAll();

        // Creates count parked instances up front, handing none of them out. Throws rather than
        // warming fewer than asked, with one carve-out: DirectSpawner has nowhere to park anything,
        // so it warms zero and returns. A disposed pool still refuses on all four.
        //
        // The bound an implementation is constructed with limits the parked stack, not the total
        // number of live instances. Prewarming while instances are handed out can therefore take a
        // pool past that number.
        void Prewarm(int count);

        // Destroys every parked instance and leaves the handed-out ones running.
        void Trim();
    }
}

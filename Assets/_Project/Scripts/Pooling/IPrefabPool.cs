using System;
using UnityEngine;

namespace Company.ChestGame.Pooling
{
    /// <summary>
    /// Seam over where an instance comes from, so a screen that spawns prefabs can be handed a real
    /// pool or the Instantiate/Destroy baseline without knowing which it got.
    /// </summary>
    /// <remarks>
    /// Disposing destroys everything the pool owns, parked and handed out alike. <see cref="Get"/>
    /// and <see cref="Prewarm"/> throw <see cref="PoolException"/> afterwards. <see cref="Release"/>,
    /// <see cref="ReleaseAll"/> and <see cref="Trim"/> are left unguarded: <see cref="ReleaseAll"/>
    /// and <see cref="Trim"/> find nothing to do on a disposed pool, and a <see cref="Release"/>
    /// naming an instance reports <c>NotHandedOut</c>, the set having been emptied already.
    /// Synchronous, and it knows nothing about frames. Spreading a large fill over several frames is
    /// the caller's job through <c>IGameClock</c>.
    /// See docs/design-decisions.md, "The seam itself".
    /// See docs/design-decisions.md, "Why ParkedPool is the default".
    /// </remarks>
    public interface IPrefabPool<T> : IDisposable where T : Component
    {
        /// <summary>
        /// A lifetime total: keeps counting across a Trim or a refill.
        /// </summary>
        int CreatedCount { get; }

        /// <summary>
        /// A lifetime total: keeps counting across a Trim or a refill.
        /// </summary>
        int DestroyedCount { get; }

        /// <summary>
        /// Handed out.
        /// </summary>
        int ActiveCount { get; }

        /// <summary>
        /// Parked, ready to be handed out.
        /// </summary>
        int AvailableCount { get; }

        /// <summary>
        /// Reuses a parked instance or creates one, parents it to parent, and hands it over. Never
        /// returns null: it either answers or throws.
        /// </summary>
        T Get(Transform parent);

        /// <summary>
        /// Takes an instance back.
        /// </summary>
        /// <exception cref="PoolException">
        /// This pool never handed out <paramref name="instance"/>, or has already taken it back.
        /// </exception>
        /// <remarks>
        /// See docs/pooling.md, "The seam's contract details".
        /// </remarks>
        void Release(T instance);

        /// <summary>
        /// Takes back everything currently handed out, for a screen tearing down without a record of
        /// what it spawned.
        /// </summary>
        void ReleaseAll();

        /// <summary>
        /// Creates count parked instances up front, handing none of them out.
        /// </summary>
        /// <exception cref="PoolException">
        /// Thrown rather than warming fewer than asked, with one carve-out: <c>DirectSpawner</c> has
        /// nowhere to park anything, so it warms zero and returns. A disposed pool still refuses on
        /// all four.
        /// </exception>
        /// <remarks>
        /// The bound an implementation is constructed with limits the parked stack, not the total
        /// number of live instances. Prewarming while instances are handed out can therefore take a
        /// pool past that number.
        /// </remarks>
        void Prewarm(int count);

        /// <summary>
        /// Destroys every parked instance and leaves the handed-out ones running.
        /// </summary>
        void Trim();
    }
}

using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Pooling
{
    /// <summary>
    /// A pool was asked for something it cannot honestly do: an unassigned or destroyed prefab or
    /// holder, an inactive holder, a bound below one, a prewarm past the bound, an instance the
    /// pool never handed out or already released, or any call made after disposal.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Exception hierarchy".
    /// See docs/pooling.md, "PoolException, and why the messages live in one place".
    /// </remarks>
    public class PoolException : InvalidOperationException
    {
        public PoolException(string message) : base(message) { }

        public static PoolException NoPrefab() =>
            new("A pool needs a prefab to instantiate from, and was handed none or a destroyed one");

        public static PoolException NoHolder() =>
            new("A pool needs a holder to park instances under, and was handed none or a destroyed one");

        /// <summary>
        /// Thrown by <see cref="ParkedPool{T}"/> when the holder it was given is inactive.
        /// </summary>
        /// <remarks>
        /// See docs/design-decisions.md, "Why ParkedPool is the default".
        /// </remarks>
        public static PoolException InactiveHolder(Transform holder) =>
            new($"ParkedPool's holder '{holder.name}' is inactive, so parking an instance under it would deactivate the instance anyway");

        public static PoolException MaxSizeBelowOne(int maxSize) =>
            new($"A pool's max size has to be at least 1, got {maxSize}");

        /// <summary>
        /// Thrown when a <c>Prewarm</c> call would push the number of parked instances past the
        /// pool's bound.
        /// </summary>
        /// <remarks>
        /// See docs/design-decisions.md, "The seam itself".
        /// </remarks>
        public static PoolException PrewarmPastMaxSize(int count, int alreadyParked, int maxSize) =>
            new($"Prewarming {count} on top of {alreadyParked} already parked would pass the max size of {maxSize}");

        /// <summary>
        /// Thrown when <c>Release</c> is given an instance this pool never handed out, or one
        /// already released back to it.
        /// </summary>
        /// <remarks>
        /// See docs/design-decisions.md, "The seam itself".
        /// </remarks>
        public static PoolException NotHandedOut(Object instance) =>
            new(instance == null
                ? "A null or already destroyed instance cannot be released"
                : $"Instance '{instance.name}' was not handed out by this pool, or has already been released");

        public static PoolException Disposed() =>
            new("This pool has been disposed and cannot hand out instances any more");
    }
}

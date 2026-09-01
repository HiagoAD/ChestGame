using System;
using UnityEngine;
// System brings a second Object with it; the alias keeps NotHandedOut's parameter meaning what it did.
using Object = UnityEngine.Object;

namespace Company.ChestGame.Pooling
{
    // A pool was asked for something it cannot honestly do. Typed, because a test asserting that
    // releasing a foreign instance throws must not be satisfied by a NullReferenceException from
    // somewhere inside the release path.
    //
    // Deliberately not under ChestGameException, and not an oversight for a later tidy-up to
    // correct: every failure named below is a wiring mistake rather than a delivery failure. See
    // the exception hierarchy in docs/architecture.md for what that base means here.
    //
    // The wording lives here rather than in each implementation, so a failure names the mistake
    // instead of whichever of the four implementations happened to catch it.
    public class PoolException : InvalidOperationException
    {
        public PoolException(string message) : base(message) { }

        public static PoolException NoPrefab() =>
            new("A pool needs a prefab to instantiate from, and was handed none or a destroyed one");

        public static PoolException NoHolder() =>
            new("A pool needs a holder to park instances under, and was handed none or a destroyed one");

        // ParkedPool only, and the failure is silent otherwise: a child reparented under an inactive
        // object is deactivated by the hierarchy, firing exactly the OnDisable that pool exists to
        // avoid, with nothing about the result looking wrong.
        public static PoolException InactiveHolder(Transform holder) =>
            new($"ParkedPool's holder '{holder.name}' is inactive, so parking an instance under it would deactivate the instance anyway");

        public static PoolException MaxSizeBelowOne(int maxSize) =>
            new($"A pool's max size has to be at least 1, got {maxSize}");

        // Asking past the bound is a call site to fix, not a number to quietly shrink: warming fewer
        // than asked would make "Prewarm(n) creates n" conditional on a bound the caller cannot see.
        public static PoolException PrewarmPastMaxSize(int count, int alreadyParked, int maxSize) =>
            new($"Prewarming {count} on top of {alreadyParked} already parked would pass the max size of {maxSize}");

        // One message for both halves: once an instance is out of the pool's hands it cannot tell
        // "never mine" from "already given back".
        public static PoolException NotHandedOut(Object instance) =>
            new(instance == null
                ? "A null or already destroyed instance cannot be released"
                : $"Instance '{instance.name}' was not handed out by this pool, or has already been released");

        public static PoolException Disposed() =>
            new("This pool has been disposed and cannot hand out instances any more");
    }
}

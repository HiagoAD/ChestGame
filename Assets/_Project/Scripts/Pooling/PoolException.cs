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
    // Deliberately not under ChestGameException, and that is not an oversight for a later tidy-up to
    // correct. In this project that base is a behavioural signal rather than a label: GameManager
    // catches exactly it, turns whatever it caught into a content-unavailable popup and treats it as
    // handled, on the understanding that anything outside it is a bug and is left to blow up where it
    // can be seen. Every failure named below is a wiring mistake - a prefab slot nobody filled, a
    // holder that was never built, a pool used after it was disposed - and none of them is something
    // to tell a player their connection is bad about. InvalidOperationException instead, which is
    // already what "you cannot ask this object for that right now" means everywhere else in .NET.
    //
    // The wording lives here rather than in each implementation: all four reject the same mistakes,
    // and one set of messages keeps a failure naming the mistake instead of whichever implementation
    // happened to catch it.
    public class PoolException : InvalidOperationException
    {
        public PoolException(string message) : base(message) { }

        public static PoolException NoPrefab() =>
            new("A pool needs a prefab to instantiate from, and was handed none or a destroyed one");

        public static PoolException NoHolder() =>
            new("A pool needs a holder to park instances under, and was handed none or a destroyed one");

        // ParkedPool only. The failure it describes is silent otherwise: a child reparented under an
        // inactive object is deactivated by the hierarchy, firing exactly the OnDisable that pool
        // exists to avoid, and nothing about the result looks wrong.
        public static PoolException InactiveHolder(Transform holder) =>
            new($"ParkedPool's holder '{holder.name}' is inactive, so parking an instance under it would deactivate the instance anyway");

        public static PoolException MaxSizeBelowOne(int maxSize) =>
            new($"A pool's max size has to be at least 1, got {maxSize}");

        // Asking past the bound is a call site to fix, not a number to quietly shrink. Warming fewer
        // than asked would make "Prewarm(n) creates n" conditional on a bound the caller cannot see
        // from the call.
        public static PoolException PrewarmPastMaxSize(int count, int alreadyParked, int maxSize) =>
            new($"Prewarming {count} on top of {alreadyParked} already parked would pass the max size of {maxSize}");

        // One message for both halves on purpose. Once an instance is out of the pool's hands the
        // pool cannot tell "never mine" from "already given back", and naming both is more honest
        // than picking one.
        public static PoolException NotHandedOut(Object instance) =>
            new(instance == null
                ? "A null or already destroyed instance cannot be released"
                : $"Instance '{instance.name}' was not handed out by this pool, or has already been released");

        public static PoolException Disposed() =>
            new("This pool has been disposed and cannot hand out instances any more");
    }
}

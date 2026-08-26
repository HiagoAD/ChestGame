using Company.ChestGame.Assets;
using NUnit.Framework;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Company.ChestGame.Tests.EditMode
{
    // The provider's bookkeeping, and the only half testable without a real content catalog. It
    // protects two bugs production could never have shown: keying on the AssetReference instance,
    // which every production caller happens to satisfy, and releasing every handle at once, which
    // only matters when two callers hold the same asset.
    public class AssetHandleRegistryTests
    {
        private const string GUID = "11111111111111111111111111111111";
        private const string OTHER_GUID = "22222222222222222222222222222222";

        private AssetHandleRegistry _registry;

        // Real completed operations rather than default handles: every
        // default(AsyncOperationHandle) compares equal to every other, so the fixture could not
        // tell "handed back both" from "handed back the same one twice".
        private ResourceManager _resourceManager;

        [SetUp]
        public void SetUp()
        {
            _registry = new AssetHandleRegistry();
            _resourceManager = new ResourceManager();
        }

        [TearDown]
        public void TearDown() => _resourceManager.Dispose();

        private AsyncOperationHandle HandleNamed(string name) =>
            _resourceManager.CreateCompletedOperation(name, null);

        [Test]
        public void AssetReference_StillHasNoValueSemanticsOfItsOwn()
        {
            // The premise the rest of this fixture rests on, asserted rather than assumed. If the
            // package ever gives AssetReference an Equals, keying on the runtime key stops being
            // necessary.
            AssetReference authored = new(GUID);
            AssetReference rebuilt = new(GUID);

            Assert.AreNotSame(authored, rebuilt);
            Assert.IsFalse(authored.Equals(rebuilt),
                "AssetReference now compares by value; AssetHandleRegistry's key can be simplified");
        }

        [Test]
        public void AReferenceRebuiltFromTheSameGuid_TakesWhatTheFirstOneLeft()
        {
            // The first bug in one test: a caller holding a GUID rather than the definition asset's
            // own field used to get a silent no-op and an unreleasable handle.
            AsyncOperationHandle loaded = HandleNamed("view");
            _registry.Remember(new AssetReference(GUID), loaded);

            bool took = _registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle taken);

            Assert.IsTrue(took, "two references naming the same asset have to be one entry, or the handle leaks");
            Assert.AreEqual(loaded, taken, "and the entry has to hand back the handle that was actually loaded");
        }

        [Test]
        public void AReferenceToADifferentAsset_TakesNothing()
        {
            // The other half of value semantics: same-key must match, different-key must not.
            _registry.Remember(new AssetReference(GUID), HandleNamed("view"));

            Assert.IsFalse(_registry.TryTake(new AssetReference(OTHER_GUID), out AsyncOperationHandle taken));
            Assert.IsFalse(taken.IsValid(), "a miss must not hand back a handle at all, or the provider releases it");

            Assert.IsTrue(_registry.TryTake(new AssetReference(GUID), out _),
                "and the miss must not have consumed the entry it did not match");
        }

        [Test]
        public void AReferenceNamingASubObject_IsNotTheSameEntryAsItsParent()
        {
            // The runtime key carries the sub-object name, which is why it beats the bare GUID: a
            // sprite out of an atlas and the atlas itself are separate loads.
            AssetReference subObject = new(GUID) { SubObjectName = "Chest" };

            _registry.Remember(subObject, HandleNamed("sprite"));

            Assert.IsFalse(_registry.TryTake(new AssetReference(GUID), out _));
            Assert.IsTrue(_registry.TryTake(new AssetReference(GUID) { SubObjectName = "Chest" }, out _));
        }

        [Test]
        public void TwoLoadsOfOneAsset_NeedTwoReleasesAndYieldBothHandles()
        {
            // Addressables ref-counts per load. Keeping only the newest handle would leak the one
            // it replaced, and one release covering both would drop a count nobody paid.
            AsyncOperationHandle first = HandleNamed("first");
            AsyncOperationHandle second = HandleNamed("second");

            _registry.Remember(new AssetReference(GUID), first);
            _registry.Remember(new AssetReference(GUID), second);

            Assert.IsTrue(_registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle taken));
            Assert.IsTrue(_registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle takenAgain));

            CollectionAssert.AreEquivalent(new[] { first, second }, new[] { taken, takenAgain },
                "two loads have to hand back the two handles they took, each of them once");
            Assert.IsFalse(_registry.TryTake(new AssetReference(GUID), out _),
                "and nothing beyond them, or a third release drops a count that was never taken");
        }

        [Test]
        public void OneRelease_LeavesTheOtherLiveLoadsHandleAlone()
        {
            // The second bug, in the shape it occurs: two containers running the same minigame,
            // each having loaded its view. Take used to remove the whole list, so the first to end
            // pulled the asset out from under the second.
            AsyncOperationHandle stillRunning = HandleNamed("container-a");
            AsyncOperationHandle endingNow = HandleNamed("container-b");

            _registry.Remember(new AssetReference(GUID), stillRunning);
            _registry.Remember(new AssetReference(GUID), endingNow);

            _registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle released);

            Assert.AreEqual(endingNow, released,
                "newest first, which is what lets a load that failed after taking its own count give back its own handle");
            Assert.IsTrue(_registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle survivor),
                "the load that is still running has to still be tracked after the other one released");
            Assert.AreEqual(stillRunning, survivor);
        }

        [Test]
        public void TakingTwiceForOneLoad_YieldsNothingTheSecondTime()
        {
            // Taking is what stops tracking, so a caller that releases twice does not release the
            // same handle twice, which Addressables reports as an error.
            _registry.Remember(new AssetReference(GUID), HandleNamed("view"));

            _registry.TryTake(new AssetReference(GUID), out _);

            Assert.IsFalse(_registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle taken));
            Assert.IsFalse(taken.IsValid());
        }

        [Test]
        public void AnAssetLoadedAgainAfterItsLastReleaseIsTrackedAgain()
        {
            // The entry is dropped with its last handle, and loading again has to start tracking
            // rather than find a hole where it used to be.
            _registry.Remember(new AssetReference(GUID), HandleNamed("first run"));
            _registry.TryTake(new AssetReference(GUID), out _);

            AsyncOperationHandle reloaded = HandleNamed("second run");
            _registry.Remember(new AssetReference(GUID), reloaded);

            Assert.IsTrue(_registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle taken));
            Assert.AreEqual(reloaded, taken);
        }

        [Test]
        public void TakingAReferenceNothingWasLoadedFor_IsSafe()
        {
            // End and BeginAsync's unwind both release unconditionally, which only works because
            // nothing tracked yields nothing rather than failing.
            Assert.IsFalse(_registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle nothing));
            Assert.IsFalse(nothing.IsValid());

            Assert.IsFalse(_registry.TryTake(null, out AsyncOperationHandle forNull));
            Assert.IsFalse(forNull.IsValid());

            Assert.DoesNotThrow(() => _registry.Remember(null, default));
        }
    }
}

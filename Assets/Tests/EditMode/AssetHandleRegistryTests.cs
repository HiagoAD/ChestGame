using Company.ChestGame.Assets;
using NUnit.Framework;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Company.ChestGame.Tests.EditMode
{
    // The provider's bookkeeping, which is the half of it that has a rule rather than a
    // translation, and the only half that can be tested without a real content catalog behind it.
    //
    // What it protects is two bugs production could never have shown. The dictionary used to key on
    // the AssetReference instance, and every production caller happens to hand back the very same
    // serialized field it loaded with, so that miss only ever appears for a caller that builds an
    // equivalent reference of its own. And releasing used to hand back every handle held for an
    // asset at once, which only ever matters when two callers hold the same asset — which the
    // framework allows, because MinigameManager.Get returns a fresh container per request, and
    // which the shell happens never to do, because it runs one minigame at a time.
    public class AssetHandleRegistryTests
    {
        private const string GUID = "11111111111111111111111111111111";
        private const string OTHER_GUID = "22222222222222222222222222222222";

        private AssetHandleRegistry _registry;

        // Real completed operations rather than default handles, so that the handles in this fixture
        // can be told apart. Which handle comes back is the whole subject here, and every
        // default(AsyncOperationHandle) compares equal to every other one — a fixture built on
        // those could not tell "handed back both" from "handed back the same one twice".
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
            // necessary and this says so in one line instead of leaving dead indirection behind.
            AssetReference authored = new(GUID);
            AssetReference rebuilt = new(GUID);

            Assert.AreNotSame(authored, rebuilt);
            Assert.IsFalse(authored.Equals(rebuilt),
                "AssetReference now compares by value; AssetHandleRegistry's key can be simplified");
        }

        [Test]
        public void AReferenceRebuiltFromTheSameGuid_TakesWhatTheFirstOneLeft()
        {
            // The first bug in one test. A caller that did not author the reference it is releasing
            // — anything holding a GUID rather than the definition asset's own field — used to get
            // a silent no-op and a handle nobody could ever let go of again.
            AsyncOperationHandle loaded = HandleNamed("view");
            _registry.Remember(new AssetReference(GUID), loaded);

            bool took = _registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle taken);

            Assert.IsTrue(took, "two references naming the same asset have to be one entry, or the handle leaks");
            Assert.AreEqual(loaded, taken, "and the entry has to hand back the handle that was actually loaded");
        }

        [Test]
        public void AReferenceToADifferentAsset_TakesNothing()
        {
            // The other half of value semantics: same-key must match, different-key must not, or
            // the lookup would be releasing whatever it found first.
            _registry.Remember(new AssetReference(GUID), HandleNamed("view"));

            Assert.IsFalse(_registry.TryTake(new AssetReference(OTHER_GUID), out AsyncOperationHandle taken));
            Assert.IsFalse(taken.IsValid(), "a miss must not hand back a handle at all, or the provider releases it");

            Assert.IsTrue(_registry.TryTake(new AssetReference(GUID), out _),
                "and the miss must not have consumed the entry it did not match");
        }

        [Test]
        public void AReferenceNamingASubObject_IsNotTheSameEntryAsItsParent()
        {
            // The runtime key carries the sub-object name, which is the reason it is preferred to
            // the bare GUID: a sprite out of an atlas and the atlas itself are separate loads.
            AssetReference subObject = new(GUID) { SubObjectName = "Chest" };

            _registry.Remember(subObject, HandleNamed("sprite"));

            Assert.IsFalse(_registry.TryTake(new AssetReference(GUID), out _));
            Assert.IsTrue(_registry.TryTake(new AssetReference(GUID) { SubObjectName = "Chest" }, out _));
        }

        [Test]
        public void TwoLoadsOfOneAsset_NeedTwoReleasesAndYieldBothHandles()
        {
            // Addressables ref-counts per load, so a second load of the same asset is a second
            // handle and a second count. Keeping only the newest would leak the one it replaced,
            // and one release covering both would drop a count nobody paid.
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
            // The second bug in one test, in the shape it actually occurs: MinigameManager.Get
            // hands out a fresh container per request, so two containers can be running the same
            // minigame, each having loaded its view. Take used to remove the whole list, so the
            // first container to end released both counts and pulled the asset out from under the
            // second one while it was still on screen.
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
            // same handle twice — which Addressables reports as an error.
            _registry.Remember(new AssetReference(GUID), HandleNamed("view"));

            _registry.TryTake(new AssetReference(GUID), out _);

            Assert.IsFalse(_registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle taken));
            Assert.IsFalse(taken.IsValid());
        }

        [Test]
        public void AnAssetLoadedAgainAfterItsLastReleaseIsTrackedAgain()
        {
            // The entry is dropped entirely when its last handle goes, so that an asset loaded and
            // released over and over does not leave an empty list behind for the session. Loading
            // it again has to start tracking it again rather than find a hole where it used to be.
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
            // MinigameContainer.End and BeginAsync's unwind both release unconditionally, which
            // only works because nothing tracked yields nothing rather than failing.
            Assert.IsFalse(_registry.TryTake(new AssetReference(GUID), out AsyncOperationHandle nothing));
            Assert.IsFalse(nothing.IsValid());

            Assert.IsFalse(_registry.TryTake(null, out AsyncOperationHandle forNull));
            Assert.IsFalse(forNull.IsValid());

            Assert.DoesNotThrow(() => _registry.Remember(null, default));
        }
    }
}

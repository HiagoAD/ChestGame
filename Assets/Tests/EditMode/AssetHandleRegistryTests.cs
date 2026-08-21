using System.Collections.Generic;
using Company.ChestGame.Assets;
using NUnit.Framework;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Company.ChestGame.Tests.EditMode
{
    // The provider's bookkeeping, which is the half of it that has a rule rather than a
    // translation, and the only half that can be tested without a real content catalog behind it.
    //
    // What it protects is a bug that production could never have shown: the dictionary used to key
    // on the AssetReference instance, and every production caller happens to hand back the very
    // same serialized field it loaded with, so the miss only ever appears for a caller that builds
    // an equivalent reference of its own.
    public class AssetHandleRegistryTests
    {
        private const string GUID = "11111111111111111111111111111111";
        private const string OTHER_GUID = "22222222222222222222222222222222";

        private AssetHandleRegistry _registry;

        [SetUp]
        public void SetUp() => _registry = new AssetHandleRegistry();

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
            // The bug in one test. A caller that did not author the reference it is releasing —
            // anything holding a GUID rather than the definition asset's own field — used to get a
            // silent no-op and a handle nobody could ever let go of again.
            _registry.Remember(new AssetReference(GUID), default);

            IReadOnlyList<AsyncOperationHandle> taken = _registry.Take(new AssetReference(GUID));

            Assert.AreEqual(1, taken.Count,
                "two references naming the same asset have to be one entry, or the handle leaks");
        }

        [Test]
        public void AReferenceToADifferentAsset_TakesNothing()
        {
            // The other half of value semantics: same-key must match, different-key must not, or
            // the lookup would be releasing whatever it found first.
            _registry.Remember(new AssetReference(GUID), default);

            CollectionAssert.IsEmpty(_registry.Take(new AssetReference(OTHER_GUID)));
        }

        [Test]
        public void AReferenceNamingASubObject_IsNotTheSameEntryAsItsParent()
        {
            // The runtime key carries the sub-object name, which is the reason it is preferred to
            // the bare GUID: a sprite out of an atlas and the atlas itself are separate loads.
            AssetReference subObject = new(GUID) { SubObjectName = "Chest" };

            _registry.Remember(subObject, default);

            CollectionAssert.IsEmpty(_registry.Take(new AssetReference(GUID)));
            Assert.AreEqual(1, _registry.Take(new AssetReference(GUID) { SubObjectName = "Chest" }).Count);
        }

        [Test]
        public void EveryHandleTakenForOneAsset_IsHandedBack()
        {
            // Addressables ref-counts per load, so a second load of the same asset is a second
            // handle. Keeping only the newest would leak the one it replaced.
            _registry.Remember(new AssetReference(GUID), default);
            _registry.Remember(new AssetReference(GUID), default);

            Assert.AreEqual(2, _registry.Take(new AssetReference(GUID)).Count);
        }

        [Test]
        public void TakingTwice_YieldsNothingTheSecondTime()
        {
            // Taking is what stops tracking, so a caller that releases twice does not release the
            // same handle twice — which Addressables reports as an error.
            _registry.Remember(new AssetReference(GUID), default);

            _registry.Take(new AssetReference(GUID));

            CollectionAssert.IsEmpty(_registry.Take(new AssetReference(GUID)));
        }

        [Test]
        public void TakingAReferenceNothingWasLoadedFor_IsSafe()
        {
            // MinigameContainer.End and BeginAsync's unwind both release unconditionally, which
            // only works because nothing tracked yields nothing rather than failing.
            CollectionAssert.IsEmpty(_registry.Take(new AssetReference(GUID)));
            CollectionAssert.IsEmpty(_registry.Take(null));
            Assert.DoesNotThrow(() => _registry.Remember(null, default));
        }
    }
}

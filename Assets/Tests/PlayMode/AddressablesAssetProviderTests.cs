using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    // The provider's second job is translation: Addressables reports its own failure types, and
    // letting those escape would leak the loading technology into every catch site and would let a
    // test asserting "this throws" be satisfied by something unrelated.
    //
    // Play mode because there is no way to make real Addressables fail without real Addressables.
    // The sources are covered in edit mode against a fake provider; this covers the one thing a
    // fake provider cannot, which is that the real one translates what the real library throws.
    public class AddressablesAssetProviderTests
    {
        // The chests view prefab, as the Minigame.Chests group holds it. A GUID rather than an
        // address on purpose: a GUID is what an AssetReference actually carries.
        private const string CHESTS_VIEW_GUID = "fb6e7fffa2cdb4fd89d83dcbd3cf3b32";
        private const string ABSENT_GUID = "00000000000000000000000000000042";

        // The label every entry in the Minigame.Chests group carries, which is what the delivery
        // routes work in: a label names a whole minigame's content at once.
        private const string CHESTS_LABEL = "minigame.chests";

        [UnityTest]
        public IEnumerator AKeyThatIsNotInTheCatalog_SurfacesAsAMissingAsset() => UniTask.ToCoroutine(async () =>
        {
            // Addressables reports the miss through Debug.LogError before throwing, and an
            // unexpected error log fails a test on its own (notes section 4). Expecting it keeps
            // this test about the translation rather than about the library's logging.
            LogAssert.Expect(LogType.Error, new Regex("No Location found for Key=no-such-key-ships-with-this-game"));

            IAssetProvider provider = new AddressablesAssetProvider();

            MissingAssetException caught = null;
            try
            {
                await provider.LoadAsync<TextAsset>("no-such-key-ships-with-this-game", CancellationToken.None);
            }
            catch (MissingAssetException exception)
            {
                caught = exception;
            }

            Assert.IsNotNull(caught, "an unknown key has to arrive as MissingAssetException, not as an Addressables type");
            StringAssert.Contains("no-such-key-ships-with-this-game", caught.Message, "the failure has to name the key it asked for");
        });

        [UnityTest]
        public IEnumerator AKeyThatIsInTheCatalog_LoadsTheShippedAsset() => UniTask.ToCoroutine(async () =>
        {
            IAssetProvider provider = new AddressablesAssetProvider();

            TextAsset document = await provider.LoadAsync<TextAsset>("GameConfig", CancellationToken.None);

            Assert.IsNotNull(document, "the shipped config document is addressable under its own key");
            Assert.IsNotEmpty(document.text);
        });

        [UnityTest]
        public IEnumerator AReferenceToSomethingThatDoesNotShip_SurfacesAsAMissingAsset() => UniTask.ToCoroutine(async () =>
        {
            // A well-formed GUID that no entry carries: valid enough to be looked up, absent from
            // the catalog, so Addressables logs and throws exactly as it does for a bad key.
            LogAssert.Expect(LogType.Error, new Regex("No Location found for Key=" + ABSENT_GUID));

            IAssetProvider provider = new AddressablesAssetProvider();

            MissingAssetException caught = null;
            try
            {
                await provider.LoadAsync<TextAsset>(new AssetReference(ABSENT_GUID), CancellationToken.None);
            }
            catch (MissingAssetException exception)
            {
                caught = exception;
            }

            Assert.IsNotNull(caught, "an unresolvable reference has to arrive as MissingAssetException");
            StringAssert.Contains(ABSENT_GUID, caught.Message, "the failure has to name what it asked for");
        });

        [UnityTest]
        public IEnumerator AReferenceToAShippedAsset_LoadsIt() => UniTask.ToCoroutine(async () =>
        {
            // The mechanism the whole indirection rests on: a GUID string, no object reference, and
            // the asset still arrives. LoadAsync<GameObject> through a reference is what
            // MinigameContainer.BeginAsync does for every view.
            IAssetProvider provider = new AddressablesAssetProvider();
            AssetReference reference = new(CHESTS_VIEW_GUID);

            GameObject prefab = await provider.LoadAsync<GameObject>(reference, CancellationToken.None);

            Assert.IsNotNull(prefab, "the shipped chests view is addressable through its GUID");

            provider.Release(reference);
        });

        [UnityTest]
        public IEnumerator ALabelWithNothingLeftToFetch_ReportsZeroRatherThanFailing() => UniTask.ToCoroutine(async () =>
        {
            // The ordinary case, and the one the whole on-demand path depends on: content that is
            // cached, or that the running build serves locally, has nothing to come down. Reporting
            // that as a failure would put a popup in front of every start.
            IAssetProvider provider = new AddressablesAssetProvider();

            long size = await provider.GetDownloadSizeAsync(CHESTS_LABEL, CancellationToken.None);

            Assert.AreEqual(0L, size, "the chests content is not remote to a run that never built bundles");

            // And the fetch itself completes rather than failing over having nothing to do.
            await provider.DownloadAsync(CHESTS_LABEL, null, CancellationToken.None);
        });

        [UnityTest]
        public IEnumerator ALabelThatShipsWithNothing_SurfacesAsAMissingAsset() => UniTask.ToCoroutine(async () =>
        {
            // A label nobody authored is the same authoring mistake as a key nobody authored, and
            // it has to arrive typed for the same reason. Addressables logs before it throws here
            // too, so the log is expected rather than fatal.
            LogAssert.Expect(LogType.Error, new Regex("no-such-label-ships-with-this-game"));

            IAssetProvider provider = new AddressablesAssetProvider();

            MissingAssetException caught = null;
            try
            {
                await provider.GetDownloadSizeAsync("no-such-label-ships-with-this-game", CancellationToken.None);
            }
            catch (MissingAssetException exception)
            {
                caught = exception;
            }

            Assert.IsNotNull(caught, "an unknown label has to arrive as MissingAssetException, not as an Addressables type");
            StringAssert.Contains("no-such-label-ships-with-this-game", caught.Message);
        });

        [Test]
        public void ReleasingAReferenceThatWasNeverLoaded_IsSafe()
        {
            // MinigameContainer.End is documented as safe to call unconditionally, and it releases
            // whatever Begin took. Addressables warns loudly when asked to release nothing, so the
            // provider has to know it holds nothing rather than ask.
            IAssetProvider provider = new AddressablesAssetProvider();

            Assert.DoesNotThrow(() => provider.Release(new AssetReference(ABSENT_GUID)));
            Assert.DoesNotThrow(() => provider.Release(null));
        }
    }
}

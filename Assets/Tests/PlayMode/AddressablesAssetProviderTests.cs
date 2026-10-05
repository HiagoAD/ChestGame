using System;
using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// The provider's second job is translation, and play mode is the only place to cover it: there
    /// is no way to make real Addressables fail without real Addressables. The sources are covered
    /// in edit mode against a fake.
    /// </summary>
    public class AddressablesAssetProviderTests
    {
        /// <summary>
        /// The chests view prefab, as the Minigame.Chests group holds it. A GUID rather than an
        /// address.
        /// </summary>
        /// <remarks>
        /// See docs/asset-loading.md, "The seam".
        /// </remarks>
        private const string CHESTS_VIEW_GUID = "fb6e7fffa2cdb4fd89d83dcbd3cf3b32";
        private const string ABSENT_GUID = "00000000000000000000000000000042";

        /// <summary>
        /// The label every entry in the Minigame.Chests group carries.
        /// </summary>
        private const string CHESTS_LABEL = "minigame.chests";

        /// <remarks>
        /// See docs/testing.md, "Expecting the error Addressables logs before it throws".
        /// </remarks>
        [UnityTest]
        public IEnumerator AKeyThatIsNotInTheCatalog_SurfacesAsAMissingAsset() => UniTask.ToCoroutine(async () =>
        {
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

        /// <remarks>
        /// See docs/asset-loading.md, "The seam".
        /// </remarks>
        [UnityTest]
        public IEnumerator AReferenceToAShippedAsset_LoadsIt() => UniTask.ToCoroutine(async () =>
        {
            IAssetProvider provider = new AddressablesAssetProvider();
            AssetReference reference = new(CHESTS_VIEW_GUID);

            GameObject prefab = await provider.LoadAsync<GameObject>(reference, CancellationToken.None);

            Assert.IsNotNull(prefab, "the shipped chests view is addressable through its GUID");

            provider.Release(reference);
        });

        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// </remarks>
        [UnityTest]
        public IEnumerator ALabelWithNothingLeftToFetch_ReportsZeroRatherThanFailing() => UniTask.ToCoroutine(async () =>
        {
            IAssetProvider provider = new AddressablesAssetProvider();

            long size = await provider.GetDownloadSizeAsync(CHESTS_LABEL, CancellationToken.None);

            Assert.AreEqual(0L, size, "the chests content is not remote to a run that never built bundles");

            await provider.DownloadAsync(CHESTS_LABEL, null, CancellationToken.None);
        });

        /// <remarks>
        /// See docs/asset-loading.md, "Translating failures".
        /// See docs/testing.md, "Expecting the error Addressables logs before it throws".
        /// </remarks>
        [UnityTest]
        public IEnumerator ALabelThatShipsWithNothing_SurfacesAsAMissingAsset() => UniTask.ToCoroutine(async () =>
        {
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

        /// <remarks>
        /// See docs/testing.md, "AReferenceLoadCancelledBeforeItArrives_LeavesNothingLoaded, and how the probe works".
        /// </remarks>
        [UnityTest]
        public IEnumerator AReferenceLoadCancelledBeforeItArrives_LeavesNothingLoaded() => UniTask.ToCoroutine(async () =>
        {
            IAssetProvider provider = new AddressablesAssetProvider();
            AssetReference reference = new(CHESTS_VIEW_GUID);

            GameObject warmUp = await provider.LoadAsync<GameObject>(reference, CancellationToken.None);
            Assert.IsNotNull(warmUp, "the warm-up load is the same one the fixture already covers");
            provider.Release(reference);

            using CancellationTokenSource cancelled = new();
            cancelled.Cancel();

            bool unwound = false;
            try
            {
                await provider.LoadAsync<GameObject>(reference, cancelled.Token);
            }
            catch (OperationCanceledException)
            {
                unwound = true;
            }

            Assert.IsTrue(unwound, "a cancelled load has to unwind as cancellation rather than as a load failure");

            await UniTask.DelayFrame(3);

            AsyncOperationHandle<GameObject> probe = Addressables.LoadAssetAsync<GameObject>(new AssetReference(CHESTS_VIEW_GUID));
            bool answeredFromAHandleStillHeld = probe.IsDone;

            await probe.ToUniTask();
            Addressables.Release(probe);

            Assert.IsFalse(answeredFromAHandleStillHeld,
                "the cancelled load never handed its asset to anyone and still holds its ref-count, so the asset is resident for the session");
        });

        /// <remarks>
        /// See docs/minigames.md, "Teardown".
        /// See docs/asset-loading.md, "AssetHandleRegistry".
        /// </remarks>
        [Test]
        public void ReleasingAReferenceThatWasNeverLoaded_IsSafe()
        {
            IAssetProvider provider = new AddressablesAssetProvider();

            Assert.DoesNotThrow(() => provider.Release(new AssetReference(ABSENT_GUID)));
            Assert.DoesNotThrow(() => provider.Release(null));
        }
    }
}

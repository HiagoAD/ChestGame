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
    // The provider's second job is translation, and play mode is the only place to cover it: there
    // is no way to make real Addressables fail without real Addressables. The sources are covered
    // in edit mode against a fake.
    public class AddressablesAssetProviderTests
    {
        // The chests view prefab, as the Minigame.Chests group holds it. A GUID rather than an
        // address, because a GUID is what an AssetReference actually carries.
        private const string CHESTS_VIEW_GUID = "fb6e7fffa2cdb4fd89d83dcbd3cf3b32";
        private const string ABSENT_GUID = "00000000000000000000000000000042";

        // The label every entry in the Minigame.Chests group carries.
        private const string CHESTS_LABEL = "minigame.chests";

        [UnityTest]
        public IEnumerator AKeyThatIsNotInTheCatalog_SurfacesAsAMissingAsset() => UniTask.ToCoroutine(async () =>
        {
            // Addressables logs an error before throwing, and an unexpected error log fails a test
            // on its own. Expecting it keeps this about the translation.
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
            // A well-formed GUID that no entry carries: valid enough to look up, absent from the
            // catalog.
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
            // The mechanism the indirection rests on: a GUID string, no object reference, and the
            // asset still arrives. This is what MinigameContainer.BeginAsync does for every view.
            IAssetProvider provider = new AddressablesAssetProvider();
            AssetReference reference = new(CHESTS_VIEW_GUID);

            GameObject prefab = await provider.LoadAsync<GameObject>(reference, CancellationToken.None);

            Assert.IsNotNull(prefab, "the shipped chests view is addressable through its GUID");

            provider.Release(reference);
        });

        [UnityTest]
        public IEnumerator ALabelWithNothingLeftToFetch_ReportsZeroRatherThanFailing() => UniTask.ToCoroutine(async () =>
        {
            // The ordinary case the on-demand path depends on. Reporting cached or local content as
            // a failure would put a popup in front of every start.
            IAssetProvider provider = new AddressablesAssetProvider();

            long size = await provider.GetDownloadSizeAsync(CHESTS_LABEL, CancellationToken.None);

            Assert.AreEqual(0L, size, "the chests content is not remote to a run that never built bundles");

            // And the fetch itself completes rather than failing over having nothing to do.
            await provider.DownloadAsync(CHESTS_LABEL, null, CancellationToken.None);
        });

        [UnityTest]
        public IEnumerator ALabelThatShipsWithNothing_SurfacesAsAMissingAsset() => UniTask.ToCoroutine(async () =>
        {
            // A label nobody authored is the same authoring mistake as a key nobody authored.
            // Addressables logs before it throws here too.
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

        [UnityTest]
        public IEnumerator AReferenceLoadCancelledBeforeItArrives_LeavesNothingLoaded() => UniTask.ToCoroutine(async () =>
        {
            // Addressables takes the ref-count on the call, not on the await, so a token firing
            // while the bytes are still coming used to throw straight past the bookkeeping.
            // GameManager passes GetCancellationTokenOnDestroy, so leaving the scene mid-load is
            // exactly this. Play mode because the leak is a real ResourceManager's ref-count.
            IAssetProvider provider = new AddressablesAssetProvider();
            AssetReference reference = new(CHESTS_VIEW_GUID);

            // Warm up first, and let go again: an uninitialised Addressables answers every load
            // with a chained operation that never finishes on the frame it was asked for, so the
            // probe below would read "not held" no matter what.
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

            // The operation the cancelled load started is still running, and what it does on the
            // way out is the subject here.
            await UniTask.DelayFrame(3);

            // The probe. Addressables hands back an operation it already holds finished, while one
            // it does not hold has to be started and never finishes on its own frame. So "was it
            // done immediately" reads whether the cancelled load's ref-count is still outstanding,
            // which has no other observer.
            AsyncOperationHandle<GameObject> probe = Addressables.LoadAssetAsync<GameObject>(new AssetReference(CHESTS_VIEW_GUID));
            bool answeredFromAHandleStillHeld = probe.IsDone;

            await probe.ToUniTask();
            Addressables.Release(probe);

            Assert.IsFalse(answeredFromAHandleStillHeld,
                "the cancelled load never handed its asset to anyone and still holds its ref-count, so the asset is resident for the session");
        });

        [Test]
        public void ReleasingAReferenceThatWasNeverLoaded_IsSafe()
        {
            // MinigameContainer.End is safe to call unconditionally, and Addressables warns loudly
            // when asked to release nothing, so the provider has to know it holds nothing rather
            // than ask.
            IAssetProvider provider = new AddressablesAssetProvider();

            Assert.DoesNotThrow(() => provider.Release(new AssetReference(ABSENT_GUID)));
            Assert.DoesNotThrow(() => provider.Release(null));
        }
    }
}

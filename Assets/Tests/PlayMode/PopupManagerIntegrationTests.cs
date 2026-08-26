using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Currency;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Rewards;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    // PopupManager's own logic and the sources' key handling are covered in edit mode. What is left
    // here needs the engine and the shipped content: that the Addressables-backed sources resolve
    // the keys the game ships, and that a real prefab instantiates under the canvas the provider
    // builds. UnityTests throughout, because these loads really do wait.
    public class PopupManagerIntegrationTests
    {
        private GameObject _spawnedRoot;

        [TearDown]
        public void TearDown()
        {
            if (_spawnedRoot != null)
            {
                Object.Destroy(_spawnedRoot);
            }

            // The provider parents popups under a DontDestroyOnLoad canvas it creates on first use,
            // which would otherwise survive into the next test.
            foreach (PopupParent parent in Object.FindObjectsByType<PopupParent>(FindObjectsSortMode.None))
            {
                Object.Destroy(parent.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator TheAddressablesListSource_FindsTheShippedPopupList() => UniTask.ToCoroutine(async () =>
        {
            PopupCatalog catalog = new(await ShippedPopupEntries());

            CollectionAssert.IsNotEmpty(catalog.Popups);
            CollectionAssert.Contains(catalog.Popups.Keys, typeof(RewardReceivedPopup));
        });

        [UnityTest]
        public IEnumerator TheAddressablesParentSource_FindsTheShippedPrefabWithoutInstantiatingIt() =>
            UniTask.ToCoroutine(async () =>
            {
                int before = Object.FindObjectsByType<PopupParent>(FindObjectsSortMode.None).Length;

                PopupParent prefab = await ShippedParentPrefab();

                Assert.IsNotNull(prefab, "the shipped PopupParent prefab is not addressable under its key");
                Assert.AreEqual(before, Object.FindObjectsByType<PopupParent>(FindObjectsSortMode.None).Length,
                    "loading the prefab must not put a copy of it in the scene");
            });

        [UnityTest]
        public IEnumerator TheParentProvider_BuildsTheSharedCanvasOnFirstUse() => UniTask.ToCoroutine(async () =>
        {
            PopupParentProvider provider = new(await ShippedParentPrefab());

            Transform first = provider.Default;

            Assert.IsNotNull(first, "the shipped PopupParent prefab must expose a target transform");
            Assert.AreSame(first, provider.Default, "the canvas is built once and reused");
        });

        [UnityTest]
        public IEnumerator SpawningTheRealRewardPopup_ProducesALivePopup() => UniTask.ToCoroutine(async () =>
        {
            PopupManager popups = new(
                new PopupCatalog(await ShippedPopupEntries()),
                new PopupParentProvider(await ShippedParentPrefab()));

            RewardReceivedPopup popup = popups.Spawn<RewardReceivedPopup, RewardReceivedPopupData>(
                new RewardReceivedPopupData(CurrencyType.Coins, 50));
            _spawnedRoot = popup.gameObject;

            await UniTask.Yield();

            Assert.IsTrue(popup.isActiveAndEnabled);
            Assert.IsNotNull(popup.transform.parent, "popups land under the shared canvas when no parent is given");
        });

        // The real provider, not a fake: the point of these four is that the shipped keys resolve.
        private static readonly IAssetProvider AssetProvider = new AddressablesAssetProvider();

        private static UniTask<IReadOnlyList<PopupBase>> ShippedPopupEntries() =>
            new AddressablesPopupListSource(AssetProvider).ReadAsync(CancellationToken.None);

        private static UniTask<PopupParent> ShippedParentPrefab() =>
            new AddressablesPopupParentSource(AssetProvider).ReadAsync(CancellationToken.None);
    }
}

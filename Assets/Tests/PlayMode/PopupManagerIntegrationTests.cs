using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Currency;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Rewards;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    // PopupManager's own logic is covered in edit mode against a real catalog and a fake parent.
    // What is left here is the part that genuinely needs the engine and the shipped assets: that
    // the Resources-backed sources find their assets, and that a real popup prefab instantiates
    // under the canvas the provider builds from the prefab they hand back.
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

        [Test]
        public void TheResourcesListSource_FindsTheShippedPopupList()
        {
            PopupCatalog catalog = new(ShippedPopupEntries());

            CollectionAssert.IsNotEmpty(catalog.Popups);
            CollectionAssert.Contains(catalog.Popups.Keys, typeof(RewardReceivedPopup));
        }

        [Test]
        public void TheResourcesParentSource_FindsTheShippedPrefabWithoutInstantiatingIt()
        {
            int before = Object.FindObjectsByType<PopupParent>(FindObjectsSortMode.None).Length;

            PopupParent prefab = ShippedParentPrefab();

            Assert.IsNotNull(prefab, "the shipped PopupParent prefab is missing from Resources");
            Assert.AreEqual(before, Object.FindObjectsByType<PopupParent>(FindObjectsSortMode.None).Length,
                "loading the prefab must not put a copy of it in the scene");
        }

        [Test]
        public void TheParentProvider_BuildsTheSharedCanvasOnFirstUse()
        {
            PopupParentProvider provider = new(ShippedParentPrefab());

            Transform first = provider.Default;

            Assert.IsNotNull(first, "the shipped PopupParent prefab must expose a target transform");
            Assert.AreSame(first, provider.Default, "the canvas is built once and reused");
        }

        [UnityTest]
        public IEnumerator SpawningTheRealRewardPopup_ProducesALivePopup()
        {
            PopupManager popups = new(
                new PopupCatalog(ShippedPopupEntries()),
                new PopupParentProvider(ShippedParentPrefab()));

            RewardReceivedPopup popup = popups.Spawn<RewardReceivedPopup, RewardReceivedPopupData>(
                new RewardReceivedPopupData(CurrencyType.Coins, 50));
            _spawnedRoot = popup.gameObject;

            yield return null;

            Assert.IsTrue(popup.isActiveAndEnabled);
            Assert.IsNotNull(popup.transform.parent, "popups land under the shared canvas when no parent is given");
        }

        private static IReadOnlyList<PopupBase> ShippedPopupEntries() =>
            SynchronousUniTask.Result(new ResourcesPopupListSource().ReadAsync(CancellationToken.None));

        private static PopupParent ShippedParentPrefab() =>
            SynchronousUniTask.Result(new ResourcesPopupParentSource().ReadAsync(CancellationToken.None));
    }
}

using System.Collections;
using Company.ChestGame.Currency;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Rewards;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.PlayMode
{
    // PopupManager's own logic is covered in edit mode against a fake catalog. What is left here is
    // the part that genuinely needs the engine and the shipped assets: that the Resources-backed
    // catalog and parent provider find their assets, and that a real popup prefab instantiates.
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
        public void TheResourcesCatalog_FindsTheShippedPopupList()
        {
            ResourcesPopupCatalog catalog = new();

            CollectionAssert.IsNotEmpty(catalog.Popups);
            CollectionAssert.Contains(catalog.Popups.Keys, typeof(RewardReceivedPopup));
        }

        [Test]
        public void TheResourcesParentProvider_BuildsTheSharedCanvasOnFirstUse()
        {
            ResourcesPopupParentProvider provider = new();

            Transform first = provider.Default;

            Assert.IsNotNull(first, "the shipped PopupParent prefab must expose a target transform");
            Assert.AreSame(first, provider.Default, "the canvas is built once and reused");
        }

        [UnityTest]
        public IEnumerator SpawningTheRealRewardPopup_ProducesALivePopup()
        {
            PopupManager popups = new(new ResourcesPopupCatalog(), new ResourcesPopupParentProvider());

            RewardReceivedPopup popup = popups.Spawn<RewardReceivedPopup, RewardReceivedPopupData>(
                new RewardReceivedPopupData(CurrencyType.Coins, 50));
            _spawnedRoot = popup.gameObject;

            yield return null;

            Assert.IsTrue(popup.isActiveAndEnabled);
            Assert.IsNotNull(popup.transform.parent, "popups land under the shared canvas when no parent is given");
        }
    }
}

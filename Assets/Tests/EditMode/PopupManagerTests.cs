using System.Collections.Generic;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    // PopupManager is a catalog lookup, a parent choice, and a hand-off of data. With the catalog
    // and the parent supplied rather than loaded, all three are reachable here instead of in the
    // play-mode suite. Only spawning the real shipped prefabs still needs play mode.
    public class PopupManagerTests
    {
        private PopupCatalog _catalog;
        private FakePopupParentProvider _parentProvider;
        private PopupManager _popups;

        private TestPopup _prefab;
        private GameObject _defaultParent;
        private readonly List<GameObject> _spawned = new();

        [SetUp]
        public void SetUp()
        {
            _prefab = new GameObject("TestPopupPrefab").AddComponent<TestPopup>();
            _defaultParent = new GameObject("DefaultParent");

            // The real catalog rather than a fake: it takes a plain list, so using it costs
            // nothing and keeps this test honest about how lookups actually resolve.
            _catalog = new PopupCatalog(new List<PopupBase> { _prefab });
            _parentProvider = new FakePopupParentProvider { Parent = _defaultParent.transform };

            _popups = new PopupManager(_catalog, _parentProvider);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject spawned in _spawned)
            {
                if (spawned != null) Object.DestroyImmediate(spawned);
            }
            _spawned.Clear();

            Object.DestroyImmediate(_prefab.gameObject);
            Object.DestroyImmediate(_defaultParent);
        }

        private TestPopup Spawn(TestPopupData data = null, Transform parent = null)
        {
            TestPopup popup = _popups.Spawn<TestPopup, TestPopupData>(data, parent);
            if (popup != null) _spawned.Add(popup.gameObject);
            return popup;
        }

        [Test]
        public void Spawn_InstantiatesTheCatalogedPrefab()
        {
            TestPopup popup = Spawn(new TestPopupData());

            Assert.IsNotNull(popup);
            Assert.AreNotSame(_prefab, popup, "a fresh instance, not the prefab itself");
        }

        [Test]
        public void Spawn_HandsThePopupItsDataAndInitialisesIt()
        {
            TestPopupData data = new();

            TestPopup popup = Spawn(data);

            Assert.AreEqual(1, popup.InitializeCount);
            Assert.AreSame(data, popup.ReceivedData);
        }

        [Test]
        public void Spawn_WithoutAParent_UsesTheProvidersDefault()
        {
            TestPopup popup = Spawn(new TestPopupData());

            Assert.AreSame(_defaultParent.transform, popup.transform.parent);
            Assert.AreEqual(1, _parentProvider.DefaultAccessCount);
        }

        [Test]
        public void Spawn_WithAnExplicitParent_UsesItAndLeavesTheDefaultUntouched()
        {
            GameObject explicitParent = new("ExplicitParent");
            _spawned.Add(explicitParent);

            TestPopup popup = Spawn(new TestPopupData(), explicitParent.transform);

            Assert.AreSame(explicitParent.transform, popup.transform.parent);
            Assert.AreEqual(0, _parentProvider.DefaultAccessCount,
                "an explicit parent must not force the shared canvas into existence");
        }

        [Test]
        public void Spawn_ForAPopupTheCatalogDoesNotList_ThrowsPopupNotFound()
        {
            PopupNotFoundException error = Assert.Throws<PopupNotFoundException>(
                () => _popups.Spawn<UncatalogedPopup, TestPopupData>(new TestPopupData()));

            Assert.AreEqual(typeof(UncatalogedPopup), error.PopupType);
        }

        [Test]
        public void AFailedSpawn_NeverReachesTheParentProvider()
        {
            Assert.Throws<PopupNotFoundException>(
                () => _popups.Spawn<UncatalogedPopup, TestPopupData>(new TestPopupData()));

            Assert.AreEqual(0, _parentProvider.DefaultAccessCount);
        }

        public class TestPopupData : PopupDataBase { }

        public class TestPopup : PopupBase<TestPopup, TestPopupData>
        {
            public int InitializeCount { get; private set; }
            public TestPopupData ReceivedData { get; private set; }

            protected override void OnInitialize()
            {
                InitializeCount++;
                ReceivedData = Data;
            }
        }

        public class UncatalogedPopup : PopupBase<UncatalogedPopup, TestPopupData> { }
    }
}

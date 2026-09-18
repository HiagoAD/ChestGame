using System;
using System.Collections;
using System.Collections.Generic;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Tests.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Covers that <see cref="PopupManager"/>, not the popup itself, destroys a spawned popup's
    /// <see cref="GameObject"/> once it asks to be closed.
    /// </summary>
    /// <remarks>
    /// Runs in play mode because <see cref="Object.Destroy(Object)"/> is deferred to the end of the
    /// frame. See docs/testing.md, "What lives where".
    /// </remarks>
    public class PopupManagerCloseRequestTests
    {
        private GameObject _prefabObject;
        private GameObject _defaultParent;

        [TearDown]
        public void TearDown()
        {
            if (_prefabObject != null) Object.Destroy(_prefabObject);
            if (_defaultParent != null) Object.Destroy(_defaultParent);
        }

        [UnityTest]
        public IEnumerator ARequestedClose_DestroysTheSpawnedPopup() => UniTask.ToCoroutine(async () =>
        {
            TestPopup prefab = new GameObject("TestPopupPrefab").AddComponent<TestPopup>();
            _prefabObject = prefab.gameObject;
            _defaultParent = new GameObject("DefaultParent");

            PopupManager popups = new(
                new PopupCatalog(new List<PopupBase> { prefab }),
                new FakePopupParentProvider { Parent = _defaultParent.transform });

            TestPopup popup = popups.Spawn<TestPopup, TestPopupData>(new TestPopupData());

            popup.RequestCloseForTest();
            await UniTask.Yield();

            Assert.IsTrue(popup == null, "PopupManager must destroy a popup that asks to be closed");
        });

        [UnityTest]
        public IEnumerator APopupWhoseInitializeThrew_CanStillBeClosed() => UniTask.ToCoroutine(async () =>
        {
            ThrowingPopup prefab = new GameObject("ThrowingPopupPrefab").AddComponent<ThrowingPopup>();
            _prefabObject = prefab.gameObject;
            _defaultParent = new GameObject("DefaultParent");

            PopupManager popups = new(
                new PopupCatalog(new List<PopupBase> { prefab }),
                new FakePopupParentProvider { Parent = _defaultParent.transform });

            Assert.Throws<InvalidOperationException>(
                () => popups.Spawn<ThrowingPopup, TestPopupData>(new TestPopupData()),
                "guard: this popup's OnInitialize is supposed to throw");

            ThrowingPopup popup = _defaultParent.GetComponentInChildren<ThrowingPopup>();
            Assert.IsNotNull(popup, "guard: the popup was instantiated before its Initialize threw");

            popup.RequestCloseForTest();
            await UniTask.Yield();

            Assert.IsTrue(popup == null,
                "a popup whose Initialize threw must still be dismissable, or a wiring mistake leaves "
                + "an undismissable popup over the game");
        });

        private class TestPopupData : PopupDataBase { }

        private class TestPopup : PopupBase<TestPopup, TestPopupData>
        {
            public void RequestCloseForTest() => RequestClose();
        }

        private class ThrowingPopup : PopupBase<ThrowingPopup, TestPopupData>
        {
            public void RequestCloseForTest() => RequestClose();

            protected override void OnInitialize() => throw new InvalidOperationException("initialize failed");
        }
    }
}

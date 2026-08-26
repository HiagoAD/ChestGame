using System;
using System.Collections;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Tests.Common;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.TestTools;
using VContainer;
// System brings a second Object with it; the alias keeps every Object.Destroy meaning what it did.
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    // The stop half of the framework: End disposes the controller, destroys the view, releases the
    // handles, and leaves the container safe to tear down again. Play mode because Object.Destroy
    // only takes effect there. The provider is still a fake, since this is about the container's
    // lifecycle rather than Addressables.
    public class MinigameContainerLifecycleTests
    {
        private const string VIEW_GUID = "33333333333333333333333333333333";
        private const string CONTENT_GUID = "44444444444444444444444444444444";
        private const string REJECTING_GUID = "55555555555555555555555555555555";

        private IObjectResolver _container;
        private FakeAssetProvider _assets;
        private FakeMinigameSO _definition;
        private FakeMinigameController _controller;
        private TestMinigameView _viewRef;
        private AssetReferenceGameObject _viewReference;
        private AssetReference _contentReference;
        private GameObject _parent;
        private MinigameContainer _minigame;

        [SetUp]
        public void SetUp()
        {
            _viewRef = new GameObject("ViewPrefab").AddComponent<TestMinigameView>();
            _parent = new GameObject("MinigameParent");

            // A GUID string is all an AssetReference is, so no real addressable asset is needed.
            _viewReference = new AssetReferenceGameObject(VIEW_GUID);
            _assets = new FakeAssetProvider().With(_viewReference, _viewRef.gameObject);

            ContainerBuilder builder = new();
            builder.RegisterInstance<IAssetProvider>(_assets);
            _container = builder.Build();

            _contentReference = new AssetReference(CONTENT_GUID);
            _definition = FakeMinigameSO.Create();
            _definition.ContentReference = _contentReference;
            _controller = new FakeMinigameController();
            _minigame = new MinigameContainer();
            _container.Inject(_minigame);
            _minigame.Set(_controller, _viewReference, _definition);
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
            if (_definition != null) Object.DestroyImmediate(_definition);
            if (_viewRef != null) Object.Destroy(_viewRef.gameObject);
            if (_parent != null) Object.Destroy(_parent);
        }

        [Test]
        public void ANewMinigame_IsNotRunningUntilItBegins()
        {
            Assert.IsFalse(_minigame.Running);
            Assert.IsNull(_minigame.ViewInstance);
        }

        [UnityTest]
        public IEnumerator BeginAsync_InstantiatesTheViewAndHandsItTheController() => UniTask.ToCoroutine(async () =>
        {
            await _minigame.BeginAsync(_parent.transform, CancellationToken.None);

            Assert.IsTrue(_minigame.Running);
            Assert.AreEqual(1, _controller.InjectCalls, "the container is what injects the controller now");
            Assert.IsNotNull(_minigame.ViewInstance);
            Assert.AreNotSame(_viewRef, _minigame.ViewInstance, "a fresh instance, not the prefab");
            Assert.AreSame(_controller, ((TestMinigameView)_minigame.ViewInstance).Controller);
            Assert.AreSame(_parent.transform, _minigame.ViewInstance.transform.parent);
        });

        [UnityTest]
        public IEnumerator BeginAsync_WhenTheViewRejectsTheController_LeavesNoOrphanBehind() =>
            UniTask.ToCoroutine(async () =>
        {
            // The view is instantiated before SetController runs and _running is set after it, so a
            // throw from SetController lands in the catch with a live GameObject already in the
            // scene. End returns early while _running is false, so if the catch does not destroy
            // it, nothing does.
            TestMinigameView rejecting = new GameObject("RejectingViewPrefab").AddComponent<RejectingView>();
            AssetReferenceGameObject rejectingRef = new(REJECTING_GUID);
            _assets.With(rejectingRef, rejecting.gameObject);

            MinigameContainer minigame = new();
            _container.Inject(minigame);
            minigame.Set(_controller, rejectingRef, _definition);

            try
            {
                await minigame.BeginAsync(_parent.transform, CancellationToken.None);
                Assert.Fail("SetController threw, so BeginAsync had to rethrow");
            }
            catch (InvalidOperationException)
            {
                // The rejection itself. What matters is what the catch left behind.
            }

            Assert.IsFalse(minigame.Running, "a start that threw did not start anything");
            Assert.IsNull(minigame.ViewInstance, "the container must not still be holding the instance");

            await UniTask.Yield();

            Assert.AreEqual(0, _parent.transform.childCount,
                "the instance the failed start created has to be destroyed, not orphaned in the scene");

            Object.Destroy(rejecting.gameObject);
        });

        [UnityTest]
        public IEnumerator End_DisposesTheControllerAndDestroysTheView() => UniTask.ToCoroutine(async () =>
        {
            await _minigame.BeginAsync(_parent.transform, CancellationToken.None);
            GameObject viewObject = _minigame.ViewInstance.gameObject;

            _minigame.End();

            Assert.IsFalse(_minigame.Running);
            Assert.IsTrue(_controller.Disposed, "the controller must be disposed, not just dropped");

            await UniTask.Yield();

            Assert.IsTrue(viewObject == null, "the view GameObject is destroyed");
        });

        [UnityTest]
        public IEnumerator End_ReleasesWhatBeginLoaded() => UniTask.ToCoroutine(async () =>
        {
            // Handles, not instances: releasing the loaded asset rather than the instantiated
            // object is what lets End stay synchronous.
            await _minigame.BeginAsync(_parent.transform, CancellationToken.None);

            _minigame.End();

            CollectionAssert.AreEquivalent(new AssetReference[] { _viewReference, _contentReference },
                _assets.ReleasedReferences,
                "the view and the minigame's own content both have to be let go of");
        });

        [Test]
        public void End_OnAMinigameThatNeverBegan_IsSafe()
        {
            Assert.DoesNotThrow(() => _minigame.End());
            Assert.IsFalse(_controller.Disposed, "nothing was started, so nothing needed disposing");
            CollectionAssert.IsEmpty(_assets.ReleasedReferences, "nothing was loaded, so nothing is released");
        }

        [UnityTest]
        public IEnumerator End_Twice_DisposesTheControllerOnlyOnce() => UniTask.ToCoroutine(async () =>
        {
            await _minigame.BeginAsync(_parent.transform, CancellationToken.None);

            _minigame.End();
            _minigame.End();

            Assert.IsFalse(_minigame.Running);
            Assert.AreEqual(1, _controller.DisposeCalls);
            Assert.AreEqual(1, _definition.ReleaseContentCalls, "and releases its content only once");
        });

        // Stands in for any view whose SetController fails. What it throws does not matter; that it
        // throws after the instance exists is the scenario.
        private class RejectingView : TestMinigameView
        {
            public override void SetController(MinigameControllerBase controller) =>
                throw new InvalidOperationException("this view refuses its controller");
        }

        private class TestMinigameView : MinigameViewBase
        {
            public MinigameControllerBase Controller { get; private set; }

            public override void SetController(MinigameControllerBase controller) => Controller = controller;
        }
    }
}

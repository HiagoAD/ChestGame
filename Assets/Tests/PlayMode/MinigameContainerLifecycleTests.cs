using System.Collections;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using VContainer;

namespace Company.ChestGame.Tests.PlayMode
{
    // The stop half of the minigame framework. Begin instantiates a view through the container and
    // End has to undo all of it: dispose the controller, destroy the view, and leave the container
    // safe to tear down again. Needs play mode because Object.Destroy only takes effect there.
    public class MinigameContainerLifecycleTests
    {
        private IObjectResolver _container;
        private FakeMinigameController _controller;
        private TestMinigameView _viewRef;
        private GameObject _parent;
        private MinigameContainer _minigame;

        [SetUp]
        public void SetUp()
        {
            ContainerBuilder builder = new();
            _container = builder.Build();

            _viewRef = new GameObject("ViewPrefab").AddComponent<TestMinigameView>();
            _parent = new GameObject("MinigameParent");

            _controller = new FakeMinigameController();
            _minigame = new MinigameContainer();
            _container.Inject(_minigame);
            _minigame.Set(_controller, _viewRef);
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
            if (_viewRef != null) Object.Destroy(_viewRef.gameObject);
            if (_parent != null) Object.Destroy(_parent);
        }

        [Test]
        public void ANewMinigame_IsNotRunningUntilItBegins()
        {
            Assert.IsFalse(_minigame.Running);
            Assert.IsNull(_minigame.ViewInstance);
        }

        [Test]
        public void Begin_InstantiatesTheViewAndHandsItTheController()
        {
            _minigame.Begin(_parent.transform);

            Assert.IsTrue(_minigame.Running);
            Assert.IsNotNull(_minigame.ViewInstance);
            Assert.AreNotSame(_viewRef, _minigame.ViewInstance, "a fresh instance, not the prefab");
            Assert.AreSame(_controller, ((TestMinigameView)_minigame.ViewInstance).Controller);
            Assert.AreSame(_parent.transform, _minigame.ViewInstance.transform.parent);
        }

        [UnityTest]
        public IEnumerator End_DisposesTheControllerAndDestroysTheView()
        {
            _minigame.Begin(_parent.transform);
            GameObject viewObject = _minigame.ViewInstance.gameObject;

            _minigame.End();

            Assert.IsFalse(_minigame.Running);
            Assert.IsTrue(_controller.Disposed, "the controller must be disposed, not just dropped");

            yield return null;

            Assert.IsTrue(viewObject == null, "the view GameObject is destroyed");
        }

        [Test]
        public void End_OnAMinigameThatNeverBegan_IsSafe()
        {
            Assert.DoesNotThrow(() => _minigame.End());
            Assert.IsFalse(_controller.Disposed, "nothing was started, so nothing needed disposing");
        }

        [Test]
        public void End_Twice_DisposesTheControllerOnlyOnce()
        {
            _minigame.Begin(_parent.transform);

            _minigame.End();
            _minigame.End();

            Assert.IsFalse(_minigame.Running);
            Assert.AreEqual(1, _controller.DisposeCalls);
        }

        private class TestMinigameView : MinigameViewBase
        {
            public MinigameControllerBase Controller { get; private set; }

            public override void SetController(MinigameControllerBase controller) => Controller = controller;
        }
    }
}

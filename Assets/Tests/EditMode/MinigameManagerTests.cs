using System;
using System.Collections.Generic;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Minigame.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using VContainer;

namespace Company.ChestGame.Tests.EditMode
{
    // What is worth pinning about MinigameManager: it builds the right container type, injects it,
    // and fails clearly when asked for a minigame it does not know about.
    public class MinigameManagerTests
    {
        private MinigameCatalog _catalog;
        private FakeMinigameSO _minigameSO;
        private IObjectResolver _container;
        private MinigameManager _manager;

        [SetUp]
        public void SetUp()
        {
            // The real catalog rather than a fake, because it is constructible from a plain list.
            _minigameSO = FakeMinigameSO.Create();
            _catalog = new MinigameCatalog(new List<MinigameBaseSO> { _minigameSO });

            ContainerBuilder builder = new();
            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);

            // The container reaches its content through the provider, so a resolver that cannot
            // supply one cannot inject it.
            builder.RegisterInstance<IAssetProvider>(new FakeAssetProvider());
            _container = builder.Build();

            _manager = new MinigameManager(_container, _catalog);
        }

        [TearDown]
        public void TearDown()
        {
            _container?.Dispose();
            if (_minigameSO != null)
            {
                UnityEngine.Object.DestroyImmediate(_minigameSO);
            }
        }

        [Test]
        public void Get_BuildsTheContainerRegisteredForThatType()
        {
            FakeMinigameContainer minigame = _manager.Get<FakeMinigameContainer>();

            Assert.IsNotNull(minigame);
            Assert.AreEqual(1, _minigameSO.ContainersCreated);
        }

        [Test]
        public void Get_ReturnsAContainerCarryingItsController()
        {
            FakeMinigameContainer minigame = _manager.Get<FakeMinigameContainer>();

            Assert.IsInstanceOf<FakeMinigameController>(minigame.ControllerInstance);
        }

        [Test]
        public void Get_HandsBackAFreshInstanceEachTime()
        {
            // Each request is a new game session; sharing one container would leak the previous
            // round's controller state.
            FakeMinigameContainer first = _manager.Get<FakeMinigameContainer>();
            FakeMinigameContainer second = _manager.Get<FakeMinigameContainer>();

            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first.ControllerInstance, second.ControllerInstance);
            Assert.AreEqual(2, _minigameSO.ContainersCreated);
        }

        [Test]
        public void Get_LeavesTheNewContainerNotRunning()
        {
            FakeMinigameContainer minigame = _manager.Get<FakeMinigameContainer>();

            Assert.IsFalse(minigame.Running, "a minigame only starts running once Begin is called");
        }

        [Test]
        public void GetById_BuildsTheContainerRegisteredForThatId()
        {
            // The id path is what the shell uses, so it has to reach the same construction the
            // typed path does.
            MinigameContainer minigame = _manager.Get("fake");

            Assert.IsInstanceOf<FakeMinigameContainer>(minigame);
            Assert.IsInstanceOf<FakeMinigameController>(minigame.ControllerInstance);
            Assert.AreEqual(1, _minigameSO.ContainersCreated);
        }

        [Test]
        public void GetById_ForAnUnknownId_ThrowsMinigameNotFound()
        {
            MinigameNotFoundException error = Assert.Throws<MinigameNotFoundException>(
                () => _manager.Get("no-such-minigame"));

            Assert.AreEqual("no-such-minigame", error.Id);
        }

        [Test]
        public void Get_BuildsTheContainerWithoutConfiguringOrLoadingAnything()
        {
            // Get builds and injects the container and stops there, because a definition names its
            // content rather than holding it. The hook and the controller's injection live in
            // BeginAsync, pinned by MinigameContainerContentTests.
            FakeMinigameContainer minigame = _manager.Get<FakeMinigameContainer>();
            FakeMinigameController controller = (FakeMinigameController)minigame.ControllerInstance;

            Assert.AreEqual(0, _minigameSO.ConfigureCalls,
                "configuring the controller belongs to Begin, where its content actually arrives");
            Assert.AreEqual(0, controller.InjectCalls,
                "and so does injecting it, or it would be injected before its own content existed");
        }

        [Test]
        public void Get_ForAnUnknownMinigame_ThrowsMinigameNotFound()
        {
            // Typed so this cannot be satisfied by an unrelated NullReferenceException inside Get.
            MinigameNotFoundException error = Assert.Throws<MinigameNotFoundException>(
                () => _manager.Get<UnregisteredMinigameContainer>());

            Assert.AreEqual(typeof(UnregisteredMinigameContainer), error.ContainerType);
        }

        [Test]
        public void Get_OnAnEmptyCatalog_ThrowsMinigameNotFound()
        {
            MinigameManager empty = new(_container, new MinigameCatalog(new List<MinigameBaseSO>()));

            Assert.Throws<MinigameNotFoundException>(() => empty.Get<FakeMinigameContainer>());
        }

        private class UnregisteredMinigameContainer : MinigameContainer { }
    }
}

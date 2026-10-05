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
    /// <summary>
    /// Covers what is worth pinning about MinigameManager: it builds the right container type,
    /// injects it, and fails clearly when asked for a minigame it does not know about.
    /// </summary>
    public class MinigameManagerTests
    {
        private MinigameCatalog _catalog;
        private FakeMinigameSO _minigameSO;
        private IObjectResolver _container;
        private MinigameManager _manager;

        /// <remarks>
        /// See docs/testing.md, "The save suites never touch a real save".
        /// See docs/testing.md, "What the minigame fixtures choose not to fake".
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            _minigameSO = FakeMinigameSO.Create();
            _catalog = new MinigameCatalog(new List<MinigameBaseSO> { _minigameSO });

            ContainerBuilder builder = new();
            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);

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

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        [Test]
        public void Get_HandsBackAFreshInstanceEachTime()
        {
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

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        [Test]
        public void GetById_BuildsTheContainerRegisteredForThatId()
        {
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

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        [Test]
        public void Get_BuildsTheContainerWithoutConfiguringOrLoadingAnything()
        {
            FakeMinigameContainer minigame = _manager.Get<FakeMinigameContainer>();
            FakeMinigameController controller = (FakeMinigameController)minigame.ControllerInstance;

            Assert.AreEqual(0, _minigameSO.ConfigureCalls,
                "configuring the controller belongs to Begin, where its content actually arrives");
            Assert.AreEqual(0, controller.InjectCalls,
                "and so does injecting it, or it would be injected before its own content existed");
        }

        /// <remarks>
        /// See docs/architecture.md, "Exception hierarchy".
        /// </remarks>
        [Test]
        public void Get_ForAnUnknownMinigame_ThrowsMinigameNotFound()
        {
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

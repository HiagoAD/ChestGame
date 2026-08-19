using System;
using System.Collections.Generic;
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
    // MinigameManager is the generic framework the project leans on, so the behaviour worth pinning
    // is that it builds the right container type, injects it, and fails clearly when asked for a
    // minigame it does not know about.
    public class MinigameManagerTests
    {
        private MinigameCatalog _catalog;
        private FakeMinigameSO _minigameSO;
        private IObjectResolver _container;
        private MinigameManager _manager;

        [SetUp]
        public void SetUp()
        {
            // The real catalog rather than a fake: it is constructible from a plain list, so
            // using it costs nothing and keeps this test honest about how lookups actually resolve.
            _minigameSO = FakeMinigameSO.Create();
            _catalog = new MinigameCatalog(new List<MinigameBaseSO> { _minigameSO });

            ContainerBuilder builder = new();
            builder.Register<IRandomProvider, UnityRandomProvider>(Lifetime.Singleton);
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
            // Each request is a new game session; sharing one container across sessions would leak
            // the previous round's controller state.
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
            // The id path is what the game shell uses, so it has to reach the same construction the
            // typed path does without the caller ever naming a container type.
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
        public void Get_ConfiguresTheControllerBeforeInjectingIt()
        {
            // A framework contract, not an accident of ordering: ConfigureController runs inside
            // GetMinigameContainer and Get injects afterwards, so a controller can build state
            // from its own config document and still be injected on top of it. ChestsMinigameSO
            // depends on exactly this, because the chest list is sized from ChestCount.
            ConfigurableMinigameSO definition = ScriptableObject.CreateInstance<ConfigurableMinigameSO>();
            try
            {
                MinigameManager manager = new(_container,
                    new MinigameCatalog(new List<MinigameBaseSO> { definition }));

                ConfigurableContainer minigame = manager.Get<ConfigurableContainer>();
                ConfigurableController controller = (ConfigurableController)minigame.ControllerInstance;

                Assert.IsTrue(controller.Configured, "the ConfigureController hook should have run");
                Assert.IsTrue(controller.Injected, "the controller should still be injected");
                Assert.IsTrue(controller.WasConfiguredBeforeInject,
                    "Configure has to land before Inject, or a controller cannot build state from its own config");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void Get_ForAnUnknownMinigame_ThrowsMinigameNotFound()
        {
            // Typed so this cannot be satisfied by an unrelated NullReferenceException from
            // somewhere inside Get.
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

        // Built on the generic base rather than on MinigameBaseSO directly, so the hook and the
        // order it runs in are exercised through the real GetMinigameContainer, not a stand-in.
        private class ConfigurableMinigameSO
            : MinigameBase<ConfigurableController, ConfigurableView, ConfigurableContainer>
        {
            protected override void ConfigureController(ConfigurableController controller) =>
                controller.Configure();
        }

        private class ConfigurableContainer : MinigameContainer { }

        private class ConfigurableView : MinigameViewBase
        {
            public override void SetController(MinigameControllerBase controller) { }
        }

        private class ConfigurableController : MinigameControllerBase
        {
            public bool Configured { get; private set; }
            public bool Injected { get; private set; }
            public bool WasConfiguredBeforeInject { get; private set; }

            public void Configure() => Configured = true;

            [Inject]
            public void Inject(IRandomProvider random)
            {
                WasConfiguredBeforeInject = Configured;
                Injected = true;
            }

            public override void NewGame() { }

            public override void Dispose() { }
        }
    }
}

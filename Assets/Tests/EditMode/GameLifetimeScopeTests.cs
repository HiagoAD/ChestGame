using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Core;
using Company.ChestGame.Currency;
using Company.ChestGame.Minigame;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Rewards;
using NUnit.Framework;
using TapNation.Modules.ResourceBank.Saving;
using UnityEngine;
using VContainer;

namespace Company.ChestGame.Tests.EditMode
{
    // These run against GameLifetimeScope.RegisterServices itself rather than a copy of it, so
    // dropping a registration from the composition root fails here.
    public class GameLifetimeScopeTests
    {
        private ContainerBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            _builder = new ContainerBuilder();
            GameLifetimeScope.RegisterServices(_builder);
        }

        [Test]
        public void RegisterServices_RegistersEveryServiceTheGameResolves()
        {
            Assert.IsTrue(_builder.Exists(typeof(IRandomProvider), true), nameof(IRandomProvider));
            Assert.IsTrue(_builder.Exists(typeof(IGameClock), true), nameof(IGameClock));
            Assert.IsTrue(_builder.Exists(typeof(IGameConfigSource), true), nameof(IGameConfigSource));
            Assert.IsTrue(_builder.Exists(typeof(IMinigameCatalog), true), nameof(IMinigameCatalog));
            Assert.IsTrue(_builder.Exists(typeof(IResourceBankSaveHandler<CurrencyType>), true), "IResourceBankSaveHandler<CurrencyType>");
            Assert.IsTrue(_builder.Exists(typeof(ICurrencyManager), true), nameof(ICurrencyManager));
            Assert.IsTrue(_builder.Exists(typeof(IGameConfig), true), nameof(IGameConfig));
            Assert.IsTrue(_builder.Exists(typeof(IRewardsManager), true), nameof(IRewardsManager));
            Assert.IsTrue(_builder.Exists(typeof(IPopupCatalog), true), nameof(IPopupCatalog));
            Assert.IsTrue(_builder.Exists(typeof(IPopupParentProvider), true), nameof(IPopupParentProvider));
            Assert.IsTrue(_builder.Exists(typeof(IPopupManager), true), nameof(IPopupManager));
            Assert.IsTrue(_builder.Exists(typeof(IMinigameManager), true), nameof(IMinigameManager));
        }

        [Test]
        public void EveryServiceTheGameResolves_HasASatisfiableObjectGraph()
        {
            // Exists() only proves a line of code was written. These two are the services whose
            // constructors reach outside themselves, so resolving them is the assertion that
            // matters: PopupManager needs a catalog and a parent provider, MinigameManager needs a
            // catalog and the container's own IObjectResolver.
            using IObjectResolver container = _builder.Build();

            Assert.IsInstanceOf<PopupManager>(container.Resolve<IPopupManager>());
            Assert.IsInstanceOf<MinigameManager>(container.Resolve<IMinigameManager>());
            Assert.IsInstanceOf<RewardsManager>(container.Resolve<IRewardsManager>());
        }

        [Test]
        public void ResolvingPopupManager_DoesNotCreateTheSharedCanvasYet()
        {
            // The parent canvas is DontDestroyOnLoad, so building it during resolution would leak a
            // scene object into every consumer of the container, tests included.
            using IObjectResolver container = _builder.Build();

            container.Resolve<IPopupManager>();

            Assert.IsEmpty(UnityEngine.Object.FindObjectsByType<PopupParent>(FindObjectsSortMode.None));
        }

        [Test]
        public void EveryEngineFacingSeam_HasAProductionImplementation()
        {
            // The seams exist so tests can substitute them; the real game still has to get a real
            // clock and a real random source out of the container.
            using IObjectResolver container = _builder.Build();

            Assert.IsInstanceOf<UnityRandomProvider>(container.Resolve<IRandomProvider>());
            Assert.IsInstanceOf<UnityGameClock>(container.Resolve<IGameClock>());
            Assert.IsInstanceOf<ResourcesGameConfigSource>(container.Resolve<IGameConfigSource>());
            Assert.IsInstanceOf<DefaultResourceBankSaveHandle<CurrencyType>>(
                container.Resolve<IResourceBankSaveHandler<CurrencyType>>());
        }

        [Test]
        public void CurrencyManager_ResolvesWithTheRegisteredSaveHandler()
        {
            // CurrencyManager takes its save handler as its only constructor argument, so this
            // fails outright if the scope stops registering one.
            using IObjectResolver container = _builder.Build();

            // Deliberately no assertion on balances: a container-built CurrencyManager reads the
            // real PlayerPrefs save, whose contents belong to whoever is running the tests.
            Assert.IsInstanceOf<CurrencyManager>(container.Resolve<ICurrencyManager>());
        }

        [Test]
        public void GameConfig_ResolvesAndParsesTheShippedConfigDocument()
        {
            // Reaches the real Resources/Data.json through the registered source, which makes this
            // the one test that would catch the shipped config going missing or malformed.
            using IObjectResolver container = _builder.Build();

            IGameConfig config = container.Resolve<IGameConfig>();

            Assert.IsTrue(config.Initialized);
            Assert.Greater(config.ChestCount, 0);
            Assert.Greater(config.AttempsCount, 0);
            Assert.Greater(config.TimeToOpenChestMiliseconds, 0);
        }

        [Test]
        public void MinigameCatalog_ResolvesAndListsTheShippedMinigames()
        {
            using IObjectResolver container = _builder.Build();

            IMinigameCatalog catalog = container.Resolve<IMinigameCatalog>();

            CollectionAssert.IsNotEmpty(catalog.Minigames);
        }
    }
}

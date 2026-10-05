using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Config;
using Company.ChestGame.Core;
using Company.ChestGame.Currency;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Minigame.Internal;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Rewards;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using Company.ChestGame.UI;
using NUnit.Framework;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Runs against GameLifetimeScope's own registration methods rather than a copy, so a dropped
    /// registration in the composition root fails here.
    /// </summary>
    /// <remarks>
    /// The bare SetUp registration carries no currency overrides. A test that resolves
    /// ICurrencyManager, the currency save handler or the currency scheduler instead builds its own
    /// container through IsolatedCurrencyOverrides().
    /// See docs/architecture.md, "Boot".
    /// See docs/architecture.md, "Registration, in two halves".
    /// See docs/saving.md, "Redirecting this composition away from a developer's real save".
    /// </remarks>
    public class GameLifetimeScopeTests
    {
        private ContainerBuilder _builder;

        private PopupParent _parentPrefab;

        private readonly List<string> _tempSaveRoots = new();
        private readonly List<string> _legacyPlayerPrefsKeys = new();

        [SetUp]
        public void SetUp()
        {
            _builder = new ContainerBuilder();
            GameLifetimeScope.RegisterCoreServices(_builder);
        }

        /// <remarks>
        /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
        /// </remarks>
        [TearDown]
        public void TearDown()
        {
            if (_parentPrefab != null) UnityEngine.Object.DestroyImmediate(_parentPrefab.gameObject);

            foreach (string root in _tempSaveRoots)
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
            _tempSaveRoots.Clear();

            if (_legacyPlayerPrefsKeys.Count > 0)
            {
                foreach (string key in _legacyPlayerPrefsKeys)
                {
                    PlayerPrefs.DeleteKey(key);
                    PlayerPrefs.DeleteKey(key + ".migrated");
                }
                PlayerPrefs.Save();
                _legacyPlayerPrefsKeys.Clear();
            }
        }

        /// <summary>
        /// Builds an isolated <see cref="SaveFactoryInputs"/> and legacy PlayerPrefs key for one
        /// test: a fresh temp root and a GUID-bearing key, recorded and cleaned up in
        /// <see cref="TearDown"/> even if the calling test fails.
        /// </summary>
        /// <remarks>
        /// See docs/testing.md, "The save suites never touch a real save".
        /// </remarks>
        private (SaveFactoryInputs inputs, string legacyKey) IsolatedCurrencyOverrides()
        {
            string root = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + Guid.NewGuid().ToString("N"));
            _tempSaveRoots.Add(root);

            string legacyKey = "ChestGameSaveTests.Legacy." + Guid.NewGuid().ToString("N");
            _legacyPlayerPrefsKeys.Add(legacyKey);

            return (SaveFactoryInputs.Defaults(root), legacyKey);
        }

        [Test]
        public void RegisterCoreServices_RegistersEveryServiceTheGameResolves()
        {
            Assert.IsTrue(_builder.Exists(typeof(IRandomProvider), true), nameof(IRandomProvider));
            Assert.IsTrue(_builder.Exists(typeof(IGameClock), true), nameof(IGameClock));
            Assert.IsTrue(_builder.Exists(typeof(IAssetProvider), true), nameof(IAssetProvider));
            Assert.IsTrue(_builder.Exists(typeof(IGameConfigSource), true), nameof(IGameConfigSource));
            Assert.IsTrue(_builder.Exists(typeof(IMinigameListSource), true), nameof(IMinigameListSource));
            Assert.IsTrue(_builder.Exists(typeof(IPopupListSource), true), nameof(IPopupListSource));
            Assert.IsTrue(_builder.Exists(typeof(IPopupParentSource), true), nameof(IPopupParentSource));
            Assert.IsTrue(_builder.Exists(typeof(ICurrencySaveHandler), true), nameof(ICurrencySaveHandler));
            Assert.IsTrue(_builder.Exists(typeof(ICurrencyManager), true), nameof(ICurrencyManager));
            Assert.IsTrue(_builder.Exists(typeof(CurrencyLabelControllerFactory), true), nameof(CurrencyLabelControllerFactory));
            Assert.IsTrue(_builder.Exists(typeof(GameContentLoader), true), nameof(GameContentLoader));
            Assert.IsTrue(_builder.Exists(typeof(GameBootstrapper), true), nameof(GameBootstrapper));
            Assert.IsTrue(_builder.Exists(typeof(IBootStatus), true), nameof(IBootStatus));
        }

        /// <remarks>
        /// See docs/architecture.md, "Registration, in two halves".
        /// </remarks>
        [Test]
        public void TheBootstrapper_IsRegisteredAsTheEntryPointThatRunsIt()
        {
            Assert.IsTrue(_builder.Exists(typeof(IAsyncStartable), true), nameof(IAsyncStartable));
        }

        /// <remarks>
        /// See docs/architecture.md, "Boot".
        /// </remarks>
        [Test]
        public void EveryCoreServiceTheGameResolves_HasASatisfiableObjectGraph()
        {
            using IObjectResolver container = _builder.Build();

            Assert.IsInstanceOf<GameContentLoader>(container.Resolve<GameContentLoader>());
        }

        /// <remarks>
        /// See docs/saving.md, "Redirecting this composition away from a developer's real save".
        /// </remarks>
        [Test]
        public void EveryEngineFacingSeam_HasAProductionImplementation()
        {
            ContainerBuilder builder = new();
            (SaveFactoryInputs inputs, string legacyKey) = IsolatedCurrencyOverrides();
            GameLifetimeScope.RegisterCoreServices(builder, currencySaveInputs: inputs, legacyCurrencyPlayerPrefsKey: legacyKey);

            using IObjectResolver container = builder.Build();

            Assert.IsInstanceOf<UnityRandomProvider>(container.Resolve<IRandomProvider>());
            Assert.IsInstanceOf<UnityGameClock>(container.Resolve<IGameClock>());
            Assert.IsInstanceOf<AddressablesAssetProvider>(container.Resolve<IAssetProvider>());
            Assert.IsInstanceOf<AddressablesGameConfigSource>(container.Resolve<IGameConfigSource>());
            Assert.IsInstanceOf<AddressablesMinigameListSource>(container.Resolve<IMinigameListSource>());
            Assert.IsInstanceOf<AddressablesPopupListSource>(container.Resolve<IPopupListSource>());
            Assert.IsInstanceOf<AddressablesPopupParentSource>(container.Resolve<IPopupParentSource>());
            Assert.IsInstanceOf<CurrencySaveHandler>(
                container.Resolve<ICurrencySaveHandler>());
        }

        /// <remarks>
        /// See docs/architecture.md, "Currency and rewards".
        /// See docs/saving.md, "Redirecting this composition away from a developer's real save".
        /// </remarks>
        [Test]
        public void CurrencyManager_ResolvesWithTheRegisteredSaveHandler()
        {
            ContainerBuilder builder = new();
            (SaveFactoryInputs inputs, string legacyKey) = IsolatedCurrencyOverrides();
            GameLifetimeScope.RegisterCoreServices(builder, currencySaveInputs: inputs, legacyCurrencyPlayerPrefsKey: legacyKey);

            using IObjectResolver container = builder.Build();

            ICurrencyManager currencyManager = container.Resolve<ICurrencyManager>();

            Assert.IsInstanceOf<CurrencyManager>(currencyManager);
            Assert.AreEqual(0, currencyManager.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(0, currencyManager.GetCurrencyAmount(CurrencyType.Gems));
        }

        /// <remarks>
        /// See docs/saving.md, "What ships, and where the composition asserts its own constraints".
        /// </remarks>
        [Test]
        public void CurrencySaveScheduler_ResolvesWithoutThrowing_AndCanFlushBlocking()
        {
            ContainerBuilder builder = new();
            (SaveFactoryInputs inputs, string legacyKey) = IsolatedCurrencyOverrides();
            GameLifetimeScope.RegisterCoreServices(builder, currencySaveInputs: inputs, legacyCurrencyPlayerPrefsKey: legacyKey);

            using IObjectResolver container = builder.Build();

            SaveScheduler<CurrencySaveDocument> scheduler = null;
            Assert.DoesNotThrow(() => scheduler = container.Resolve<SaveScheduler<CurrencySaveDocument>>());
            Assert.IsTrue(scheduler.CanFlushBlocking);
        }

        /// <remarks>
        /// See docs/saving.md, "An unregistered save looks exactly like a registered one".
        /// </remarks>
        [Test]
        public void EverySaveThisCompositionOwns_IsRegisteredForThePauseQuitFlush()
        {
            ContainerBuilder builder = new();
            (SaveFactoryInputs inputs, string legacyKey) = IsolatedCurrencyOverrides();
            GameLifetimeScope.RegisterCoreServices(builder, currencySaveInputs: inputs, legacyCurrencyPlayerPrefsKey: legacyKey);

            using IObjectResolver container = builder.Build();

            CollectionAssert.AreEquivalent(
                new[] { CurrencySaveHandler.SaveKey, GameMetaSaveDocument.SaveKey },
                container.Resolve<ISaveFlushRegistry>().Registered.Select(flushable => flushable.SaveKey).ToArray(),
                "the shipped composition no longer flushes exactly the saves it owns at pause/quit");
        }

        [Test]
        public void RegisterLoadedServices_RegistersEveryContentBackedService()
        {
            GameLifetimeScope.RegisterLoadedServices(_builder, ContentWithAStubParentPrefab());

            Assert.IsTrue(_builder.Exists(typeof(IGameConfig), true), nameof(IGameConfig));
            Assert.IsTrue(_builder.Exists(typeof(IMinigameCatalog), true), nameof(IMinigameCatalog));
            Assert.IsTrue(_builder.Exists(typeof(IPopupCatalog), true), nameof(IPopupCatalog));
            Assert.IsTrue(_builder.Exists(typeof(IPopupParentProvider), true), nameof(IPopupParentProvider));
            Assert.IsTrue(_builder.Exists(typeof(IPopupManager), true), nameof(IPopupManager));
            Assert.IsTrue(_builder.Exists(typeof(IMinigameManager), true), nameof(IMinigameManager));
            Assert.IsTrue(_builder.Exists(typeof(IRewardsManager), true), nameof(IRewardsManager));
            Assert.IsTrue(_builder.Exists(typeof(MinigameContentPreloader), true), nameof(MinigameContentPreloader));
        }

        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        [Test]
        public void WithNoLabelToReportInto_BootStillHasSomethingToReportThrough()
        {
            using IObjectResolver container = _builder.Build();

            IBootStatus status = container.Resolve<IBootStatus>();

            Assert.IsInstanceOf<SilentBootStatus>(status);
            Assert.DoesNotThrow(() => status.Report("anything"));
        }

        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        [Test]
        public void ABootStatusHandedIn_IsTheOneTheGameReportsThrough()
        {
            ContainerBuilder builder = new();
            RecordingBootStatus reporter = new();

            GameLifetimeScope.RegisterCoreServices(builder, reporter);

            using IObjectResolver container = builder.Build();

            Assert.AreSame(reporter, container.Resolve<IBootStatus>());
        }

        /// <remarks>
        /// See docs/architecture.md, "Registration, in two halves".
        /// </remarks>
        [Test]
        public void ThePreloader_ResolvesWithTheLoadedCatalogAndTheCoreAssetProvider()
        {
            GameLifetimeScope.RegisterLoadedServices(_builder, ContentWithAStubParentPrefab());

            using IObjectResolver container = _builder.Build();

            Assert.IsInstanceOf<MinigameContentPreloader>(container.Resolve<MinigameContentPreloader>());
        }

        /// <remarks>
        /// See docs/architecture.md, "Registration, in two halves".
        /// See docs/saving.md, "Redirecting this composition away from a developer's real save".
        /// </remarks>
        [Test]
        public void EveryLoadedServiceTheGameResolves_HasASatisfiableObjectGraph()
        {
            ContainerBuilder builder = new();
            (SaveFactoryInputs inputs, string legacyKey) = IsolatedCurrencyOverrides();
            GameLifetimeScope.RegisterCoreServices(builder, currencySaveInputs: inputs, legacyCurrencyPlayerPrefsKey: legacyKey);
            GameLifetimeScope.RegisterLoadedServices(builder, ContentWithAStubParentPrefab());

            using IObjectResolver container = builder.Build();

            Assert.IsInstanceOf<PopupManager>(container.Resolve<IPopupManager>());
            Assert.IsInstanceOf<MinigameManager>(container.Resolve<IMinigameManager>());
            Assert.IsInstanceOf<RewardsManager>(container.Resolve<IRewardsManager>());
        }

        /// <remarks>
        /// See docs/architecture.md, "Registration, in two halves".
        /// </remarks>
        [Test]
        public void TheLoadedConfig_IsBuiltFromTheDocumentThatWasLoaded()
        {
            GameLifetimeScope.RegisterLoadedServices(_builder, ContentWithAStubParentPrefab());

            using IObjectResolver container = _builder.Build();

            IGameConfig config = container.Resolve<IGameConfig>();

            Assert.IsInstanceOf<LocalJsonGameConfig>(config);
            Assert.AreEqual(10, config.GemsReward);
            Assert.AreEqual(50, config.CoinsReward);
        }

        /// <remarks>
        /// See docs/architecture.md, "Popups".
        /// </remarks>
        [Test]
        public void ResolvingPopupManager_DoesNotCreateTheSharedCanvasYet()
        {
            _parentPrefab = new GameObject("PopupParentPrefab").AddComponent<PopupParent>();

            int before = LivePopupParents();

            GameLifetimeScope.RegisterLoadedServices(_builder, ContentWith(_parentPrefab));

            using IObjectResolver container = _builder.Build();

            container.Resolve<IPopupManager>();

            Assert.AreEqual(before, LivePopupParents(), "the shared canvas was instantiated before any popup asked for it");
        }

        private static int LivePopupParents() =>
            UnityEngine.Object.FindObjectsByType<PopupParent>(FindObjectsSortMode.None).Length;

        private LoadedContent ContentWithAStubParentPrefab()
        {
            _parentPrefab = new GameObject("PopupParentPrefab").AddComponent<PopupParent>();
            return ContentWith(_parentPrefab);
        }

        private class RecordingBootStatus : IBootStatus
        {
            public void Report(string message) { }
        }

        private static LoadedContent ContentWith(PopupParent parentPrefab) =>
            new(FakeGameConfigSource.ValidDocument,
                new List<MinigameBaseSO>(),
                new List<PopupBase>(),
                parentPrefab);
    }
}

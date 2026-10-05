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
using NUnit.Framework;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Tests.EditMode
{
    // Run against GameLifetimeScope's own registration methods rather than a copy, so dropping a
    // registration from the composition root fails here. The root scope is everything that needs no
    // asset, which is what keeps it assertable in edit mode. What the shipped assets contain is
    // proved in GameBootstrapperTests.
    //
    // The bare SetUp registration below carries no currency overrides. That is safe only because
    // nothing it builds performs IO: it does resolve every SaveScheduler<T> the composition owns,
    // through RegisterCoreServices' build callback, but no scheduler, FileStore or AtomicFileStore
    // constructor touches disk (checked, not assumed).
    //
    // What is NOT safe is a test resolving ICurrencyManager, the currency save handler or the
    // currency scheduler from it: without an override that performs the real legacy import against
    // the developer's actual persistentDataPath and real PlayerPrefs entry. Every test below that
    // touches any of the three builds its own container through IsolatedCurrencyOverrides()
    // instead. See docs/saving.md, "Redirecting this composition away from a developer's real
    // save".
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
                // Both halves: CurrencyLegacyImport.Clear renames rather than deletes, so a
                // successful import leaves a ".migrated" sibling this list does not itself carry.
                foreach (string key in _legacyPlayerPrefsKeys)
                {
                    PlayerPrefs.DeleteKey(key);
                    PlayerPrefs.DeleteKey(key + ".migrated");
                }
                PlayerPrefs.Save();
                _legacyPlayerPrefsKeys.Clear();
            }
        }

        // A fresh temp root and a GUID-bearing legacy PlayerPrefs key, per call - never the
        // developer's real Application.persistentDataPath or real
        // "ResourceBankSaveData_CurrencyType" entry. Both are recorded and cleaned up in TearDown
        // even if the test that requested them fails.
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
            Assert.IsTrue(_builder.Exists(typeof(GameContentLoader), true), nameof(GameContentLoader));
            Assert.IsTrue(_builder.Exists(typeof(GameBootstrapper), true), nameof(GameBootstrapper));
            Assert.IsTrue(_builder.Exists(typeof(IBootStatus), true), nameof(IBootStatus));
        }

        [Test]
        public void TheBootstrapper_IsRegisteredAsTheEntryPointThatRunsIt()
        {
            // VContainer only runs what it can find as an IAsyncStartable. Registered as the
            // concrete type alone, the game would build a container and never boot.
            Assert.IsTrue(_builder.Exists(typeof(IAsyncStartable), true), nameof(IAsyncStartable));
        }

        [Test]
        public void EveryCoreServiceTheGameResolves_HasASatisfiableObjectGraph()
        {
            // Exists() only proves a line was written. The loader needs all four sources, each of
            // which needs the asset provider, so a missing registration anywhere down that chain
            // fails here.
            using IObjectResolver container = _builder.Build();

            Assert.IsInstanceOf<GameContentLoader>(container.Resolve<GameContentLoader>());
        }

        [Test]
        public void EveryEngineFacingSeam_HasAProductionImplementation()
        {
            // The seams exist so tests can substitute them; the real game still has to get real
            // ones. Isolated even though constructing the save handler alone touches nothing, so no
            // test here depends on that staying true.
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

        [Test]
        public void CurrencyManager_ResolvesWithTheRegisteredSaveHandler()
        {
            // CurrencyManager takes its save handler as its only constructor argument, so this
            // fails outright if the scope stops registering one. Isolated because resolving
            // ICurrencyManager runs the real legacy import - see the fixture header.
            ContainerBuilder builder = new();
            (SaveFactoryInputs inputs, string legacyKey) = IsolatedCurrencyOverrides();
            GameLifetimeScope.RegisterCoreServices(builder, currencySaveInputs: inputs, legacyCurrencyPlayerPrefsKey: legacyKey);

            using IObjectResolver container = builder.Build();

            ICurrencyManager currencyManager = container.Resolve<ICurrencyManager>();

            Assert.IsInstanceOf<CurrencyManager>(currencyManager);
            // Nothing was ever seeded under the isolated GUID legacy key, so a freshly resolved
            // manager reads as a genuine first run rather than carrying over anything real.
            Assert.AreEqual(0, currencyManager.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual(0, currencyManager.GetCurrencyAmount(CurrencyType.Gems));
        }

        [Test]
        public void CurrencySaveScheduler_ResolvesWithoutThrowing_AndCanFlushBlocking()
        {
            // Resolving registers the scheduler, and Register throws SchedulerCannotFlushBlocking
            // when CanFlushBlocking is false. So if a future change wraps the currency store in a
            // ThreadHoppingStore, this resolve starts throwing here rather than shipping a scheduler
            // that fails the first real pause or quit on a device.
            ContainerBuilder builder = new();
            (SaveFactoryInputs inputs, string legacyKey) = IsolatedCurrencyOverrides();
            GameLifetimeScope.RegisterCoreServices(builder, currencySaveInputs: inputs, legacyCurrencyPlayerPrefsKey: legacyKey);

            using IObjectResolver container = builder.Build();

            SaveScheduler<CurrencySaveDocument> scheduler = null;
            Assert.DoesNotThrow(() => scheduler = container.Resolve<SaveScheduler<CurrencySaveDocument>>());
            Assert.IsTrue(scheduler.CanFlushBlocking);
        }

        [Test]
        public void EverySaveThisCompositionOwns_IsRegisteredForThePauseQuitFlush()
        {
            // Building the container is the whole act: the build callback resolves each scheduler,
            // and resolving is what registers it. An unregistered save is never flushed at
            // pause/quit and looks identical otherwise, so nothing else here would notice.
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

        [Test]
        public void WithNoLabelToReportInto_BootStillHasSomethingToReportThrough()
        {
            // The bootstrapper reports unconditionally, so an unwired label slot, and every
            // container a test builds, still has to resolve one.
            using IObjectResolver container = _builder.Build();

            IBootStatus status = container.Resolve<IBootStatus>();

            Assert.IsInstanceOf<SilentBootStatus>(status);
            Assert.DoesNotThrow(() => status.Report("anything"));
        }

        [Test]
        public void ABootStatusHandedIn_IsTheOneTheGameReportsThrough()
        {
            // The scene's label is the one thing the root scope cannot construct for itself.
            // Registering it is what connects the bootstrapper to the boot scene.
            ContainerBuilder builder = new();
            RecordingBootStatus reporter = new();

            GameLifetimeScope.RegisterCoreServices(builder, reporter);

            using IObjectResolver container = builder.Build();

            Assert.AreSame(reporter, container.Resolve<IBootStatus>());
        }

        [Test]
        public void ThePreloader_ResolvesWithTheLoadedCatalogAndTheCoreAssetProvider()
        {
            // Reaches across both halves of the split, the catalog from the loaded one and the
            // provider from core, which is the graph that fails silently if either registration
            // moves.
            GameLifetimeScope.RegisterLoadedServices(_builder, ContentWithAStubParentPrefab());

            using IObjectResolver container = _builder.Build();

            Assert.IsInstanceOf<MinigameContentPreloader>(container.Resolve<MinigameContentPreloader>());
        }

        [Test]
        public void EveryLoadedServiceTheGameResolves_HasASatisfiableObjectGraph()
        {
            // The three services whose constructors reach outside themselves: PopupManager needs a
            // catalog and a parent provider, MinigameManager needs a catalog and the resolver, and
            // RewardsManager reaches across both halves. Isolated because it resolves
            // ICurrencyManager transitively - see the fixture header.
            ContainerBuilder builder = new();
            (SaveFactoryInputs inputs, string legacyKey) = IsolatedCurrencyOverrides();
            GameLifetimeScope.RegisterCoreServices(builder, currencySaveInputs: inputs, legacyCurrencyPlayerPrefsKey: legacyKey);
            GameLifetimeScope.RegisterLoadedServices(builder, ContentWithAStubParentPrefab());

            using IObjectResolver container = builder.Build();

            Assert.IsInstanceOf<PopupManager>(container.Resolve<IPopupManager>());
            Assert.IsInstanceOf<MinigameManager>(container.Resolve<IMinigameManager>());
            Assert.IsInstanceOf<RewardsManager>(container.Resolve<IRewardsManager>());
        }

        [Test]
        public void TheLoadedConfig_IsBuiltFromTheDocumentThatWasLoaded()
        {
            // The registration parses the carried document rather than fetching one, which is why
            // nothing downstream can observe a half-built config.
            GameLifetimeScope.RegisterLoadedServices(_builder, ContentWithAStubParentPrefab());

            using IObjectResolver container = _builder.Build();

            IGameConfig config = container.Resolve<IGameConfig>();

            Assert.IsInstanceOf<LocalJsonGameConfig>(config);
            Assert.AreEqual(10, config.GemsReward);
            Assert.AreEqual(50, config.CoinsReward);
        }

        [Test]
        public void ResolvingPopupManager_DoesNotCreateTheSharedCanvasYet()
        {
            // The parent canvas is DontDestroyOnLoad, so building it during resolution would leak a
            // scene object into every consumer of the container. Counting from before the
            // registration catches an eager constructor as well as an eager resolve.
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

using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using Company.ChestGame.Config;
using Company.ChestGame.Core;
using Company.ChestGame.Currency;
using Company.ChestGame.Gameplay;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Saving;
using Company.ChestGame.UI;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Boots the real Boot scene and checks what only a real boot can prove: that the shipped
    /// <see cref="IGameConfig"/> document, the minigame and popup catalogs, the popup parent prefab,
    /// and the shipped chests minigame's own content all resolve through the real composition root
    /// and through Addressables.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "What lives where".
    /// See docs/saving.md, "Sealing the boot path a test cannot pass arguments through".
    /// See docs/testing.md, "The save suites never touch a real save".
    /// </remarks>
    public class GameBootstrapperTests
    {
        private const string BOOT_SCENE = "Boot";
        private const string GAME_SCENE = "Game";

        private string _currencySaveRoot;
        private string _legacyPlayerPrefsKey;

        /// <remarks>
        /// See docs/testing.md, "The save suites never touch a real save".
        /// </remarks>
        [TearDown]
        public void RestoreTheCurrencySaveOverrides()
        {
            GameLifetimeScope.CurrencySaveInputsOverride = null;
            GameLifetimeScope.LegacyCurrencyPlayerPrefsKeyOverride = null;

            if (_legacyPlayerPrefsKey != null)
            {
                PlayerPrefs.DeleteKey(_legacyPlayerPrefsKey);
                PlayerPrefs.DeleteKey(_legacyPlayerPrefsKey + ".migrated");
                PlayerPrefs.Save();
            }
        }

        /// <remarks>
        /// See docs/testing.md, "The save suites never touch a real save".
        /// See docs/testing.md, "What lives where".
        /// </remarks>
        [UnitySetUp]
        public IEnumerator BootTheGame()
        {
            _currencySaveRoot = Path.Combine(Path.GetTempPath(), "ChestGameSaveTests_" + System.Guid.NewGuid().ToString("N"));
            _legacyPlayerPrefsKey = "ChestGameSaveTests.Legacy." + System.Guid.NewGuid().ToString("N");

            GameLifetimeScope.CurrencySaveInputsOverride = SaveFactoryInputs.Defaults(_currencySaveRoot);
            GameLifetimeScope.LegacyCurrencyPlayerPrefsKeyOverride = _legacyPlayerPrefsKey;

            yield return SceneManager.LoadSceneAsync(BOOT_SCENE);

            float deadline = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().name != GAME_SCENE && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(GAME_SCENE, SceneManager.GetActiveScene().name,
                "the bootstrapper never reached the game scene");

            yield return null;
        }

        /// <remarks>
        /// See docs/testing.md, "The save suites never touch a real save".
        /// </remarks>
        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            foreach (LifetimeScope scope in Object.FindObjectsByType<LifetimeScope>(FindObjectsSortMode.None))
            {
                if (scope != null) Object.Destroy(scope.gameObject);
            }

            foreach (PopupParent parent in Object.FindObjectsByType<PopupParent>(FindObjectsSortMode.None))
            {
                Object.Destroy(parent.gameObject);
            }

            yield return null;

            Scene game = SceneManager.GetSceneByName(GAME_SCENE);
            if (game.IsValid() && game.isLoaded)
            {
                Scene empty = SceneManager.CreateScene("AfterBootTeardown");
                SceneManager.SetActiveScene(empty);
                yield return SceneManager.UnloadSceneAsync(game);
            }

            if (_currencySaveRoot != null && Directory.Exists(_currencySaveRoot)) Directory.Delete(_currencySaveRoot, recursive: true);
        }

        /// <remarks>
        /// See docs/architecture.md, "Boot".
        /// </remarks>
        [Test]
        public void TheGameScene_OpensWithAScopeParentedToTheOneHoldingTheLoadedContent()
        {
            GameSceneLifetimeScope sceneScope = SceneScope();

            Assert.IsNotNull(sceneScope.Parent, "the game scene's scope was not parented by EnqueueParent");
            Assert.IsNotNull(sceneScope.Container, "the game scene's scope never built its container");

            Assert.IsInstanceOf<MinigameManager>(sceneScope.Container.Resolve<IMinigameManager>());
            Assert.IsInstanceOf<CurrencyManager>(sceneScope.Container.Resolve<ICurrencyManager>());
        }

        [Test]
        public void TheRootScope_SurvivesTheSceneLoad()
        {
            GameLifetimeScope root = Object.FindAnyObjectByType<GameLifetimeScope>();

            Assert.IsNotNull(root, "the root scope did not survive the scene load");
            Assert.AreNotEqual(GAME_SCENE, root.gameObject.scene.name,
                "the root scope should have moved to DontDestroyOnLoad, not be part of the game scene");
        }

        /// <remarks>
        /// See docs/testing.md, "What lives where".
        /// </remarks>
        [Test]
        public void GameConfig_ResolvesAndParsesTheShippedConfigDocument()
        {
            IGameConfig config = Resolve<IGameConfig>();

            Assert.IsInstanceOf<LocalJsonGameConfig>(config);
            Assert.Greater(config.GemsReward, 0);
            Assert.Greater(config.CoinsReward, 0);
        }

        [Test]
        public void MinigameCatalog_ResolvesAndListsTheShippedMinigames()
        {
            IMinigameCatalog catalog = Resolve<IMinigameCatalog>();

            CollectionAssert.IsNotEmpty(catalog.Minigames);
        }

        [Test]
        public void PopupCatalog_ResolvesAndListsTheShippedPopups()
        {
            IPopupCatalog catalog = Resolve<IPopupCatalog>();

            CollectionAssert.IsNotEmpty(catalog.Popups);
        }

        /// <remarks>
        /// See docs/architecture.md, "Popups".
        /// </remarks>
        [Test]
        public void ThePopupParentPrefab_ReachedTheProviderThroughTheContentThatWasLoaded()
        {
            IPopupParentProvider provider = Resolve<IPopupParentProvider>();

            Assert.IsInstanceOf<PopupParentProvider>(provider);
            Assert.IsNotNull(provider.Default, "the shipped popup parent prefab never reached the provider");
        }

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        [UnityTest]
        public IEnumerator TheShippedChestsMinigame_BeginsFromTheContentItsDefinitionNamesRatherThanHolds()
            => UniTask.ToCoroutine(async () =>
        {
            IMinigameManager manager = Resolve<IMinigameManager>();
            GameObject parent = new("ChestsMinigameParent");

            MinigameContainer minigame = manager.Get("chests");
            try
            {
                await minigame.BeginAsync(parent.transform, CancellationToken.None);

                ChestsMinigameController controller = (ChestsMinigameController)minigame.ControllerInstance;

                Assert.IsNotNull(controller.Chests, "the config document never reached the controller");
                Assert.Greater(controller.Chests.Count, 0);
                Assert.Greater(controller.TotalAttempts, 0);
                Assert.IsNotNull(minigame.ViewInstance, "the view prefab never resolved through its reference");
                Assert.IsInstanceOf<ChestsMinigameView>(minigame.ViewInstance);
            }
            finally
            {
                minigame.End();
                Object.Destroy(parent);
            }
        });

        /// <remarks>
        /// See docs/minigames.md, "What adding a minigame actually takes".
        /// </remarks>
        [Test]
        public void TheShippedChestsMinigame_NamesItsOwnContent()
        {
            IMinigameCatalog catalog = Resolve<IMinigameCatalog>();
            MinigameBaseSO definition = catalog.Minigames[typeof(ChestsMinigame)];

            Assert.AreEqual("minigame.chests", definition.ContentLabel,
                "the label has to match the one the group's entries carry");
            Assert.AreEqual(MinigameLoadPolicy.OnDemand, definition.LoadPolicy);
        }

        /// <remarks>
        /// See docs/architecture.md, "Boot".
        /// </remarks>
        [Test]
        public void TheSceneObjects_AreInjectedFromBothHalvesOfTheSplit()
        {
            GameShellView gameShellView = Object.FindAnyObjectByType<GameShellView>();
            Assert.IsNotNull(gameShellView, "the game scene no longer contains a GameShellView");
            Assert.IsTrue(gameShellView.IsBound, "GameShellView was never bound to its controller");

            CurrencyWatcher watcher = Object.FindAnyObjectByType<CurrencyWatcher>();
            Assert.IsNotNull(watcher, "the game scene no longer contains a CurrencyWatcher");
            Assert.IsNotNull(InjectedField(watcher, "_currencyManager"),
                "CurrencyWatcher was never injected with the currency manager");
        }

        private static T Resolve<T>() => SceneScope().Container.Resolve<T>();

        private static GameSceneLifetimeScope SceneScope()
        {
            GameSceneLifetimeScope scope = Object.FindAnyObjectByType<GameSceneLifetimeScope>();
            Assert.IsNotNull(scope, "the game scene carries no scope of its own");

            return scope;
        }

        private static object InjectedField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name} has no field called {fieldName}");

            return field.GetValue(target);
        }
    }
}

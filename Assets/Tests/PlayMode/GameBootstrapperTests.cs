using System.Collections;
using System.Reflection;
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
using Company.ChestGame.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Tests.PlayMode
{
    // The boot flow, run for real: the boot scene builds the root scope, the bootstrapper loads the
    // content, and only then does the game scene open with a scope that can see it.
    //
    // This is also where the shipped assets get checked. That coverage used to sit in
    // GameLifetimeScopeTests, back when the root scope loaded everything itself; the point of
    // moving it is that resolving IGameConfig or IMinigameCatalog now means having booted, and
    // booting means a scene.
    public class GameBootstrapperTests
    {
        private const string BOOT_SCENE = "Boot";
        private const string GAME_SCENE = "Game";

        [UnitySetUp]
        public IEnumerator BootTheGame()
        {
            yield return SceneManager.LoadSceneAsync(BOOT_SCENE);

            // A settled state, not a mid-flight one: the loop waits for the game scene to be the
            // active one and everything asserted below is true once it is.
            float deadline = Time.realtimeSinceStartup + 30f;
            while (SceneManager.GetActiveScene().name != GAME_SCENE && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.AreEqual(GAME_SCENE, SceneManager.GetActiveScene().name,
                "the bootstrapper never reached the game scene");

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator CleanUp()
        {
            // The root scope is DontDestroyOnLoad by design, so nothing removes it but this.
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
        }

        [Test]
        public void TheGameScene_OpensWithAScopeParentedToTheOneHoldingTheLoadedContent()
        {
            GameSceneLifetimeScope sceneScope = SceneScope();

            Assert.IsNotNull(sceneScope.Parent, "the game scene's scope was not parented by EnqueueParent");
            Assert.IsNotNull(sceneScope.Container, "the game scene's scope never built its container");

            // The scene scope registers nothing itself, so anything it resolves came down the chain.
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

        [Test]
        public void GameConfig_ResolvesAndParsesTheShippedConfigDocument()
        {
            // Reaches the real Resources/GameConfig.json through the registered source, which makes
            // this the one test that would catch the shipped config going missing or malformed.
            // Booting at all is most of the assertion: every failure in there throws.
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

        [Test]
        public void TheShippedChestsMinigame_CarriesAConfigDocumentItCanBuildAControllerFrom()
        {
            // The chests config no longer comes from a Resources path anybody validates; it is a
            // TextAsset reference on the definition asset. Nothing else would notice that
            // reference going empty until the first button press in a real session, so this is
            // where the shipped wiring gets checked.
            IMinigameCatalog catalog = Resolve<IMinigameCatalog>();
            MinigameBaseSO definition = catalog.Minigames[typeof(ChestsMinigame)];

            ChestsMinigameController controller =
                (ChestsMinigameController)definition.GetMinigameContainer().ControllerInstance;

            Assert.IsNotNull(controller.Chests, "the config document never reached the controller");
            Assert.Greater(controller.Chests.Count, 0);
            Assert.Greater(controller.TotalAttempts, 0);
        }

        [Test]
        public void TheSceneObjects_AreInjectedFromBothHalvesOfTheSplit()
        {
            // The trap this phase set: the auto-inject list used to live on the root scope, which
            // after the split cannot resolve IMinigameManager at all. GameManager needs the loaded
            // half, CurrencyWatcher needs the core half, and both are injected from the scene scope.
            GameManager gameManager = Object.FindAnyObjectByType<GameManager>();
            Assert.IsNotNull(gameManager, "the game scene no longer contains a GameManager");
            Assert.IsNotNull(InjectedField(gameManager, "_minigamesManager"),
                "GameManager was never injected with the minigame manager");

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

        // The injected references are private, as they should be. Reading them back is the only way
        // to assert that auto-injection actually reached these objects.
        private static object InjectedField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name} has no field called {fieldName}");

            return field.GetValue(target);
        }
    }
}

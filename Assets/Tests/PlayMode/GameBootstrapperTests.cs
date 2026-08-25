using System.Collections;
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
    // The boot flow, run for real, and where the shipped assets get checked: resolving IGameConfig
    // or IMinigameCatalog means having booted, and booting means a scene. It is the Addressables
    // integration proof for the same reason, since every key the game ships is resolved by the boot
    // flow.
    public class GameBootstrapperTests
    {
        private const string BOOT_SCENE = "Boot";
        private const string GAME_SCENE = "Game";

        [UnitySetUp]
        public IEnumerator BootTheGame()
        {
            yield return SceneManager.LoadSceneAsync(BOOT_SCENE);

            // A settled state, not a mid-flight one: everything asserted below is true once the
            // game scene is active.
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

            // The scene scope registers nothing itself, so anything it resolves came down the
            // chain.
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
            // Reaches the shipped GameConfig document through the registered source and through
            // Addressables, so this is what catches it going missing, unaddressable or malformed.
            // Booting at all is most of the assertion.
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
        public void ThePopupParentPrefab_ReachedTheProviderThroughTheContentThatWasLoaded()
        {
            // The fourth source, and the only one whose result nothing else here would notice going
            // missing: the provider holds the prefab untouched until a popup is shown. Asking for
            // the canvas is what forces the prefab to have been real.
            IPopupParentProvider provider = Resolve<IPopupParentProvider>();

            Assert.IsInstanceOf<PopupParentProvider>(provider);
            Assert.IsNotNull(provider.Default, "the shipped popup parent prefab never reached the provider");
        }

        [UnityTest]
        public IEnumerator TheShippedChestsMinigame_BeginsFromTheContentItsDefinitionNamesRatherThanHolds()
            => UniTask.ToCoroutine(async () =>
        {
            // The chests view and config are behind AssetReferences, so a wrong GUID or an entry
            // dropped from the group surfaces nowhere until a minigame is begun. Beginning one for
            // real is the proof, and the same proof that configure-load-inject-instantiate works
            // outside a fixture holding fakes.
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

        [Test]
        public void TheShippedChestsMinigame_NamesItsOwnContent()
        {
            // The two fields the delivery paths read, pinned against the group they describe:
            // nothing else would notice the label drifting from the one the entries carry.
            IMinigameCatalog catalog = Resolve<IMinigameCatalog>();
            MinigameBaseSO definition = catalog.Minigames[typeof(ChestsMinigame)];

            Assert.AreEqual("minigame.chests", definition.ContentLabel,
                "the label has to match the one the group's entries carry");
            Assert.AreEqual(MinigameLoadPolicy.OnDemand, definition.LoadPolicy);
        }

        [Test]
        public void TheSceneObjects_AreInjectedFromBothHalvesOfTheSplit()
        {
            // The trap in splitting the scope: the root scope cannot resolve IMinigameManager, so
            // the auto-inject list has to live on the scene scope.
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

        // The injected references are private, so reading them back is the only way to assert
        // auto-injection reached these objects.
        private static object InjectedField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"{target.GetType().Name} has no field called {fieldName}");

            return field.GetValue(target);
        }
    }
}

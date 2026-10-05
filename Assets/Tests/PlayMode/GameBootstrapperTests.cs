using System.Collections;
using System.Collections.Generic;
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
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
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
    public class GameBootstrapperTests : RealGameBootFixture
    {
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
        [UnityTest]
        public IEnumerator TheShippedChestsMinigame_NamesContentThatActuallyResolves()
        {
            IMinigameCatalog catalog = Resolve<IMinigameCatalog>();
            MinigameBaseSO definition = catalog.Minigames[typeof(ChestsMinigame)];

            Assert.AreEqual(MinigameLoadPolicy.OnDemand, definition.LoadPolicy);
            Assert.IsFalse(string.IsNullOrWhiteSpace(definition.ContentLabel),
                "the shipped chests minigame has to name a content label at all");

            AsyncOperationHandle<IList<IResourceLocation>> locating =
                Addressables.LoadResourceLocationsAsync(definition.ContentLabel);
            try
            {
                yield return locating;

                Assert.AreEqual(AsyncOperationStatus.Succeeded, locating.Status,
                    $"looking up the label '{definition.ContentLabel}' failed outright");
                Assert.IsNotNull(locating.Result);
                Assert.Greater(locating.Result.Count, 0,
                    $"no Addressables entry carries '{definition.ContentLabel}', so the minigame's content can never be fetched as a unit");
            }
            finally
            {
                Addressables.Release(locating);
            }
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

            CurrencyLabelView currencyLabelView = Object.FindAnyObjectByType<CurrencyLabelView>();
            Assert.IsNotNull(currencyLabelView, "the game scene no longer contains a CurrencyLabelView");
            Assert.IsTrue(currencyLabelView.IsBound, "CurrencyLabelView was never bound to its controller");
        }

        private static T Resolve<T>() => SceneScope().Container.Resolve<T>();

        private static GameSceneLifetimeScope SceneScope()
        {
            GameSceneLifetimeScope scope = Object.FindAnyObjectByType<GameSceneLifetimeScope>();
            Assert.IsNotNull(scope, "the game scene carries no scope of its own");

            return scope;
        }
    }
}

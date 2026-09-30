using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Company.ChestGame.Core;
using Company.ChestGame.Gameplay;
using Company.ChestGame.Minigame.Chests;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using VContainer;
using VContainer.Unity;
using Object = UnityEngine.Object;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Boots the real game, starts the chests minigame through the real <see cref="GameShellView"/>
    /// start button, then lets the engine take the game scene away and checks that the shell's
    /// disposal ended the minigame: the chests <see cref="SaveScheduler{T}"/> is gone from the root
    /// scope's <see cref="ISaveFlushRegistry"/>. Runs against the asset database, so a real download
    /// in flight cannot be exercised here.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Entry point and game flow".
    /// See docs/testing.md, "What lives where".
    /// See docs/testing.md, "The save suites never touch a real save".
    /// </remarks>
    public class GameShellTeardownTests : RealGameBootFixture
    {
        private const string LEFT_SCENE = "AfterShellTeardown";
        private const float SETTLE_TIMEOUT_SECONDS = 30f;

        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        [UnityTest]
        public IEnumerator LeavingTheGameScene_WithTheChestsMinigameRunning_UnregistersItsSaveScheduler()
        {
            GameLifetimeScope root = Object.FindAnyObjectByType<GameLifetimeScope>();
            Assert.IsNotNull(root, "the root scope did not survive the scene load");

            ISaveFlushRegistry resolved = root.Container.Resolve<ISaveFlushRegistry>();
            Assert.IsInstanceOf<SaveFlushRegistry>(resolved, "the root scope no longer registers the concrete SaveFlushRegistry");
            SaveFlushRegistry registry = (SaveFlushRegistry)resolved;

            GameShellView shell = Object.FindAnyObjectByType<GameShellView>();
            Assert.IsNotNull(shell, "the game scene no longer contains a GameShellView");
            Assert.IsTrue(shell.IsBound, "GameShellView was never bound to its controller");

            Button startButton = StartButtonOf(shell);
            Assert.IsNotNull(startButton, "the authored start button is missing from the GameShellView");
            Assert.IsTrue(startButton.interactable, "the start button is not pressable before any start");
            Assert.IsNull(Object.FindAnyObjectByType<ChestsMinigameView>(), "a chests view existed before any start");
            Assert.AreEqual(0, ChestsFlushableCount(registry), "the chests scheduler was registered before any start");

            int flushablesBeforeStart = registry.Registered.Count;

            startButton.onClick.Invoke();

            yield return WaitForCondition(() =>
                Object.FindAnyObjectByType<ChestsMinigameView>() != null
                && registry.Registered.Count == flushablesBeforeStart + 1
                && startButton.interactable);

            Assert.IsNotNull(Object.FindAnyObjectByType<ChestsMinigameView>(),
                "the chests view never appeared after the start button was pressed");
            Assert.IsTrue(startButton.interactable, "the shell's start never settled: the button is still busy");
            Assert.AreEqual(flushablesBeforeStart + 1, registry.Registered.Count,
                "starting the chests minigame should add exactly one flushable to the root registry");
            Assert.AreEqual(1, ChestsFlushableCount(registry),
                "the flushable the start added is not the chests run's scheduler");

            Scene game = SceneManager.GetSceneByName(GAME_SCENE);
            Assert.IsTrue(game.isLoaded, "the game scene is not loaded");

            SceneManager.SetActiveScene(SceneManager.CreateScene(LEFT_SCENE));
            yield return SceneManager.UnloadSceneAsync(game);

            yield return WaitForCondition(() => registry.Registered.Count == flushablesBeforeStart);

            Assert.IsFalse(game.isLoaded, "the game scene is still loaded");
            Assert.IsNull(Object.FindAnyObjectByType<GameSceneLifetimeScope>(), "the game scene's scope survived the unload");
            Assert.AreEqual(flushablesBeforeStart, registry.Registered.Count,
                "leaving the scene should return the root registry to its pre-start flushables: the shell's disposal did not end the minigame");
            Assert.AreEqual(0, ChestsFlushableCount(registry),
                "the chests run's scheduler is still registered after the scene went away");
        }

        private static int ChestsFlushableCount(SaveFlushRegistry registry) =>
            registry.Registered.Count(flushable => flushable.SaveKey == ChestsRunSaveDocument.SaveKey);

        private static Button StartButtonOf(GameShellView shell)
        {
            FieldInfo field = typeof(GameShellView).GetField("_startButton", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(nameof(GameShellView), "_startButton");

            return (Button)field.GetValue(shell);
        }

        private static IEnumerator WaitForCondition(Func<bool> condition)
        {
            float deadline = Time.realtimeSinceStartup + SETTLE_TIMEOUT_SECONDS;
            while (!condition() && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }
        }
    }
}

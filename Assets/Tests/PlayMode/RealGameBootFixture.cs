using System.Collections;
using System.IO;
using Company.ChestGame.Core;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Saving;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using VContainer.Unity;

namespace Company.ChestGame.Tests.PlayMode
{
    /// <summary>
    /// Boots the real Boot scene into the Game scene with every save redirected to a temp root, and
    /// cleans up after each test. Derived fixtures inherit the setup and teardown: NUnit runs a base
    /// class's SetUp before, and TearDown after, the derived fixture's, including the Unity variants.
    /// </summary>
    /// <remarks>
    /// See docs/testing.md, "The save suites never touch a real save".
    /// </remarks>
    public abstract class RealGameBootFixture
    {
        protected const string BOOT_SCENE = "Boot";
        protected const string GAME_SCENE = "Game";

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
    }
}

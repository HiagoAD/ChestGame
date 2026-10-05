using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Core;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers <see cref="GameContentLoader"/> against fakes, with no scene and no scope.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Boot".
    /// </remarks>
    public class GameContentLoaderTests
    {
        private FakeGameConfigSource _configSource;
        private FakeMinigameListSource _minigameListSource;
        private FakePopupListSource _popupListSource;
        private FakePopupParentSource _popupParentSource;

        private PopupParent _parentPrefab;

        private GameContentLoader _loader;

        [SetUp]
        public void SetUp()
        {
            _parentPrefab = new GameObject("PopupParentPrefab").AddComponent<PopupParent>();

            _configSource = new FakeGameConfigSource();
            _minigameListSource = new FakeMinigameListSource();
            _popupListSource = new FakePopupListSource();
            _popupParentSource = new FakePopupParentSource { Prefab = _parentPrefab };

            _loader = new GameContentLoader(_configSource, _minigameListSource, _popupListSource, _popupParentSource);
        }

        [TearDown]
        public void TearDown()
        {
            if (_parentPrefab != null) Object.DestroyImmediate(_parentPrefab.gameObject);
        }

        /// <remarks>
        /// See docs/architecture.md, "Boot".
        /// </remarks>
        [Test]
        public void LoadAsync_ReadsEverySourceExactlyOnce()
        {
            SynchronousUniTask.Result(_loader.LoadAsync(CancellationToken.None));

            Assert.AreEqual(1, _configSource.ReadCallCount, nameof(FakeGameConfigSource));
            Assert.AreEqual(1, _minigameListSource.ReadCallCount, nameof(FakeMinigameListSource));
            Assert.AreEqual(1, _popupListSource.ReadCallCount, nameof(FakePopupListSource));
            Assert.AreEqual(1, _popupParentSource.ReadCallCount, nameof(FakePopupParentSource));
        }

        [Test]
        public void LoadAsync_CarriesEverySourcesResultIntoTheContent()
        {
            List<MinigameBaseSO> minigames = new();
            List<PopupBase> popups = new();

            _configSource.Document = @"{ ""GemsReward"": 1, ""CoinsReward"": 2 }";
            _minigameListSource.Entries = minigames;
            _popupListSource.Entries = popups;

            LoadedContent content = SynchronousUniTask.Result(_loader.LoadAsync(CancellationToken.None));

            Assert.AreEqual(_configSource.Document, content.GameConfigDocument);
            Assert.AreSame(minigames, content.Minigames);
            Assert.AreSame(popups, content.Popups);
            Assert.AreSame(_parentPrefab, content.PopupParentPrefab);
        }

        [Test]
        public void LoadAsync_PassesItsCancellationTokenToEverySource()
        {
            using CancellationTokenSource cancellation = new();

            SynchronousUniTask.Result(_loader.LoadAsync(cancellation.Token));

            Assert.AreEqual(cancellation.Token, _configSource.LastToken, nameof(FakeGameConfigSource));
            Assert.AreEqual(cancellation.Token, _minigameListSource.LastToken, nameof(FakeMinigameListSource));
            Assert.AreEqual(cancellation.Token, _popupListSource.LastToken, nameof(FakePopupListSource));
            Assert.AreEqual(cancellation.Token, _popupParentSource.LastToken, nameof(FakePopupParentSource));
        }

        /// <remarks>
        /// See docs/architecture.md, "Exception hierarchy".
        /// </remarks>
        [Test]
        public void AFailingConfigSource_PropagatesItsTypedException()
        {
            _configSource.FailWith = new MissingAssetException("GameConfig", "Game config");

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Result(_loader.LoadAsync(CancellationToken.None)));
        }

        [Test]
        public void AFailingMinigameListSource_PropagatesItsTypedException()
        {
            _minigameListSource.FailWith = new MissingAssetException("Minigames/MinigameList", "Minigame list");

            MissingAssetException error = Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Result(_loader.LoadAsync(CancellationToken.None)));

            Assert.AreEqual("Minigames/MinigameList", error.AssetPath);
        }

        [Test]
        public void AFailingPopupListSource_PropagatesItsTypedException()
        {
            _popupListSource.FailWith = new MissingAssetException("Popups/PopupList", "Popup list");

            MissingAssetException error = Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Result(_loader.LoadAsync(CancellationToken.None)));

            Assert.AreEqual("Popups/PopupList", error.AssetPath);
        }

        [Test]
        public void AFailingPopupParentSource_PropagatesItsTypedException()
        {
            _popupParentSource.FailWith = new MissingAssetException("Popups/PopupParent", "Popup parent prefab");

            MissingAssetException error = Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Result(_loader.LoadAsync(CancellationToken.None)));

            Assert.AreEqual("Popups/PopupParent", error.AssetPath);
        }

        /// <remarks>
        /// See docs/architecture.md, "Boot".
        /// </remarks>
        [Test]
        public void AFailingSource_StopsTheLoadRatherThanHandingBackHalfTheContent()
        {
            _minigameListSource.FailWith = new MissingAssetException("Minigames/MinigameList", "Minigame list");

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Result(_loader.LoadAsync(CancellationToken.None)));

            Assert.AreEqual(0, _popupListSource.ReadCallCount, "the load carried on past a failure");
            Assert.AreEqual(0, _popupParentSource.ReadCallCount, "the load carried on past a failure");
        }
    }
}

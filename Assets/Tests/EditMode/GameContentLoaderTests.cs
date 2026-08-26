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
    // GameContentLoader is the whole of booting worth testing: the bootstrapper around it is a
    // scene load and a CreateChild call. Fakes throughout, with no scene and no scope.
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

        [Test]
        public void LoadAsync_ReadsEverySourceExactlyOnce()
        {
            // Once, not merely at least once: a source read twice is a source downloaded twice.
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

        // A source failing is the ordinary case. The typed exception has to survive the trip out
        // through the async machinery, or the caller cannot tell it from anything else.
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

        [Test]
        public void AFailingSource_StopsTheLoadRatherThanHandingBackHalfTheContent()
        {
            // Nothing downstream may see a LoadedContent with a hole in it, which is why the
            // services that consume it need no "has it loaded" guard.
            _minigameListSource.FailWith = new MissingAssetException("Minigames/MinigameList", "Minigame list");

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Result(_loader.LoadAsync(CancellationToken.None)));

            Assert.AreEqual(0, _popupListSource.ReadCallCount, "the load carried on past a failure");
            Assert.AreEqual(0, _popupParentSource.ReadCallCount, "the load carried on past a failure");
        }
    }
}

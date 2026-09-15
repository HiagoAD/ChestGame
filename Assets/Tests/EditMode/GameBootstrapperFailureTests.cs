using System;
using System.Text.RegularExpressions;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Core;
using Company.ChestGame.Saving;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Covers what <see cref="GameBootstrapper.StartAsync"/> does when it cannot boot, without a
    /// scene.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Telling the player what boot is doing".
    /// See docs/testing.md, "What lives where".
    /// </remarks>
    public class GameBootstrapperFailureTests
    {
        private const string LOADING_MESSAGE = "Loading...";

        private FakeGameConfigSource _configSource;
        private RecordingBootStatus _status;
        private FakeSaveStore _saveStore;
        private ISaveService _saveService;
        private SaveScheduler<GameMetaSaveDocument> _metaScheduler;

        private GameBootstrapper _bootstrapper;

        /// <remarks>
        /// See docs/architecture.md, "Boot".
        /// </remarks>
        [SetUp]
        public void SetUp()
        {
            _configSource = new FakeGameConfigSource();
            _status = new RecordingBootStatus();
            _saveStore = new FakeSaveStore();
            _saveService = new SaveService(new JsonCodec(), new NoProtection(), _saveStore);
            _metaScheduler = new SaveScheduler<GameMetaSaveDocument>(_saveService, GameMetaSaveDocument.SaveKey, new FakeGameClock());

            GameContentLoader loader = new(
                _configSource,
                new FakeMinigameListSource(),
                new FakePopupListSource(),
                new FakePopupParentSource());

            _bootstrapper = new GameBootstrapper(loader, null, _status, _saveService, _metaScheduler);
        }

        [TearDown]
        public void TearDown() => _metaScheduler.Dispose();

        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        [Test]
        public void StartAsync_WhenContentCannotBeLoaded_TellsThePlayerWhy()
        {
            MissingAssetException failure = new("GameConfig", "Game config");
            _configSource.FailWith = failure;

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            Assert.AreNotEqual(LOADING_MESSAGE, _status.LastMessage,
                "the boot screen was left narrating a step that had already failed");
            StringAssert.Contains(failure.Message, _status.LastMessage,
                "the reason is the one thing shipping the Core group local exists to be able to say");
        }

        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        [Test]
        public void StartAsync_WhenContentCannotBeLoaded_KeepsTheStackTraceOutOfTheUI()
        {
            MissingAssetException failure = new("GameConfig", "Game config");
            _configSource.FailWith = failure;

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            StringAssert.DoesNotContain(nameof(MissingAssetException), _status.LastMessage,
                "the exception's own type name reaches the label only through ToString");
            StringAssert.DoesNotContain(nameof(GameBootstrapper), _status.LastMessage,
                "a stack frame reached the label");
        }

        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        [Test]
        public void StartAsync_WhenContentCannotBeLoaded_StillPropagatesTheTypedFailure()
        {
            _configSource.FailWith = new MissingAssetException("Minigames/MinigameList", "Minigame list");

            MissingAssetException error = Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            Assert.AreEqual("Minigames/MinigameList", error.AssetPath, "and it is the original, not a wrapper");
        }

        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        [Test]
        public void StartAsync_WhenBootIsCancelled_SaysNothingToThePlayer()
        {
            _configSource.FailWith = new OperationCanceledException();

            Assert.Catch<OperationCanceledException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            Assert.AreEqual(LOADING_MESSAGE, _status.LastMessage,
                "boot reported a failure for what was only a shutdown");
        }

        [Test]
        public void StartAsync_RecordsALaunch_EvenWhenContentCannotBeLoaded()
        {
            _configSource.FailWith = new MissingAssetException("GameConfig", "Game config");

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            _metaScheduler.FlushBlocking();
            GameMetaSaveDocument meta = SynchronousUniTask.Result(
                _saveService.LoadAsync<GameMetaSaveDocument>(GameMetaSaveDocument.SaveKey, CancellationToken.None));

            Assert.AreEqual(1, meta.Launches);
            Assert.Greater(meta.FirstLaunchUnixMs, 0);
            Assert.AreEqual(meta.FirstLaunchUnixMs, meta.LastPlayedUnixMs);
        }

        [Test]
        public void StartAsync_OnASecondLaunch_IncrementsLaunchesAndKeepsTheFirstLaunchTimestamp()
        {
            _configSource.FailWith = new MissingAssetException("GameConfig", "Game config");

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));
            _metaScheduler.FlushBlocking();
            GameMetaSaveDocument first = SynchronousUniTask.Result(
                _saveService.LoadAsync<GameMetaSaveDocument>(GameMetaSaveDocument.SaveKey, CancellationToken.None));

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));
            _metaScheduler.FlushBlocking();
            GameMetaSaveDocument second = SynchronousUniTask.Result(
                _saveService.LoadAsync<GameMetaSaveDocument>(GameMetaSaveDocument.SaveKey, CancellationToken.None));

            Assert.AreEqual(2, second.Launches);
            Assert.AreEqual(first.FirstLaunchUnixMs, second.FirstLaunchUnixMs,
                "the first-launch timestamp must never move once set");
        }

        /// <remarks>
        /// See docs/saving.md, "A corrupt meta save is recoverable; a corrupt currency save is not".
        /// </remarks>
        [Test]
        public void StartAsync_WhenTheMetaSaveIsCorrupt_LogsAndStillPropagatesTheContentFailure()
        {
            _saveStore.Seed(GameMetaSaveDocument.SaveKey, System.Text.Encoding.UTF8.GetBytes("not json"));
            _configSource.FailWith = new MissingAssetException("GameConfig", "Game config");

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("The meta save could not be read and is being reset")));

            MissingAssetException error = Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            Assert.AreEqual("GameConfig", error.AssetPath,
                "a corrupt meta save must not brick boot or replace the real failure with a SaveException");
        }

        /// <summary>
        /// Records the last message <see cref="GameBootstrapper"/> reported through
        /// <see cref="IBootStatus"/>, so a test can assert on it directly.
        /// </summary>
        /// <remarks>
        /// See docs/testing.md, "RecordingBootStatus, and what it makes assertable".
        /// </remarks>
        private class RecordingBootStatus : IBootStatus
        {
            public string LastMessage { get; private set; }

            public void Report(string message) => LastMessage = message;
        }
    }
}

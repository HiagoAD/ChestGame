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
    // What boot does when it cannot boot. GameBootstrapperTests proves the happy path in play mode;
    // this half needs no scene, because a content load that fails never reaches one. The failure
    // matters more: shipping the Core group local buys nothing unless something reports through
    // IBootStatus.
    public class GameBootstrapperFailureTests
    {
        private const string LOADING_MESSAGE = "Loading...";

        private FakeGameConfigSource _configSource;
        private RecordingBootStatus _status;
        private FakeSaveStore _saveStore;
        private ISaveService _saveService;
        private SaveScheduler<GameMetaSaveDocument> _metaScheduler;

        private GameBootstrapper _bootstrapper;

        [SetUp]
        public void SetUp()
        {
            _configSource = new FakeGameConfigSource();
            _status = new RecordingBootStatus();
            _saveStore = new FakeSaveStore();
            _saveService = new SaveService(new JsonCodec(), new NoProtection(), _saveStore);
            _metaScheduler = new SaveScheduler<GameMetaSaveDocument>(_saveService, GameMetaSaveDocument.SaveKey, new FakeGameClock());

            // Only the first source is ever reached: the loader stops at a failure rather than
            // reading on. The rest are present because the loader needs four.
            GameContentLoader loader = new(
                _configSource,
                new FakeMinigameListSource(),
                new FakePopupListSource(),
                new FakePopupParentSource());

            // No root scope, because it is not touched until the step after the load. Moving
            // CreateChild ahead of the load would fail here with a NullReferenceException, which is
            // the right answer.
            _bootstrapper = new GameBootstrapper(loader, null, _status, _saveService, _metaScheduler);
        }

        [TearDown]
        public void TearDown() => _metaScheduler.Dispose();

        [Test]
        public void StartAsync_WhenContentCannotBeLoaded_TellsThePlayerWhy()
        {
            // The whole finding: a corrupt bundle or a malformed document used to escape into
            // VContainer and leave the boot screen narrating a step that had already failed.
            MissingAssetException failure = new("GameConfig", "Game config");
            _configSource.FailWith = failure;

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            Assert.AreNotEqual(LOADING_MESSAGE, _status.LastMessage,
                "the boot screen was left narrating a step that had already failed");
            StringAssert.Contains(failure.Message, _status.LastMessage,
                "the reason is the one thing shipping the Core group local exists to be able to say");
        }

        [Test]
        public void StartAsync_WhenContentCannotBeLoaded_KeepsTheStackTraceOutOfTheUI()
        {
            // Message, not ToString. A stack trace on a boot screen buries the one line that might
            // have meant something, and it is what the shortest fix to the test above would put
            // there.
            MissingAssetException failure = new("GameConfig", "Game config");
            _configSource.FailWith = failure;

            Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            StringAssert.DoesNotContain(nameof(MissingAssetException), _status.LastMessage,
                "the exception's own type name reaches the label only through ToString");
            StringAssert.DoesNotContain(nameof(GameBootstrapper), _status.LastMessage,
                "a stack frame reached the label");
        }

        [Test]
        public void StartAsync_WhenContentCannotBeLoaded_StillPropagatesTheTypedFailure()
        {
            // Reported and rethrown, not swallowed. Returning normally would claim boot succeeded
            // when the game scene was never loaded, and take the exception away from the developer
            // who has to fix it.
            _configSource.FailWith = new MissingAssetException("Minigames/MinigameList", "Minigame list");

            MissingAssetException error = Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            Assert.AreEqual("Minigames/MinigameList", error.AssetPath, "and it is the original, not a wrapper");
        }

        [Test]
        public void StartAsync_WhenBootIsCancelled_SaysNothingToThePlayer()
        {
            // Cancellation is the application quitting, not the game failing to start. Reporting it
            // would turn an ordinary shutdown into the error the next bug report is about.
            _configSource.FailWith = new OperationCanceledException();

            Assert.Catch<OperationCanceledException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            Assert.AreEqual(LOADING_MESSAGE, _status.LastMessage,
                "boot reported a failure for what was only a shutdown");
        }

        // --- Meta: recorded early enough that a later content failure does not lose it ------

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

        [Test]
        public void StartAsync_WhenTheMetaSaveIsCorrupt_LogsAndStillPropagatesTheContentFailure()
        {
            // Never bytes SaveService's own pipeline could have written - PayloadUnreadable, the
            // same failure a genuinely truncated save would report.
            _saveStore.Seed(GameMetaSaveDocument.SaveKey, System.Text.Encoding.UTF8.GetBytes("not json"));
            _configSource.FailWith = new MissingAssetException("GameConfig", "Game config");

            LogAssert.Expect(LogType.Error, new Regex(Regex.Escape("The meta save could not be read and is being reset")));

            MissingAssetException error = Assert.Throws<MissingAssetException>(
                () => SynchronousUniTask.Complete(_bootstrapper.StartAsync(CancellationToken.None)));

            Assert.AreEqual("GameConfig", error.AssetPath,
                "a corrupt meta save must not brick boot or replace the real failure with a SaveException");
        }

        // What IBootStatus was told, which is otherwise invisible: SilentBootStatus keeps the
        // bootstrapper free of null checks and its narration untestable at the same time.
        private class RecordingBootStatus : IBootStatus
        {
            public string LastMessage { get; private set; }

            public void Report(string message) => LastMessage = message;
        }
    }
}

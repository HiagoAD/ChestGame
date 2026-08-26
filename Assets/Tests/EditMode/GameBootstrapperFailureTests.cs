using System;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Core;
using Company.ChestGame.Tests.Common;
using NUnit.Framework;

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

        private GameBootstrapper _bootstrapper;

        [SetUp]
        public void SetUp()
        {
            _configSource = new FakeGameConfigSource();
            _status = new RecordingBootStatus();

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
            _bootstrapper = new GameBootstrapper(loader, null, _status);
        }

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

        // What IBootStatus was told, which is otherwise invisible: SilentBootStatus keeps the
        // bootstrapper free of null checks and its narration untestable at the same time.
        private class RecordingBootStatus : IBootStatus
        {
            public string LastMessage { get; private set; }

            public void Report(string message) => LastMessage = message;
        }
    }
}

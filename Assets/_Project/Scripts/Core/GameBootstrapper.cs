using System;
using System.Threading;
using Company.ChestGame.Minigame;
using Company.ChestGame.Saving;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    // Boot scene's only job: load the content, build the scope that consumes it, fetch whatever
    // the minigames want up front, then open the game scene with that scope already standing.
    // Nothing downstream exists before its data arrived, so no service ever has to ask whether
    // loading has finished.
    public class GameBootstrapper : IAsyncStartable
    {
        public const string GAME_SCENE_NAME = "Game";

        private const string LOADING_MESSAGE = "Loading...";
        private const string PREPARING_MESSAGE = "Preparing content...";
        private const string STARTING_MESSAGE = "Starting...";

        // What the boot screen says instead of sitting on "Loading..." forever. Only the
        // exception's Message follows it: a stack trace on a boot screen hides the one line that
        // might tell a player something.
        private const string FAILED_MESSAGE = "Could not start the game.";

        private readonly GameContentLoader _loader;
        private readonly LifetimeScope _rootScope;
        private readonly IBootStatus _status;
        private readonly ISaveService _saveService;
        private readonly SaveScheduler<GameMetaSaveDocument> _metaScheduler;

        private LifetimeScope _gameScope;

        public GameBootstrapper(GameContentLoader loader, LifetimeScope rootScope, IBootStatus status,
            ISaveService saveService, SaveScheduler<GameMetaSaveDocument> metaScheduler)
        {
            _loader = loader;
            _rootScope = rootScope;
            _status = status;
            _saveService = saveService;
            _metaScheduler = metaScheduler;
        }

        // A failure is reported to the label, then rethrown rather than swallowed. Returning
        // normally here would claim the game scene loaded and its services exist when neither did.
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            try
            {
                // Early, so a content failure below still gets recorded as a launch. Meta's own
                // failure never becomes a boot failure - see RecordLaunchAsync.
                await RecordLaunchAsync(cancellation);

                _status.Report(LOADING_MESSAGE);

                LoadedContent content = await _loader.LoadAsync(cancellation);

                _gameScope = _rootScope.CreateChild(builder => GameLifetimeScope.RegisterLoadedServices(builder, content));

                // Resolved from the scope just built: the preloader needs the catalog, which does
                // not exist until the content it was built from arrived.
                _status.Report(PREPARING_MESSAGE);
                await _gameScope.Container.Resolve<MinigameContentPreloader>()
                    .PreloadAsync(new DownloadStatus(_status), cancellation);

                _status.Report(STARTING_MESSAGE);

                // Makes the game scene's own scope a child of the one built above, without that
                // scene holding a reference to an object that did not exist when it was authored.
                using (LifetimeScope.EnqueueParent(_gameScope))
                {
                    // ToUniTask rather than awaiting the AsyncOperation: UniTask compiles that
                    // awaiter out under #if !UNITY_2023_1_OR_NEWER, so on this editor
                    // `await operation` binds to the IEnumerator overload and fails to compile.
                    await SceneManager.LoadSceneAsync(GAME_SCENE_NAME).ToUniTask(cancellationToken: cancellation);
                }
            }
            // Cancellation is the scope disposing as the application quits, not boot failing, and
            // there is nobody left to read a message by then.
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                _status.Report($"{FAILED_MESSAGE} {failure.Message}");
                throw;
            }
        }

        // Only the load is guarded: an unreadable meta save is reset to a fresh document rather
        // than failing boot. A failure past this point is a bug, not a corrupt save, and fails
        // boot like any other.
        private async UniTask RecordLaunchAsync(CancellationToken ct)
        {
            GameMetaSaveDocument meta;
            try
            {
                meta = await _saveService.LoadAsync<GameMetaSaveDocument>(GameMetaSaveDocument.SaveKey, ct);
            }
            catch (SaveException exception)
            {
                Debug.LogError($"The meta save could not be read and is being reset: {exception.Message}");
                meta = new GameMetaSaveDocument();
            }

            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            meta.Launches++;
            if (meta.FirstLaunchUnixMs == 0) meta.FirstLaunchUnixMs = now;
            meta.LastPlayedUnixMs = now;

            _metaScheduler.MarkDirty(meta);
        }

        // The preloader reports a number because a number is all it knows; wording is the shell's.
        private sealed class DownloadStatus : IProgress<float>
        {
            private readonly IBootStatus _status;

            public DownloadStatus(IBootStatus status) => _status = status;

            public void Report(float value) =>
                _status.Report($"{PREPARING_MESSAGE} {Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%");
        }
    }
}

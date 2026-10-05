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
    /// <summary>
    /// Boot scene's only job: load the content, build the scope that consumes it, fetch whatever
    /// the minigames want up front, then open the game scene with that scope already standing.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Boot".
    /// </remarks>
    public class GameBootstrapper : IAsyncStartable
    {
        public const string GAME_SCENE_NAME = "Game";

        private const string LOADING_MESSAGE = "Loading...";
        private const string PREPARING_MESSAGE = "Preparing content...";
        private const string STARTING_MESSAGE = "Starting...";

        /// <summary>
        /// Prefix for the boot screen's failure line; <see cref="Exception.Message"/> follows it.
        /// </summary>
        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
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

        /// <summary>
        /// Runs the boot sequence: records the launch, loads content, builds the loaded-content
        /// scope, preloads minigame content, then loads the game scene.
        /// </summary>
        /// <param name="cancellation">
        /// Token observed throughout boot. A cancellation is not reported as a failure.
        /// </param>
        /// <exception cref="Exception">
        /// Rethrown, after being reported through <see cref="IBootStatus"/>, for any failure other
        /// than cancellation.
        /// </exception>
        /// <remarks>
        /// See docs/architecture.md, "Boot".
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// See docs/context/self-contained-minigames.md, "6. Traps - Addressables and Unity 6".
        /// </remarks>
        public async UniTask StartAsync(CancellationToken cancellation)
        {
            try
            {
                await RecordLaunchAsync(cancellation);

                _status.Report(LOADING_MESSAGE);

                LoadedContent content = await _loader.LoadAsync(cancellation);

                _gameScope = _rootScope.CreateChild(builder => GameLifetimeScope.RegisterLoadedServices(builder, content));

                _status.Report(PREPARING_MESSAGE);
                await _gameScope.Container.Resolve<MinigameContentPreloader>()
                    .PreloadAsync(new DownloadStatus(_status), cancellation);

                _status.Report(STARTING_MESSAGE);

                using (LifetimeScope.EnqueueParent(_gameScope))
                {
                    await SceneManager.LoadSceneAsync(GAME_SCENE_NAME).ToUniTask(cancellationToken: cancellation);
                }
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                _status.Report($"{FAILED_MESSAGE} {failure.Message}");
                throw;
            }
        }

        /// <summary>
        /// Loads the meta save, increments the launch count, stamps the play times, and hands the
        /// result to the meta scheduler.
        /// </summary>
        /// <param name="ct">Cancellation token for the load.</param>
        /// <remarks>
        /// See docs/architecture.md, "Boot".
        /// </remarks>
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

        /// <summary>
        /// Turns the preloader's 0..1 progress fraction into a boot status line.
        /// </summary>
        /// <remarks>
        /// See docs/content-delivery.md, "Progress reporting".
        /// </remarks>
        private sealed class DownloadStatus : IProgress<float>
        {
            private readonly IBootStatus _status;

            public DownloadStatus(IBootStatus status) => _status = status;

            public void Report(float value) =>
                _status.Report($"{PREPARING_MESSAGE} {Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%");
        }
    }
}

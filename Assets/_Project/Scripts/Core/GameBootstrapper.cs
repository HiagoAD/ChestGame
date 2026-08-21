using System;
using System.Threading;
using Company.ChestGame.Minigame;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    // The boot scene's only job, in the order the ordering guarantee depends on: load the content,
    // build the scope that consumes it, fetch whatever the minigames want up front, then open the
    // game scene with that scope already standing.
    //
    // Nothing in the game scene can therefore be constructed before its data arrived, which is why
    // no service anywhere has to ask whether loading has finished.
    public class GameBootstrapper : IAsyncStartable
    {
        public const string GAME_SCENE_NAME = "Game";

        private const string LOADING_MESSAGE = "Loading...";
        private const string PREPARING_MESSAGE = "Preparing content...";
        private const string STARTING_MESSAGE = "Starting...";

        private readonly GameContentLoader _loader;
        private readonly LifetimeScope _rootScope;
        private readonly IBootStatus _status;

        private LifetimeScope _gameScope;

        public GameBootstrapper(GameContentLoader loader, LifetimeScope rootScope, IBootStatus status)
        {
            _loader = loader;
            _rootScope = rootScope;
            _status = status;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            _status.Report(LOADING_MESSAGE);

            LoadedContent content = await _loader.LoadAsync(cancellation);

            _gameScope = _rootScope.CreateChild(builder => GameLifetimeScope.RegisterLoadedServices(builder, content));

            // Resolved from the scope that was just built rather than constructed here: the
            // preloader needs the catalog, and the catalog does not exist until the content it was
            // built from arrived. The walking and the summing belong to the preloader; what is here
            // is only where in the boot order it happens.
            _status.Report(PREPARING_MESSAGE);
            await _gameScope.Container.Resolve<MinigameContentPreloader>()
                .PreloadAsync(new DownloadStatus(_status), cancellation);

            _status.Report(STARTING_MESSAGE);

            // EnqueueParent is what makes the game scene's own scope a child of the one built
            // above, without that scene holding a reference to an object that did not exist when
            // it was authored.
            using (LifetimeScope.EnqueueParent(_gameScope))
            {
                // ToUniTask rather than awaiting the AsyncOperation directly: UniTask compiles
                // its AsyncOperation awaiter out under #if !UNITY_2023_1_OR_NEWER, so on this
                // editor `await operation` binds to the IEnumerator overload and fails to compile.
                await SceneManager.LoadSceneAsync(GAME_SCENE_NAME).ToUniTask(cancellationToken: cancellation);
            }
        }

        // Turns the preloader's fraction into the one thing the boot scene can show. The preloader
        // reports a number because a number is all it knows; wording is the shell's business.
        private sealed class DownloadStatus : IProgress<float>
        {
            private readonly IBootStatus _status;

            public DownloadStatus(IBootStatus status) => _status = status;

            public void Report(float value) =>
                _status.Report($"{PREPARING_MESSAGE} {Mathf.RoundToInt(Mathf.Clamp01(value) * 100f)}%");
        }
    }
}

using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace Company.ChestGame.Core
{
    // The boot scene's only job, in the order the ordering guarantee depends on: load the content,
    // build the scope that consumes it, then open the game scene with that scope already standing.
    //
    // Nothing in the game scene can therefore be constructed before its data arrived, which is why
    // no service anywhere has to ask whether loading has finished.
    public class GameBootstrapper : IAsyncStartable
    {
        public const string GAME_SCENE_NAME = "Game";

        private readonly GameContentLoader _loader;
        private readonly LifetimeScope _rootScope;

        private LifetimeScope _gameScope;

        public GameBootstrapper(GameContentLoader loader, LifetimeScope rootScope)
        {
            _loader = loader;
            _rootScope = rootScope;
        }

        public async UniTask StartAsync(CancellationToken cancellation)
        {
            LoadedContent content = await _loader.LoadAsync(cancellation);

            _gameScope = _rootScope.CreateChild(builder => GameLifetimeScope.RegisterLoadedServices(builder, content));

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
    }
}

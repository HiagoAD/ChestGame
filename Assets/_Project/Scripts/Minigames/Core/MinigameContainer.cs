using System.Threading;
using Company.ChestGame.Assets;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using VContainer;
using VContainer.Unity;

namespace Company.ChestGame.Minigame.Core
{
    public class MinigameContainer : IInitializable
    {
        protected bool _running;

        public virtual bool Running => _running;

        public MinigameViewBase ViewInstance { get; private set; }


        public AssetReferenceGameObject ViewRef { get; private set; }
        public MinigameControllerBase ControllerInstance { get; private set; }

        [Inject]
        private IObjectResolver _resolver;

        [Inject]
        private IAssetProvider _assets;

        // The container has to know its own definition because the definition is what owns the
        // minigame-specific content hook, and that hook runs from BeginAsync.
        private MinigameBaseSO _definition;


        public virtual void Set(MinigameControllerBase controller, AssetReferenceGameObject view, MinigameBaseSO definition)
        {
            ControllerInstance = controller;
            ViewRef = view;
            _definition = definition;
        }

        public virtual void Initialize()
        {

        }

        // Everything content-shaped happens here, together, because none of it can happen any
        // earlier: the view and the minigame's own content are behind references, and a reference
        // resolves asynchronously.
        //
        // A controller builds state from its own config and is injected on top of it, so the
        // configure-before-inject ordering below is the framework promise, not an accident of
        // where the lines sit.
        public virtual async UniTask BeginAsync(Transform parent, CancellationToken ct)
        {
            try
            {
                await EnsureContentIsDownloadedAsync(ct);

                GameObject prefab = await _assets.LoadAsync<GameObject>(ViewRef, ct);

                await _definition.ConfigureControllerAsync(ControllerInstance, _assets, ct);
                _resolver.Inject(ControllerInstance);

                // Instantiated through the resolver rather than through Addressables, so the view
                // and everything under it are injected the way every other object in the game is.
                ViewInstance = _resolver.Instantiate(prefab.GetComponent<MinigameViewBase>(), parent);
                ViewInstance.SetController(ControllerInstance);
                _running = true;
            }
            catch
            {
                // Nothing else can ever let these go. End is a no-op until _running is true, which
                // is the last line above, so a load that threw or a start that was cancelled
                // halfway would otherwise leave whatever already arrived resident for the rest of
                // the session with no handle on it anywhere.
                //
                // Releasing rather than making End unconditional is deliberate: End has to stay
                // safe on a container that never began, and "release what I took" is the only
                // statement that is true in both cases.
                ReleaseContent();
                throw;
            }
        }

        // Safe to call on a minigame that was never begun, or begun and already ended, so callers
        // can tear down unconditionally. Synchronous on purpose: it releases the asset handles
        // BeginAsync took, and releasing a handle needs no waiting.
        public virtual void End()
        {
            if (!_running) return;

            _running = false;
            ControllerInstance.Dispose();

            if (ViewInstance != null)
            {
                Object.Destroy(ViewInstance.gameObject);
            }
            ViewInstance = null;

            ReleaseContent();
        }

        // A minigame whose content was authored to arrive only when it is asked for is asked for
        // here, which is the one moment the game knows it is about to be needed. Content that is
        // already cached, shipped local, or set to preload measures zero and costs a size query.
        private async UniTask EnsureContentIsDownloadedAsync(CancellationToken ct)
        {
            if (_definition.LoadPolicy != MinigameLoadPolicy.OnDemand) return;

            string label = _definition.ContentLabel;

            // A minigame that names no content has nothing to fetch, and a blank label is not a key
            // — the same reasoning the catalogs apply to a blank id.
            if (string.IsNullOrWhiteSpace(label)) return;

            long size = await _assets.GetDownloadSizeAsync(label, ct);
            if (size <= 0) return;

            await _assets.DownloadAsync(label, null, ct);
        }

        private void ReleaseContent()
        {
            _assets.Release(ViewRef);
            _definition.ReleaseContent(_assets);
        }
    }

}

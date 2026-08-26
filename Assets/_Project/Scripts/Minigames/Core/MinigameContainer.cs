using System;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using VContainer;
using VContainer.Unity;
// System brings a second Object with it; the alias keeps End's Object.Destroy meaning what it did.
using Object = UnityEngine.Object;

namespace Company.ChestGame.Minigame.Core
{
    public class MinigameContainer
    {
        protected bool _running;

        public bool Running => _running;

        public MinigameViewBase ViewInstance { get; private set; }


        public AssetReferenceGameObject ViewRef { get; private set; }
        public MinigameControllerBase ControllerInstance { get; private set; }

        [Inject]
        private IObjectResolver _resolver;

        [Inject]
        private IAssetProvider _assets;

        // Known here because the definition owns the content hook, which runs from BeginAsync.
        private MinigameBaseSO _definition;

        // How long the on-demand fetch may take before the player is told it did not work.
        // Overridable rather than authored, and longer than anything Addressables bounds itself.
        // See docs/content-delivery.md.
        protected virtual TimeSpan ContentDownloadTimeout => TimeSpan.FromSeconds(90);


        public void Set(MinigameControllerBase controller, AssetReferenceGameObject view, MinigameBaseSO definition)
        {
            ControllerInstance = controller;
            ViewRef = view;
            _definition = definition;
        }

        // Everything content-shaped happens here, because a reference resolves asynchronously and
        // none of it can happen earlier. The configure-before-inject ordering below is a framework
        // promise, not an accident of where the lines sit: a controller builds state from its own
        // config and is injected on top of it.
        public async UniTask BeginAsync(Transform parent, CancellationToken ct)
        {
            // Loud rather than a silent early return, and deliberately not symmetrical with End:
            // a second start takes a second ref-count on the view that the single End can never
            // give back.
            if (_running) throw new MinigameAlreadyRunningException(_definition != null ? _definition.Id : null);

            try
            {
                await EnsureContentIsDownloadedAsync(ct);

                GameObject prefab = await _assets.LoadAsync<GameObject>(ViewRef, ct);

                await _definition.ConfigureControllerAsync(ControllerInstance, _assets, ct);
                _resolver.Inject(ControllerInstance);

                // Through the resolver rather than Addressables, so the view and everything under
                // it are injected the way every other object in the game is.
                ViewInstance = _resolver.Instantiate(prefab.GetComponent<MinigameViewBase>(), parent);
                ViewInstance.SetController(ControllerInstance);
                _running = true;
            }
            catch
            {
                // Nothing else can ever let these go: End is a no-op until _running is true, which
                // is the last line above. The view is destroyed here for the same reason, one
                // object further on.
                if (ViewInstance != null)
                {
                    Object.Destroy(ViewInstance.gameObject);
                    ViewInstance = null;
                }

                ReleaseContent();
                throw;
            }
        }

        // Safe on a minigame that was never begun, or begun and already ended, so callers can tear
        // down unconditionally. Synchronous because it releases handles, never instances.
        public void End()
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

        // The one moment the game knows this content is about to be needed. Already cached,
        // shipped local or preloaded content measures zero and costs one size query.
        private async UniTask EnsureContentIsDownloadedAsync(CancellationToken ct)
        {
            if (_definition.LoadPolicy != MinigameLoadPolicy.OnDemand) return;

            // The blank-label rule belongs to the descriptor, which owns the field, so both
            // delivery paths get the same answer.
            if (!_definition.TryGetContentLabel(out string label)) return;

            // A deadline at all because a stalled download never fails: nothing throws, nothing
            // returns, and the disabled start button stays disabled. Linked, so a scene going away
            // mid-fetch ends the wait immediately. Read once, so the exception reports the budget
            // that was actually given.
            TimeSpan budget = ContentDownloadTimeout;

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(budget);

            try
            {
                long size = await _assets.GetDownloadSizeAsync(label, deadline.Token);
                if (size <= 0) return;

                await _assets.DownloadAsync(label, null, deadline.Token);
            }
            // Only the deadline firing on its own is worth telling a player about; the caller
            // cancelling means the scene is going away. Both at once counts as the caller's.
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new ContentDownloadTimeoutException(label, budget);
            }
        }

        private void ReleaseContent()
        {
            _assets.Release(ViewRef);
            _definition.ReleaseContent(_assets);
        }
    }

}

using System;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using VContainer;
// VContainer.Unity is still needed after IInitializable went: Instantiate is an extension method
// on IObjectResolver and it lives in that namespace.
using VContainer.Unity;
// System is here for TimeSpan, and it brings a second Object with it; the alias keeps End's
// Object.Destroy meaning what it always did.
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

        // The container has to know its own definition because the definition is what owns the
        // minigame-specific content hook, and that hook runs from BeginAsync.
        private MinigameBaseSO _definition;

        // How long the on-demand fetch is allowed to take before the player is told it did not
        // work. Overridable rather than authored: a minigame whose payload justifies a longer wait
        // widens it on its own container subclass — which every minigame already has — and a test
        // shortens it to milliseconds, without a tuning knob appearing on a document that ships to
        // players and without the framework growing a config surface it does not otherwise need.
        //
        // Ninety seconds is deliberately longer than anything Addressables bounds on its own. A
        // bundle request gives up after fifteen seconds in which not one byte arrived and is
        // retried twice, so the worst *bounded* failure is forty-five seconds and the package's own
        // typed error wins that race and reaches the player with a better reason than "it timed
        // out". This exists for the stalls the package does not bound at all.
        protected virtual TimeSpan ContentDownloadTimeout => TimeSpan.FromSeconds(90);


        public void Set(MinigameControllerBase controller, AssetReferenceGameObject view, MinigameBaseSO definition)
        {
            ControllerInstance = controller;
            ViewRef = view;
            _definition = definition;
        }

        // Everything content-shaped happens here, together, because none of it can happen any
        // earlier: the view and the minigame's own content are behind references, and a reference
        // resolves asynchronously.
        //
        // A controller builds state from its own config and is injected on top of it, so the
        // configure-before-inject ordering below is the framework promise, not an accident of
        // where the lines sit.
        public async UniTask BeginAsync(Transform parent, CancellationToken ct)
        {
            // Loud rather than a silent early return, and deliberately not symmetrical with End.
            // End has to be safe to call unconditionally because teardown runs from paths that
            // cannot know what happened; starting twice is nobody's teardown, it is a caller that
            // lost track of its own container. Returning quietly would hide that, and under
            // one-release-per-load it would also leak: a second BeginAsync takes a second ref-count
            // on the view that the single End can never give back.
            if (_running) throw new MinigameAlreadyRunningException(_definition != null ? _definition.Id : null);

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
                // The view is destroyed here and not left to End, which returns early while
                // _running is false — so an instance that was created and then had SetController
                // throw would otherwise sit in the scene, unreferenced and undestroyable, for the
                // rest of the session. The same leak the handles above have, one object further on.
                if (ViewInstance != null)
                {
                    Object.Destroy(ViewInstance.gameObject);
                    ViewInstance = null;
                }

                ReleaseContent();
                throw;
            }
        }

        // Safe to call on a minigame that was never begun, or begun and already ended, so callers
        // can tear down unconditionally. Synchronous on purpose: it releases the asset handles
        // BeginAsync took, and releasing a handle needs no waiting.
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

        // A minigame whose content was authored to arrive only when it is asked for is asked for
        // here, which is the one moment the game knows it is about to be needed. Content that is
        // already cached, shipped local, or set to preload measures zero and costs a size query.
        private async UniTask EnsureContentIsDownloadedAsync(CancellationToken ct)
        {
            if (_definition.LoadPolicy != MinigameLoadPolicy.OnDemand) return;

            // The rule belongs to the descriptor, which owns the field. It used to be stated here
            // too, and the two statements disagreed: this path skipped in silence while the
            // preloader warned, so whether an unauthored label was visible depended on which load
            // policy it happened to be paired with.
            if (!_definition.TryGetContentLabel(out string label)) return;

            // Linked rather than a bare deadline, so a scene that goes away mid-fetch still ends
            // the wait immediately instead of sitting out the rest of the budget.
            //
            // A deadline at all because a download that *stalls* is not a download that fails:
            // nothing throws, nothing returns, and the start button the shell disabled on the way
            // in stays disabled for the rest of the session with nothing on screen to say why.
            //
            // Read once, so the budget the exception reports is the budget that was actually given.
            TimeSpan budget = ContentDownloadTimeout;

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(budget);

            try
            {
                long size = await _assets.GetDownloadSizeAsync(label, deadline.Token);
                if (size <= 0) return;

                await _assets.DownloadAsync(label, null, deadline.Token);
            }
            // Which of the two tokens fired is the whole distinction, and the caller's token is the
            // only one that can be asked about after the fact. The caller cancelling means the
            // scene is going away and there is nobody left to tell, so it stays an
            // OperationCanceledException and travels out untouched. Only the deadline firing
            // becomes something under ChestGameException, because only then is a player still
            // sitting in front of a button waiting for an answer that is never coming.
            //
            // Both at once counts as the caller's: a teardown that happens to coincide with the
            // deadline is still a teardown, and popping a message onto a scene being unloaded
            // would be worse than saying nothing.
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

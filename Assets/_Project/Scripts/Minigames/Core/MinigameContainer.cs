using System;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using VContainer;
using VContainer.Unity;
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

        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        private MinigameBaseSO _definition;

        /// <summary>
        /// How long the on-demand fetch may take before the player is told it did not work.
        /// </summary>
        /// <remarks>
        /// See docs/content-delivery.md, "Timeouts".
        /// </remarks>
        protected virtual TimeSpan ContentDownloadTimeout => TimeSpan.FromSeconds(90);


        public void Set(MinigameControllerBase controller, AssetReferenceGameObject view, MinigameBaseSO definition)
        {
            ControllerInstance = controller;
            ViewRef = view;
            _definition = definition;
        }

        /// <summary>
        /// Everything content-shaped happens here: fetches this minigame's on-demand content if
        /// needed, loads the view, then runs the definition's configure hook and injects the
        /// controller before the view is instantiated. A controller builds state from its own
        /// config and is injected on top of it. When a step after injection fails, the injected
        /// controller is disposed before the view is destroyed and the content released; a failure
        /// before injection completes does not dispose it.
        /// </summary>
        /// <exception cref="MinigameAlreadyRunningException">The container is already running.</exception>
        /// <exception cref="ContentDownloadTimeoutException">
        /// The on-demand content deadline elapsed before the download finished.
        /// </exception>
        /// <exception cref="AssetLoadException">
        /// The on-demand content query or download, or the view load, resolved but failed.
        /// </exception>
        /// <exception cref="MissingAssetException">
        /// The on-demand content label or the view reference names nothing in the shipped catalog.
        /// </exception>
        /// <exception cref="OperationCanceledException">
        /// <paramref name="ct"/> was cancelled before the controller was injected, including when
        /// the last await completed after the cancel. Nothing is injected, instantiated or left
        /// running, and the content is released.
        /// </exception>
        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// See docs/minigames.md, "Starting twice is loud".
        /// See docs/minigames.md, "Failure during a start".
        /// </remarks>
        public async UniTask BeginAsync(Transform parent, CancellationToken ct)
        {
            if (_running) throw new MinigameAlreadyRunningException(_definition != null ? _definition.Id : null);

            bool injected = false;

            try
            {
                await EnsureContentIsDownloadedAsync(ct);

                GameObject prefab = await _assets.LoadAsync<GameObject>(ViewRef, ct);

                await _definition.ConfigureControllerAsync(ControllerInstance, _assets, ct);
                ct.ThrowIfCancellationRequested();

                _resolver.Inject(ControllerInstance);
                injected = true;

                ViewInstance = _resolver.Instantiate(prefab.GetComponent<MinigameViewBase>(), parent);
                ViewInstance.SetController(ControllerInstance);
                _running = true;
            }
            catch
            {
                if (injected)
                {
                    ControllerInstance.Dispose();
                }

                if (ViewInstance != null)
                {
                    Object.Destroy(ViewInstance.gameObject);
                    ViewInstance = null;
                }

                ReleaseContent();
                throw;
            }
        }

        /// <summary>
        /// Safe on a minigame that was never begun, or begun and already ended, so callers can tear
        /// down unconditionally.
        /// </summary>
        /// <remarks>
        /// See docs/minigames.md, "Teardown".
        /// </remarks>
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

        /// <exception cref="ContentDownloadTimeoutException">
        /// <see cref="ContentDownloadTimeout"/> elapsed before the download finished.
        /// </exception>
        /// <exception cref="AssetLoadException">The size query or the download resolved but failed.</exception>
        /// <exception cref="MissingAssetException">The content label names nothing in the shipped catalog.</exception>
        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// See docs/content-delivery.md, "Timeouts".
        /// See docs/content-delivery.md, "Which token fired".
        /// </remarks>
        private async UniTask EnsureContentIsDownloadedAsync(CancellationToken ct)
        {
            if (_definition.LoadPolicy != MinigameLoadPolicy.OnDemand) return;

            if (!_definition.TryGetContentLabel(out string label)) return;

            TimeSpan budget = ContentDownloadTimeout;

            using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(budget);

            try
            {
                long size = await _assets.GetDownloadSizeAsync(label, deadline.Token);
                if (size <= 0) return;

                await _assets.DownloadAsync(label, null, deadline.Token);
            }
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

using System;
using System.Threading;
using Company.ChestGame.Assets;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Company.ChestGame.Minigame.Core
{
    public abstract class MinigameBaseSO : ScriptableObject
    {
        [SerializeField] private string _id;

        /// <summary>
        /// The label every asset this minigame owns carries.
        /// </summary>
        [SerializeField] private string _contentLabel;
        [SerializeField] private MinigameLoadPolicy _loadPolicy;

        public string Id => _id;
        public string ContentLabel => _contentLabel;
        public MinigameLoadPolicy LoadPolicy => _loadPolicy;

        /// <summary>
        /// Returns the content label to fetch this minigame's content by. Returns <c>false</c> and
        /// logs a warning instead of throwing when no label is authored.
        /// </summary>
        /// <param name="label">The authored content label, or <c>null</c> when none is authored.</param>
        /// <returns><c>true</c> when a label is authored; otherwise <c>false</c>.</returns>
        /// <remarks>
        /// See docs/content-delivery.md, "When content arrives".
        /// </remarks>
        public bool TryGetContentLabel(out string label)
        {
            label = _contentLabel;

            if (!string.IsNullOrWhiteSpace(label)) return true;

            Debug.LogWarning(
                $"Minigame '{name}' names no content label, so none of its content can be fetched " +
                "as a unit, skipping it");
            return false;
        }

        public abstract Type ContainerType { get; }
        public abstract MinigameContainer GetMinigameContainer();

        /// <summary>
        /// Runs from <see cref="MinigameContainer.BeginAsync"/> before the controller is injected,
        /// so a controller can build state from its own content and still be injected on top of it.
        /// </summary>
        public virtual UniTask ConfigureControllerAsync(
            MinigameControllerBase controller, IAssetProvider assets, CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// The other half of <see cref="ConfigureControllerAsync"/>: whatever it loaded is dropped
        /// here.
        /// </summary>
        public virtual void ReleaseContent(IAssetProvider assets) { }
    }

    public abstract class MinigameBase<TController, TView, TMinigame> : MinigameBaseSO
    where TController : MinigameControllerBase, new()
    where TView : MinigameViewBase
    where TMinigame : MinigameContainer, new()
    {
        /// <summary>
        /// A reference, not the prefab.
        /// </summary>
        /// <remarks>
        /// See docs/minigames.md, "A definition names its content, it does not hold it".
        /// </remarks>
        [SerializeField] private AssetReferenceGameObject _viewRef;

        public AssetReferenceGameObject ViewRef => _viewRef;

        public override Type ContainerType => typeof(TMinigame);

        /// <summary>
        /// Construction only.
        /// </summary>
        /// <remarks>
        /// See docs/minigames.md, "Nothing loads while the container is built".
        /// </remarks>
        public override MinigameContainer GetMinigameContainer()
        {
            TMinigame minigame = new();

            minigame.Set(new TController(), _viewRef, this);
            return minigame;
        }

        public sealed override UniTask ConfigureControllerAsync(
            MinigameControllerBase controller, IAssetProvider assets, CancellationToken ct)
        {
            TController typed = (TController)controller;

            return ConfigureControllerAsync(typed, assets, ct);
        }

        protected virtual UniTask ConfigureControllerAsync(
            TController controller, IAssetProvider assets, CancellationToken ct) => UniTask.CompletedTask;
    }
}

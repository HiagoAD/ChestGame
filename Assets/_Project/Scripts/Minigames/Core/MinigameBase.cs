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
        // Authored rather than derived from the container type, which is what lets the shell start
        // a minigame without referencing the assembly that defines it.
        [SerializeField] private string _id;

        // The label every asset this minigame owns carries, and how its content is meant to arrive.
        // See docs/content-delivery.md.
        [SerializeField] private string _contentLabel;
        [SerializeField] private MinigameLoadPolicy _loadPolicy;

        public string Id => _id;
        public string ContentLabel => _contentLabel;
        public MinigameLoadPolicy LoadPolicy => _loadPolicy;

        // The one place the blank-label rule is stated, because both delivery paths need it and
        // used to answer it differently. Warned rather than thrown, following CatalogBuilder on a
        // blank id, and warned rather than ignored because the failure is otherwise silent.
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

        // The one hook a concrete minigame has for handing its controller what only it knows about.
        // Runs from BeginAsync before the controller is injected, so a controller can build state
        // from its own content and still be injected on top of it.
        public virtual UniTask ConfigureControllerAsync(
            MinigameControllerBase controller, IAssetProvider assets, CancellationToken ct) => UniTask.CompletedTask;

        // The other half of the hook: whatever ConfigureControllerAsync loaded is dropped here.
        public virtual void ReleaseContent(IAssetProvider assets) { }
    }

    public abstract class MinigameBase<TController, TView, TMinigame> : MinigameBaseSO
    where TController : MinigameControllerBase, new()
    where TView : MinigameViewBase
    where TMinigame : MinigameContainer, new()
    {
        // A reference, not the prefab. It serializes as a GUID rather than an object reference, so
        // loading the descriptor does not drag the minigame's bundle in behind it. A direct field
        // would undo that silently.
        [SerializeField] private AssetReferenceGameObject _viewRef;

        public AssetReferenceGameObject ViewRef => _viewRef;

        public override Type ContainerType => typeof(TMinigame);

        // Construction only: a reference cannot be resolved synchronously, so everything
        // content-shaped happens in MinigameContainer.BeginAsync.
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

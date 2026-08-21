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
        // The id the game shell asks for. It is authored on the asset rather than derived from the
        // container type, which is what lets the shell start a minigame without referencing the
        // assembly that defines it. Serialized fields on an abstract ScriptableObject base do
        // serialize, so every concrete definition asset carries this slot.
        [SerializeField] private string _id;

        // The label every asset this minigame owns carries, and how its content is meant to arrive.
        // Authored here because the descriptor is the only thing that both names a minigame and is
        // cheap to hold: it is what makes "which content belongs to this minigame" answerable
        // without loading any of it.
        [SerializeField] private string _contentLabel;
        [SerializeField] private MinigameLoadPolicy _loadPolicy;

        public string Id => _id;
        public string ContentLabel => _contentLabel;
        public MinigameLoadPolicy LoadPolicy => _loadPolicy;

        public abstract Type ContainerType { get; }
        public abstract MinigameContainer GetMinigameContainer();

        // The one hook a concrete minigame has for handing its controller whatever only it knows
        // about, its own config document being the reason this exists. It runs from the container's
        // BeginAsync, before the controller is injected, so a controller can build state from its
        // own content and still be injected on top of it. A minigame needing nothing overrides
        // nothing.
        //
        // Asynchronous because that content is behind an AssetReference now: the descriptor names
        // it rather than holding it, so it is not there until something asks.
        public virtual UniTask ConfigureControllerAsync(
            MinigameControllerBase controller, IAssetProvider assets, CancellationToken ct) => UniTask.CompletedTask;

        // The other half of the hook. Whatever ConfigureControllerAsync loaded is dropped here, so
        // the container's End stays synchronous: it releases asset handles, never instances.
        public virtual void ReleaseContent(IAssetProvider assets) { }
    }

    public abstract class MinigameBase<TController, TView, TMinigame> : MinigameBaseSO
    where TController : MinigameControllerBase, new()
    where TView : MinigameViewBase
    where TMinigame : MinigameContainer, new()
    {
        // A reference, not the prefab. An AssetReference serializes as a GUID string rather than as
        // an object reference, so this asset names the view without depending on it, and loading
        // the descriptor no longer drags the minigame's whole bundle in behind it. That indirection
        // is the entire mechanism; a direct field would undo it silently.
        [SerializeField] private AssetReferenceGameObject _viewRef;

        public AssetReferenceGameObject ViewRef => _viewRef;

        public override Type ContainerType => typeof(TMinigame);

        // Construction only: nothing here loads, because a reference cannot be resolved
        // synchronously. Everything content-shaped happens together in MinigameContainer.BeginAsync.
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

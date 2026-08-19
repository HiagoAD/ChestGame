using System;
using UnityEngine;

namespace Company.ChestGame.Minigame.Core
{
    public abstract class MinigameBaseSO : ScriptableObject
    {
        // The id the game shell asks for. It is authored on the asset rather than derived from the
        // container type, which is what lets the shell start a minigame without referencing the
        // assembly that defines it. Serialized fields on an abstract ScriptableObject base do
        // serialize, so every concrete definition asset carries this slot.
        [SerializeField] private string _id;

        public string Id => _id;

        public abstract Type ContainerType { get; }
        public abstract MinigameContainer GetMinigameContainer();
    }

    public abstract class MinigameBase<TController, TView, TMinigame> : MinigameBaseSO
    where TController : MinigameControllerBase, new()
    where TView : MinigameViewBase
    where TMinigame : MinigameContainer, new()
    {
        [SerializeField] private TView _viewRef;

        public TView ViewRef => _viewRef;

        public override Type ContainerType => typeof(TMinigame);

        public override MinigameContainer GetMinigameContainer()
        {
            TMinigame minigame = new();

            TController controller = new();
            ConfigureController(controller);

            minigame.Set(controller, _viewRef);
            return minigame;
        }

        // The one hook a concrete minigame has for handing its controller whatever only it knows
        // about, its own config document being the reason this exists. It deliberately runs before
        // the controller is handed over, so a controller can build state from it and still be
        // injected afterwards by MinigameManager.Get. A minigame needing nothing overrides nothing.
        protected virtual void ConfigureController(TController controller) { }
    }
}

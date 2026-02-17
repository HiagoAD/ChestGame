using System;
using UnityEngine;

namespace Company.ChestGame.Minigame.Core
{
    public abstract class MinigameBaseSO : ScriptableObject
    {
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
            minigame.Set(new TController(), _viewRef);
            return minigame;
        }
    }
}
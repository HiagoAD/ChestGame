using UnityEngine;

namespace Company.ChestGame.Minigame.Core
{
    public abstract class MinigameViewBase : MonoBehaviour
    {
        public abstract void SetController(MinigameControllerBase controller);
    }
}
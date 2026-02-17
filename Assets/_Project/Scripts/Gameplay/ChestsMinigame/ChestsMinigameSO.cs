using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Minigame.Core;
using UnityEngine;

namespace Company.ChestGame.Minigame.Chests
{
    public class ChestsMinigame : MinigameContainer { }

    
    [CreateAssetMenu(fileName = "ChestsMinigame", menuName = "Minigames/Chests")]
    public class ChestsMinigameSO : MinigameBase<ChestsMinigameController, ChestsMinigameView, ChestsMinigame>
    {

    }
}
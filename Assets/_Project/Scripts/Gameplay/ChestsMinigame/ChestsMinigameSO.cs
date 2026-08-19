using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Minigame.Core;
using UnityEngine;

namespace Company.ChestGame.Minigame.Chests
{
    public class ChestsMinigame : MinigameContainer { }

    
    [CreateAssetMenu(fileName = "ChestsMinigame", menuName = "Minigames/Chests")]
    public class ChestsMinigameSO : MinigameBase<ChestsMinigameController, ChestsMinigameView, ChestsMinigame>
    {
        // The minigame carries its own config document rather than reading fields off a shared one.
        // Nothing outside this folder knows the document exists, or what is in it.
        [SerializeField] private TextAsset _configDocument;

        protected override void ConfigureController(ChestsMinigameController controller)
        {
            // An empty inspector slot would otherwise surface as an UnassignedReferenceException
            // from reading .text, which is neither typed nor traceable back to this asset. The
            // check uses Unity's overloaded equality, so it catches a destroyed asset too.
            if (_configDocument == null)
            {
                throw new GameConfigException(
                    $"The '{name}' minigame definition has no config document assigned, wire _configDocument on the asset");
            }

            controller.Configure(ChestsMinigameConfig.Parse(_configDocument.text));
        }
    }
}

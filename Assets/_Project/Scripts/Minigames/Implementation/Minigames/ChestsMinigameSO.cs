using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Chests.Internal;
using Company.ChestGame.Minigame.Core;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace Company.ChestGame.Minigame.Chests
{
    public class ChestsMinigame : MinigameContainer { }

    
    [CreateAssetMenu(fileName = "ChestsMinigame", menuName = "Minigames/Chests")]
    public class ChestsMinigameSO : MinigameBase<ChestsMinigameController, ChestsMinigameView, ChestsMinigame>
    {
        // Its own document, not fields off a shared config, and a reference rather than the
        // TextAsset itself: a direct field would make this descriptor depend on the chests bundle.
        [SerializeField] private AssetReferenceT<TextAsset> _configDocument;

        protected override async UniTask ConfigureControllerAsync(
            ChestsMinigameController controller, IAssetProvider assets, CancellationToken ct)
        {
            // Checked before the load: an empty slot would otherwise surface as a
            // MissingAssetException naming an empty GUID, traceable back to nothing.
            if (_configDocument == null || !_configDocument.RuntimeKeyIsValid())
            {
                throw new GameConfigException(
                    $"The '{name}' minigame definition has no config document assigned, wire _configDocument on the asset");
            }

            TextAsset document = await assets.LoadAsync<TextAsset>(_configDocument, ct);

            controller.Configure(ChestsMinigameConfig.Parse(document.text));
        }

        // Only needed to build the controller's state, so nothing holds it past teardown.
        public override void ReleaseContent(IAssetProvider assets) => assets.Release(_configDocument);
    }
}

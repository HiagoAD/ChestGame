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
        // The minigame carries its own config document rather than reading fields off a shared one.
        // Nothing outside this folder knows the document exists, or what is in it.
        //
        // A reference rather than the TextAsset itself, for the same reason the view is one: a
        // direct field would make this descriptor depend on the chests bundle, and the point of
        // grouping that content was that it only ships when the minigame is actually asked for.
        [SerializeField] private AssetReferenceT<TextAsset> _configDocument;

        protected override async UniTask ConfigureControllerAsync(
            ChestsMinigameController controller, IAssetProvider assets, CancellationToken ct)
        {
            // An empty inspector slot would otherwise surface as a MissingAssetException naming an
            // empty GUID, which is neither traceable back to this asset nor the failure a reader is
            // looking for. Checked before the load rather than after it, because the provider would
            // report the same emptiness without knowing whose it is.
            if (_configDocument == null || !_configDocument.RuntimeKeyIsValid())
            {
                throw new GameConfigException(
                    $"The '{name}' minigame definition has no config document assigned, wire _configDocument on the asset");
            }

            TextAsset document = await assets.LoadAsync<TextAsset>(_configDocument, ct);

            controller.Configure(ChestsMinigameConfig.Parse(document.text));
        }

        // The document is only needed to build the controller's state, so nothing has to hold it
        // past teardown.
        public override void ReleaseContent(IAssetProvider assets) => assets.Release(_configDocument);
    }
}

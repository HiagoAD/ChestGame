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
        /// <summary>
        /// Its own document, not fields off a shared config, and a reference rather than the
        /// TextAsset itself.
        /// </summary>
        /// <remarks>
        /// See docs/minigames.md, "A definition names its content, it does not hold it".
        /// </remarks>
        [SerializeField] private AssetReferenceT<TextAsset> _configDocument;

        /// <exception cref="GameConfigException">
        /// <c>_configDocument</c> is unassigned, or names a document that is not valid JSON for
        /// <see cref="ChestsMinigameConfig"/>.
        /// </exception>
        /// <exception cref="AssetLoadException">The reference resolved but the load itself failed.</exception>
        /// <exception cref="MissingAssetException">The reference is unwired or unresolvable.</exception>
        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        protected override async UniTask ConfigureControllerAsync(
            ChestsMinigameController controller, IAssetProvider assets, CancellationToken ct)
        {
            if (_configDocument == null || !_configDocument.RuntimeKeyIsValid())
            {
                throw new GameConfigException(
                    $"The '{name}' minigame definition has no config document assigned, wire _configDocument on the asset");
            }

            TextAsset document = await assets.LoadAsync<TextAsset>(_configDocument, ct);

            controller.Configure(ChestsMinigameConfig.Parse(document.text));
        }

        /// <remarks>
        /// See docs/minigames.md, "Its own config document".
        /// </remarks>
        public override void ReleaseContent(IAssetProvider assets) => assets.Release(_configDocument);
    }
}

using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Minigame.Internal
{
    /// <summary>
    /// Fetches the authored minigame list through the asset provider, and the only place that
    /// knows the key.
    /// </summary>
    public class AddressablesMinigameListSource : IMinigameListSource
    {
        private const string LIST_KEY = "Minigames/MinigameList";

        private readonly IAssetProvider _assets;

        public AddressablesMinigameListSource(IAssetProvider assets) => _assets = assets;

        /// <summary>
        /// Loads the authored minigame list.
        /// </summary>
        /// <returns>The list's entries.</returns>
        /// <exception cref="MissingAssetException">
        /// The key is not in the shipped catalog, or the loaded asset resolves to null.
        /// </exception>
        /// <exception cref="AssetLoadException">The key resolved but the load itself failed.</exception>
        public async UniTask<IReadOnlyList<MinigameBaseSO>> ReadAsync(CancellationToken ct)
        {
            MinigameListSO minigameListSO = await _assets.LoadAsync<MinigameListSO>(LIST_KEY, ct);
            if (minigameListSO == null)
            {
                throw new MissingAssetException(LIST_KEY, "Minigame list");
            }

            return minigameListSO.Entries;
        }
    }
}

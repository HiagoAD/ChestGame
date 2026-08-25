using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Assets;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Minigame.Internal
{
    // Fetches the authored minigame list through the asset provider, and the only place that knows
    // the key.
    public class AddressablesMinigameListSource : IMinigameListSource
    {
        private const string LIST_KEY = "Minigames/MinigameList";

        private readonly IAssetProvider _assets;

        public AddressablesMinigameListSource(IAssetProvider assets) => _assets = assets;

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

using System.Collections.Generic;
using System.Threading;
using Company.ChestGame.Config;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Popups;
using Company.ChestGame.Popups.Internal;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Core
{
    /// <summary>
    /// Pulls every piece of content the game needs before its services exist.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Boot".
    /// </remarks>
    public class GameContentLoader
    {
        private readonly IGameConfigSource _configSource;
        private readonly IMinigameListSource _minigameListSource;
        private readonly IPopupListSource _popupListSource;
        private readonly IPopupParentSource _popupParentSource;

        public GameContentLoader(
            IGameConfigSource configSource,
            IMinigameListSource minigameListSource,
            IPopupListSource popupListSource,
            IPopupParentSource popupParentSource)
        {
            _configSource = configSource;
            _minigameListSource = minigameListSource;
            _popupListSource = popupListSource;
            _popupParentSource = popupParentSource;
        }

        /// <summary>
        /// Reads every content source and returns the combined result.
        /// </summary>
        /// <param name="ct">Cancellation token observed by each source read.</param>
        /// <returns>The loaded content once every source has resolved.</returns>
        /// <exception cref="Company.ChestGame.Common.AssetLoadException">
        /// One of the four sources' key resolved but the load itself failed.
        /// </exception>
        /// <exception cref="Company.ChestGame.Common.MissingAssetException">
        /// One of the four sources' key is not in the shipped catalog.
        /// </exception>
        /// <remarks>
        /// Reads sequentially, not in parallel.
        /// See docs/architecture.md, "Boot".
        /// </remarks>
        public async UniTask<LoadedContent> LoadAsync(CancellationToken ct)
        {
            string gameConfigDocument = await _configSource.ReadAsync(ct);
            IReadOnlyList<MinigameBaseSO> minigames = await _minigameListSource.ReadAsync(ct);
            IReadOnlyList<PopupBase> popups = await _popupListSource.ReadAsync(ct);
            PopupParent popupParentPrefab = await _popupParentSource.ReadAsync(ct);

            return new LoadedContent(gameConfigDocument, minigames, popups, popupParentPrefab);
        }
    }
}

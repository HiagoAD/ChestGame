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
    // Pulls every piece of content the game needs before its services exist. A plain class with no
    // scene, scope or MonoBehaviour in it, which is what keeps the untestable part of booting down
    // to the three lines in the bootstrapper.
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

        // Sequential rather than parallel: nothing here is slow enough for the difference to
        // matter, and a failure names the source that caused it.
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

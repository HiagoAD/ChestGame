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
    // Pulls every piece of content the game needs before its services exist.
    //
    // Deliberately a plain class with no scene, scope or MonoBehaviour anywhere in it. The part of
    // booting that cannot be tested — a scene load and a container being built from Awake — is the
    // bootstrapper, and it is kept to three lines by everything real living here instead.
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

        // Read sequentially rather than in parallel. Nothing here is slow enough for the difference
        // to matter yet, and one at a time means a failure names the source that caused it instead
        // of whichever of four raced to the exception first.
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

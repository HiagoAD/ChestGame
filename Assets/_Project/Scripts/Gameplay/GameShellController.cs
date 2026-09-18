using System;
using System.Threading;
using Company.ChestGame.Common;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Mvc;
using Company.ChestGame.Popups;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Gameplay
{
    /// <summary>
    /// The game shell's rules. It knows no minigame by type: it holds an authored id, asks the
    /// manager for whatever is registered under it, and drives it through the framework's own
    /// surface, so this assembly references no minigame's assembly.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md. See docs/architecture.md, "Entry point and game flow".
    /// </remarks>
    public class GameShellController : IController
    {
        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private const string CONTENT_UNAVAILABLE_MESSAGE =
            "Could not download this minigame. Check your connection and try again.";

        private readonly IMinigameManager _minigames;
        private readonly IPopupManager _popups;

        private MinigameContainer _activeMinigame;

        /// <remarks>
        /// Tracked separately: the shell only ever sees the base container type back.
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private string _activeMinigameId;

        /// <remarks>
        /// A second call mid-start would build a second container and orphan the first.
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private bool _starting;

        /// <summary>Raised with <c>true</c> when a start begins and <c>false</c> when it settles.</summary>
        public event Action<bool> OnBusyChanged;

        public GameShellController(IMinigameManager minigames, IPopupManager popups)
        {
            _minigames = minigames;
            _popups = popups;
        }

        /// <param name="id">The authored id of the minigame to start.</param>
        /// <param name="parent">
        /// Forwarded to <see cref="MinigameContainer.BeginAsync"/> unread; the container demands a
        /// parent transform for the view it instantiates.
        /// </param>
        /// <param name="ct">Cancellation for the start.</param>
        /// <remarks>
        /// See docs/mvc.md. See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        public async UniTask StartAsync(string id, Transform parent, CancellationToken ct)
        {
            if (_starting) return;

            _starting = true;
            OnBusyChanged?.Invoke(true);
            try
            {
                if (_activeMinigameId != id || _activeMinigame == null || !_activeMinigame.Running)
                {
                    EndActiveMinigame();

                    MinigameContainer starting = _minigames.Get(id);
                    await starting.BeginAsync(parent, ct);

                    _activeMinigame = starting;
                    _activeMinigameId = id;
                }

                _activeMinigame.ControllerInstance.NewGame();
            }
            catch (ChestGameException failure)
            {
                Debug.LogException(failure);
                _popups.Spawn<ContentUnavailablePopup, ContentUnavailablePopupData>(
                    new ContentUnavailablePopupData(CONTENT_UNAVAILABLE_MESSAGE));
            }
            finally
            {
                _starting = false;
                OnBusyChanged?.Invoke(false);
            }
        }

        /// <summary>Ends the active minigame, if any. Safe to call when none is active.</summary>
        public void EndActiveMinigame()
        {
            if (_activeMinigame == null) return;

            _activeMinigame.End();
            _activeMinigame = null;
            _activeMinigameId = null;
        }

        public void Dispose()
        {
            EndActiveMinigame();
            OnBusyChanged = null;
        }
    }
}

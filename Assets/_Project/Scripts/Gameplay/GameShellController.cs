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

        /// <summary>
        /// Cancelled, and never disposed, by <see cref="Dispose"/>. Every start's token is linked
        /// to it.
        /// </summary>
        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private readonly CancellationTokenSource _lifetime = new();

        private MinigameContainer _activeMinigame;

        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private string _activeMinigameId;

        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private bool _starting;

        /// <summary>Set once by <see cref="Dispose"/> and never cleared.</summary>
        private bool _disposed;

        /// <summary>Raised with <c>true</c> when a start begins and <c>false</c> when it settles.</summary>
        public event Action<bool> OnBusyChanged;

        public GameShellController(IMinigameManager minigames, IPopupManager popups)
        {
            _minigames = minigames;
            _popups = popups;
        }

        /// <summary>
        /// Starts the minigame registered under <paramref name="id"/>, or restarts it when it is
        /// already running. Returns without doing anything when the controller is disposed or a
        /// start is already in flight. A content failure (a <see cref="ChestGameException"/>) is
        /// logged and shown to the player as a <see cref="ContentUnavailablePopup"/> unless the
        /// controller is disposed by then, and never escapes.
        /// </summary>
        /// <param name="id">The authored id of the minigame to start.</param>
        /// <param name="parent">
        /// Forwarded to <see cref="MinigameContainer.BeginAsync"/> unread; the container demands a
        /// parent transform for the view it instantiates.
        /// </param>
        /// <param name="ct">
        /// Cancellation for the start. Disposing the controller cancels the start as well.
        /// </param>
        /// <exception cref="OperationCanceledException">
        /// The start had to begin a container, and <paramref name="ct"/> was cancelled or the
        /// controller was disposed before it finished, including after the container had already
        /// begun, in which case the container is ended rather than kept and
        /// <see cref="MinigameControllerBase.NewGame"/> is not called. A restart of the minigame
        /// already running completes synchronously, does not observe the token and never throws
        /// this.
        /// </exception>
        /// <remarks>
        /// See docs/mvc.md. See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        public async UniTask StartAsync(string id, Transform parent, CancellationToken ct)
        {
            if (_disposed || _starting) return;

            _starting = true;
            OnBusyChanged?.Invoke(true);
            try
            {
                using CancellationTokenSource linked =
                    CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
                CancellationToken token = linked.Token;

                if (_activeMinigameId != id || _activeMinigame == null || !_activeMinigame.Running)
                {
                    EndActiveMinigame();

                    MinigameContainer starting = _minigames.Get(id);
                    await starting.BeginAsync(parent, token);

                    if (token.IsCancellationRequested)
                    {
                        starting.End();
                        token.ThrowIfCancellationRequested();
                    }

                    _activeMinigame = starting;
                    _activeMinigameId = id;
                }

                _activeMinigame.ControllerInstance.NewGame();
            }
            catch (ChestGameException failure)
            {
                Debug.LogException(failure);

                if (!_disposed)
                {
                    _popups.Spawn<ContentUnavailablePopup, ContentUnavailablePopupData>(
                        new ContentUnavailablePopupData(CONTENT_UNAVAILABLE_MESSAGE));
                }
            }
            finally
            {
                _starting = false;
                OnBusyChanged?.Invoke(false);
            }
        }

        /// <summary>Ends the active minigame, if any. Safe to call when none is active.</summary>
        private void EndActiveMinigame()
        {
            if (_activeMinigame == null) return;

            _activeMinigame.End();
            _activeMinigame = null;
            _activeMinigameId = null;
        }

        /// <summary>
        /// Cancels any start in flight, ends the active minigame and drops every
        /// <see cref="OnBusyChanged"/> subscriber, in that order. A start cancelled by this may
        /// raise <see cref="OnBusyChanged"/> with <c>false</c> before it returns. Afterwards
        /// <see cref="StartAsync"/> does nothing. Safe to call more than once.
        /// </summary>
        /// <exception cref="AggregateException">
        /// A cancellation callback registered on a start's token threw. The minigame is still ended
        /// and the subscribers still dropped before it propagates.
        /// </exception>
        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        public void Dispose()
        {
            _disposed = true;

            try
            {
                _lifetime.Cancel();
            }
            finally
            {
                EndActiveMinigame();
                OnBusyChanged = null;
            }
        }
    }
}

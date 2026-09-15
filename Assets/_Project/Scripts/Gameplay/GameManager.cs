using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

using Company.ChestGame.Common;
using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Minigame;
using Company.ChestGame.Popups;

namespace Company.ChestGame.Gameplay
{
    /// <summary>
    /// The game shell. It knows no minigame by type: it holds an authored id, asks the manager for
    /// whatever is registered under it, and drives it through the framework's own surface, so this
    /// assembly references no minigame's assembly.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Entry point and game flow".
    /// </remarks>
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private Transform _minigamesParent;
        [SerializeField] private Button _startButton;

        /// <summary>
        /// Authored in the scene; the initializer is only a default for a freshly added component.
        /// </summary>
        [SerializeField] private string _minigameId = "chests";

        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private const string CONTENT_UNAVAILABLE_MESSAGE =
            "Could not download this minigame. Check your connection and try again.";

        private MinigameContainer _activeMinigame;

        /// <remarks>
        /// Tracked separately: the shell only ever sees the base container type back.
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private string _activeMinigameId;

        private IMinigameManager _minigamesManager;
        private IPopupManager _popups;

        /// <remarks>
        /// A second press mid-start would build a second container and orphan the first.
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private bool _starting;


        [Inject]
        private void Inject(IMinigameManager minigamesManager, IPopupManager popups)
        {
            _minigamesManager = minigamesManager;
            _popups = popups;
        }

        private void Awake()
        {
            _startButton.onClick.AddListener(StartConfiguredMinigame);
        }

        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private void OnDestroy()
        {
            _startButton.onClick.RemoveListener(StartConfiguredMinigame);
            EndActiveMinigame();
        }

        private void StartConfiguredMinigame() => StartMinigame(_minigameId).Forget();

        /// <param name="id">The authored id of the minigame to start.</param>
        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private async UniTaskVoid StartMinigame(string id)
        {
            if (_starting) return;

            _starting = true;
            SetStartButtonInteractable(false);
            try
            {
                if (_activeMinigameId != id || _activeMinigame == null || !_activeMinigame.Running)
                {
                    EndActiveMinigame();

                    MinigameContainer starting = _minigamesManager.Get(id);
                    await starting.BeginAsync(_minigamesParent, this.GetCancellationTokenOnDestroy());

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
                SetStartButtonInteractable(true);
            }
        }

        /// <remarks>
        /// See docs/architecture.md, "Entry point and game flow".
        /// </remarks>
        private void SetStartButtonInteractable(bool interactable)
        {
            if (_startButton == null) return;

            _startButton.interactable = interactable;
        }

        private void EndActiveMinigame()
        {
            if (_activeMinigame == null) return;

            _activeMinigame.End();
            _activeMinigame = null;
            _activeMinigameId = null;
        }
    }
}

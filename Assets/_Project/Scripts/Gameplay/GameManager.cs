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
    // The game shell. It knows no minigame by type: it holds an authored id, asks the manager for
    // whatever is registered under it, and drives it through the framework's own surface, so this
    // assembly references no minigame's assembly.
    public class GameManager : MonoBehaviour
    {
        [SerializeField] private Transform _minigamesParent;
        [SerializeField] private Button _startButton;

        // Authored in the scene; the initializer is only a default for a freshly added component.
        [SerializeField] private string _minigameId = "chests";

        // Deliberately not the exception's own message, which names keys and labels.
        private const string CONTENT_UNAVAILABLE_MESSAGE =
            "Could not download this minigame. Check your connection and try again.";

        private MinigameContainer _activeMinigame;

        // Tracked separately: the shell only ever sees the base container type back.
        private string _activeMinigameId;

        private IMinigameManager _minigamesManager;
        private IPopupManager _popups;

        // A second press mid-start would build a second container and orphan the first.
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

        // Whatever is running is torn down with this scene, so the controller disposes and the view
        // is destroyed rather than left to the GC with live subscriptions.
        private void OnDestroy()
        {
            _startButton.onClick.RemoveListener(StartConfiguredMinigame);
            EndActiveMinigame();
        }

        private void StartConfiguredMinigame() => StartMinigame(_minigameId).Forget();

        // A different minigame, or one that has been torn down, builds a fresh container; asking
        // again for the one already running just restarts it. The token is this object's, so a
        // scene change mid-load unwinds the start instead of finishing into a destroyed shell.
        private async UniTaskVoid StartMinigame(string id)
        {
            if (_starting) return;

            // A start that goes to the network can take long enough for a player to conclude the
            // button is broken, so the button says so rather than swallowing presses behind a flag.
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
                // Caught at the project's own base type on purpose: every delivery failure reads
                // identically to whoever is holding the phone, and anything not under that base is
                // a bug and is left to blow up where it can be seen.
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

        // The button is gone by the time a start cancelled by this object's destruction unwinds,
        // which is the ordinary shutdown path rather than an error.
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

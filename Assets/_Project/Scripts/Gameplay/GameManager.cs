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
    // The main class that controls the game.
    //
    // The shell deliberately knows no minigame by type. It holds an authored id, asks the manager
    // for whatever is registered under it, and drives it through the framework's own surface, so
    // this assembly references no minigame's assembly and a minigame can be added or removed
    // without touching the shell.
    //
    // The chests minigame it currently starts is where the async work lives: two concurrent tasks
    // running in parallel, one updating the slider inside the chest every frame and the other
    // waiting on the timer, both under one cancellation token so they stay in sync.
    //
    // This minigame doesn't have persistence. At each new game, the amount of attemps is reset.
    // The currencies are persisted, even between sessions.

    public class GameManager : MonoBehaviour
    {
        [SerializeField] private Transform _minigamesParent;
        [SerializeField] private Button _startButton;

        // Authored in the scene. The initializer is a default for a freshly added component, not
        // the value the shipped scene relies on.
        [SerializeField] private string _minigameId = "chests";

        // What the player is shown when a minigame's content did not arrive. Deliberately not the
        // exception's own message, which names keys and labels the player has no use for.
        private const string CONTENT_UNAVAILABLE_MESSAGE =
            "Could not download this minigame. Check your connection and try again.";

        private MinigameContainer _activeMinigame;

        // Tracked alongside the container because the container's type no longer identifies which
        // minigame it is: the shell only ever sees the base type back from the manager.
        private string _activeMinigameId;

        private IMinigameManager _minigamesManager;
        private IPopupManager _popups;

        // Starting is asynchronous now, so a second press while the first start is still in flight
        // would build a second container and leave the first one running with nothing holding it.
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

        // Closing the loop the framework opens: whatever is running gets torn down when this scene
        // goes away, so the controller disposes and the view is destroyed instead of being left to
        // the garbage collector with its subscriptions still live.
        private void OnDestroy()
        {
            _startButton.onClick.RemoveListener(StartConfiguredMinigame);
            EndActiveMinigame();
        }

        private void StartConfiguredMinigame() => StartMinigame(_minigameId).Forget();

        // Starting a different minigame, or restarting one that has been torn down, builds a fresh
        // container; asking again for the one already running just restarts it.
        //
        // A minigame's content is named by its definition rather than held by it, so beginning one
        // is where it actually gets fetched. The token is this object's, so a scene change mid-load
        // unwinds the start instead of finishing into a destroyed shell.
        private async UniTaskVoid StartMinigame(string id)
        {
            if (_starting) return;

            // A start that goes to the network can take long enough for a player to conclude the
            // button is broken, so the button says so itself rather than silently swallowing
            // presses behind the flag.
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
                // Content that did not arrive is the one failure the player can do something about
                // — wait and press again — so it is told rather than logged and forgotten. Caught
                // at the project's own base type on purpose: a missing key and a broken download
                // arrive as different types and read identically to whoever is holding the phone,
                // and anything not under that base is a bug rather than a delivery problem and is
                // left to blow up where it can be seen.
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

        // The button is gone by the time a start cancelled by this object's own destruction unwinds,
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

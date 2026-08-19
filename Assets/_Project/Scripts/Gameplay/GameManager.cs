using UnityEngine;
using UnityEngine.UI;
using VContainer;

using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Minigame;

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

        private MinigameContainer _activeMinigame;

        // Tracked alongside the container because the container's type no longer identifies which
        // minigame it is: the shell only ever sees the base type back from the manager.
        private string _activeMinigameId;

        private IMinigameManager _minigamesManager;


        [Inject]
        private void Inject(IMinigameManager minigamesManager)
        {
            _minigamesManager = minigamesManager;
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

        private void StartConfiguredMinigame() => StartMinigame(_minigameId);

        // Starting a different minigame, or restarting one that has been torn down, builds a fresh
        // container; asking again for the one already running just restarts it.
        private void StartMinigame(string id)
        {
            if (_activeMinigameId != id || _activeMinigame == null || !_activeMinigame.Running)
            {
                EndActiveMinigame();

                _activeMinigame = _minigamesManager.Get(id);
                _activeMinigameId = id;
                _activeMinigame.Begin(_minigamesParent);
            }

            _activeMinigame.ControllerInstance.NewGame();
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

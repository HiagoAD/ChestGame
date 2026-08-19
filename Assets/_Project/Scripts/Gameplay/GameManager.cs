using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

using Company.ChestGame.Minigame.Core;
using Company.ChestGame.Minigame;
using Company.ChestGame.Minigame.Chests;

namespace Company.ChestGame.Gameplay
{
    // The main class that controls the game.
    //
    // The key area here is to control async the chest state. To display this control better,
    // a weird approach was taken, where two concurrent async tasks run in parallel, one
    // updating every frame the slider inside the chest, and the other waiting until the
    // timer finishes to open the chest, with both being controlled by the same cancellation
    // token to ensure that they behave in sync with each other.
    //
    // This minigame doesn't have persistence. At each new game, the amount of attemps is reset.
    // The currencies are persisted, even between sessions.

    public class GameManager : MonoBehaviour
    {
        [SerializeField] private Transform _minigamesParent;
        [SerializeField] private Button _startButton;

        private MinigameContainer _activeMinigame;
        private IMinigameManager _minigamesManager;


        [Inject]
        private void Inject(IMinigameManager minigamesManager)
        {
            _minigamesManager = minigamesManager;
        }

        private void Awake()
        {
            _startButton.onClick.AddListener(NewChestsMinigame);
        }

        // Closing the loop the framework opens: whatever is running gets torn down when this scene
        // goes away, so the controller disposes and the view is destroyed instead of being left to
        // the garbage collector with its subscriptions still live.
        private void OnDestroy()
        {
            _startButton.onClick.RemoveListener(NewChestsMinigame);
            EndActiveMinigame();
        }

        private void NewChestsMinigame() => StartMinigame<ChestsMinigame>();

        // Starting a minigame of a different type, or restarting one that has been torn down,
        // builds a fresh container; asking again for the one already running just restarts it.
        private void StartMinigame<TMinigame>() where TMinigame : MinigameContainer
        {
            if (_activeMinigame is not TMinigame || !_activeMinigame.Running)
            {
                EndActiveMinigame();

                _activeMinigame = _minigamesManager.Get<TMinigame>();
                _activeMinigame.Begin(_minigamesParent);
            }

            _activeMinigame.ControllerInstance.NewGame();
        }

        private void EndActiveMinigame()
        {
            if (_activeMinigame == null) return;

            _activeMinigame.End();
            _activeMinigame = null;
        }
    }
}

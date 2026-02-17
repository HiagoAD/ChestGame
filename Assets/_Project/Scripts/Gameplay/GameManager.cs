using System;
using System.Collections.Generic;
using Company.ChestGame.Config;
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


        // private ChestsMinigameController _chestsMinigameInstance;


        [Inject]
        private void Inject(IMinigameManager minigamesManager)
        {
            _minigamesManager = minigamesManager;
        }

        private void Awake()
        {
            _startButton.onClick.AddListener(NewChestsMinigame);
        }

 
        private void NewChestsMinigame()
        {
            if(_activeMinigame == null || !_activeMinigame.Running)
            {
                _activeMinigame = _minigamesManager.Get<ChestsMinigame>();
                _activeMinigame.Begin(_minigamesParent);
            }

            _activeMinigame.ControllerInstance.NewGame();

        }
    }
}

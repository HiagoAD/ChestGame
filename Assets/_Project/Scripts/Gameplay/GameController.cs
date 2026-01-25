using System;
using System.Collections.Generic;
using Company.ChestGame.Config;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

using Company.ChestGame.Rewards;
using Company.ChestGame.Gameplay.ChestsMinigame;

namespace Company.ChestGame.Gameplay
{
    // The main class of the game, controls everything, and should be split on a proper game,
    // with the logic of the minigame being handled somewhere else, and only instantiating 
    // the minigame prefab. But as this project is so simple, doing this way avoids boilerplates.
    // 
    // The key area here is to control async the chest state. To display this control better,
    // a weird approach was taken, where two concurrent async tasks run in parallel, one
    // updating every frame the slider inside the chest, and the other waiting until the
    // timer finishes to open the chest, with both being controlled by the same cancellation
    // token to ensure that they behave in sync with each other.
    //
    // This game doesn't have persistence. At each new game, a the amount of attemps is reset.

    public class GameController : MonoBehaviour
    {
        [SerializeField] private ChestsMinigameController _chestsMinigamePrefab;
        [SerializeField] private Transform _chestsMinigameParent;
        [SerializeField] private Button _startButton;

        private IGameConfig _gameConfig;
        private IRewardsManager _rewardsManager;


        private ChestsMinigameController _chestsMinigameInstance;


        [Inject]
        private void Inject(IGameConfig gameConfig, IRewardsManager rewardsManager)
        {
            _gameConfig = gameConfig;
            _rewardsManager = rewardsManager;
        }

        private void Awake()
        {
            _startButton.onClick.AddListener(NewChestsMinigame);
        }

 
        private void NewChestsMinigame()
        {
            if (_chestsMinigameInstance == null)
            {
                _chestsMinigameInstance = Instantiate(_chestsMinigamePrefab, _chestsMinigameParent);
                _chestsMinigameInstance.Set(_gameConfig.ChestCount, _gameConfig.TimeToOpenChestMiliseconds, _gameConfig.AttempsCount, OnChestsMinigameFinished);
            }

            _chestsMinigameInstance.NewGame();
        }

        private void OnChestsMinigameFinished(bool won)
        {
            if (won)
            {
                _rewardsManager.GiveRandomCurrencyReward("ChestsMinigame");
            }
        }
    }
}

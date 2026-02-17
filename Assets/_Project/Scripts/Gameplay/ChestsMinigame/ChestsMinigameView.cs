using System.Collections.Generic;
using Company.ChestGame.Minigame.Core;
using TMPro;
using UnityEngine;

namespace Company.ChestGame.Minigame.Chests.Internal
{
    public class ChestsMinigameView : MinigameViewBase
    {
        [SerializeField] private ChestsMinigameChestElementView _chestPrefab;
        [SerializeField] private Transform _chestsParent;
        [SerializeField] private TextMeshProUGUI _attemptsText;
        [SerializeField] private TextMeshProUGUI _controlMessage;


        private List<ChestsMinigameChestElementView> _chestInstances;
        private ChestsMinigameController _controller;

        private void Awake()
        {
            UpdateAttemptsText(true);
            SetControlMessage(null);
        }

        public override void SetController(MinigameControllerBase controller)
        {
            Debug.Assert(controller is ChestsMinigameController, $"Wrong controller type, ChestsMinigameController expected, got {controller.GetType()} instead");

            _controller = (ChestsMinigameController)controller;
            _controller.OnStateChange += OnControllerStateChanged;
            _controller.OnGameFinished += OnGameFinished;
            _controller.OnAttemptsChanged += UpdateAttemptsText;
        }

        private void OnControllerStateChanged(ChestsMinigameController.State state)
        {
            if (state == ChestsMinigameController.State.Playing)
            {
                StartGame();
            }
        }

        private void StartGame()
        {
            if (_chestInstances == null)
            {
                _chestInstances = new();

                foreach (ChestsMinigameChestModel chest in _controller.Chests)
                {
                    ChestsMinigameChestElementView instance = Instantiate(_chestPrefab, _chestsParent);
                    instance.Init(chest, _controller.OnChestClicked);
                    _chestInstances.Add(instance);
                }
            }
            UpdateAttemptsText();
            SetControlMessage(null);
        }

        private void OnGameFinished(bool won)
        {
            string message = won ? "You won!" : "Game Over! Out of attempts!";
            SetControlMessage(message);
        }

        private void UpdateAttemptsText(int _) => UpdateAttemptsText();
        private void UpdateAttemptsText(bool empty = false)
        {
            _attemptsText.text = empty ? "" : $"Attempts: {_controller.Attempts} / {_controller.TotalAttempts}";
        }

        private void SetControlMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                _controlMessage.gameObject.SetActive(false);
                return;
            }
            else
            {
                _controlMessage.text = message;
                _controlMessage.gameObject.SetActive(true);
            }
        }
    }
}

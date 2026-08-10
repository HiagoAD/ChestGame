using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Company.ChestGame.Minigame.Chests.Internal
{
    // Simple implementation of the Chest, only controlling the view based on the state,
    // and providing a simple callback for interaction.
    //
    // The Opening state offer a slider to display the amount of time remaining

    public class ChestsMinigameChestElementView : MonoBehaviour
    {

        [Header("Sprites")]
        [SerializeField] private Sprite _closedSprite;
        [SerializeField] private Sprite _openedEmptySprite;
        [SerializeField] private Sprite _openedFullSprite;

        [Header("Objects")]
        [SerializeField] private Image _chestImage;
        [SerializeField] private Slider _timerSlider;
        [SerializeField] private Button _button;

        private Action<ChestsMinigameChestModel> _onClickCallback;
        private ChestsMinigameChestModel _model;


        private void Awake()
        {
            _button.onClick.AddListener(OnClick);

            SetClosed();
        }

        public void Init(ChestsMinigameChestModel model, Action<ChestsMinigameChestModel> callback)
        {
            _model = model;
            _onClickCallback = callback;

            _model.OnStateChanged += OnStateChanged;
            OnStateChanged(_model.CurrentState);
        }

        // The model outlives this view: it belongs to the controller, while this object dies with
        // the minigame's view hierarchy. Without this, a chest tearing down would leave the model
        // holding a handler that drives a destroyed MonoBehaviour on the next state change.
        private void OnDestroy()
        {
            if (_model != null)
            {
                _model.OnStateChanged -= OnStateChanged;
            }

            _button.onClick.RemoveListener(OnClick);
        }



        private void OnStateChanged(ChestsMinigameChestModel.State state)
        {
            switch (state)
            {
                case ChestsMinigameChestModel.State.Closed:
                    SetClosed();
                    break;
                case ChestsMinigameChestModel.State.Opening:
                    SetOpening(_model.Completition);
                    break;
                case ChestsMinigameChestModel.State.Open_Empty:
                    SetOpen_Empty();
                    break;
                case ChestsMinigameChestModel.State.Open_Prize:
                    SetOpen_Prize();
                    break;

            }
        }

        private void SetClosed()
        {
            _chestImage.sprite = _closedSprite;
            _timerSlider.gameObject.SetActive(false);
        }

        public void SetOpening(float completition)
        {
            _timerSlider.value = completition;

            _chestImage.sprite = _closedSprite;
            _timerSlider.gameObject.SetActive(true);
        }

        public void SetOpen_Empty()
        {
            _chestImage.sprite = _openedEmptySprite;
            _timerSlider.gameObject.SetActive(false);
        }

        public void SetOpen_Prize()
        {
            _chestImage.sprite = _openedFullSprite;
            _timerSlider.gameObject.SetActive(false);
        }


        private void OnClick()
        {
            _onClickCallback?.Invoke(_model);
        }
    }
}

using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Company.ChestGame.Gameplay
{
    // Simple implementation of the Chest, only controlling the view based on the state,
    // and providing a simple callback for interaction.
    //
    // The Opening state offer a slider to display the amount of time remaining

    public class Chest : MonoBehaviour
    {
        public enum State
        {
            Closed,
            Opening,
            Open
        }

        [Header("Sprites")]
        [SerializeField] private Sprite _closedSprite;
        [SerializeField] private Sprite _openedEmptySprite;
        [SerializeField] private Sprite _openedFullSprite;

        [Header("Objects")]
        [SerializeField] private Image _chestImage;
        [SerializeField] private Slider _timerSlider;
        [SerializeField] private Button _button;

        public State CurrentState { get; private set; }
        
        private Action<Chest> _onClickCallback;


        private void Awake()
        {
            _button.onClick.AddListener(OnClick);

            SetClosed();
        }

        public void SetClickCallback(Action<Chest> callback)
        {
            _onClickCallback = callback;
        }

        public void SetClosed()
        {
            if (CurrentState == State.Closed) return;

            CurrentState = State.Closed;
            _chestImage.sprite = _closedSprite;
            _timerSlider.gameObject.SetActive(false);
        }

        public void SetOpening(float completition)
        {
            _timerSlider.value = completition;

            if (CurrentState == State.Opening) return;

            CurrentState = State.Opening;
            _chestImage.sprite = _closedSprite;
            _timerSlider.gameObject.SetActive(true);
        }

        public void SetOpen(bool hasPrize)
        {
            if (CurrentState == State.Open) return;

            CurrentState = State.Open;
            _chestImage.sprite = hasPrize ? _openedFullSprite : _openedEmptySprite;
            _timerSlider.gameObject.SetActive(false);
        }

        private void OnClick()
        {
            _onClickCallback?.Invoke(this);
        }
    }
}

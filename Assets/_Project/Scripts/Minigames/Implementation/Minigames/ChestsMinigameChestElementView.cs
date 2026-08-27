using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Company.ChestGame.Minigame.Chests.Internal
{
    // Drives one chest from its model state, with a slider during the opening state.
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

        // Paired with Release, which the caller owes between two Inits. A pool hands the same
        // instance out over and over, and the model it was showing last time has to have been let go
        // by then. Releasing here as well would hide a caller that forgot, and would take the edge
        // off the tests that prove the release path is the one doing the work.
        public void Init(ChestsMinigameChestModel model, Action<ChestsMinigameChestModel> callback)
        {
            _model = model;
            _onClickCallback = callback;

            _model.OnStateChanged += OnStateChanged;
            OnStateChanged(_model.CurrentState);
        }

        // The other half of Init, and what pooling needs that destruction alone did not. A released
        // instance goes on existing - ParkedPool does not even deactivate it - so a subscription
        // left behind would drive a chest this view is no longer showing, and a click would still
        // reach the controller carrying the old model.
        //
        // Nothing visual is reset here, on purpose. Init drives the whole of it from the model it is
        // handed, and clearing it here as well would let a broken Init still look right on whichever
        // instance came after this one.
        public void Release()
        {
            if (_model != null)
            {
                _model.OnStateChanged -= OnStateChanged;
            }

            _model = null;
            _onClickCallback = null;
        }

        // The click listener is per instance, so it is added once in Awake and dropped once here.
        // The subscription is per acquire and goes out through the same path a release takes, so a
        // view destroyed while it was still holding a model lets go of it too - the model belongs to
        // the controller and outlives the view showing it.
        private void OnDestroy()
        {
            Release();

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

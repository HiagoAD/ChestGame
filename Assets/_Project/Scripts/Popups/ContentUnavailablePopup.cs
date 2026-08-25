using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Company.ChestGame.Popups
{
    public class ContentUnavailablePopupData : PopupDataBase
    {
        public string Message { get; }

        public ContentUnavailablePopupData(string message) => Message = message;
    }

    // What the player is shown when something the game had to fetch did not arrive. It carries a
    // message rather than a failure type, so one popup covers a missing key and a broken download
    // alike and nothing about it names Addressables.
    public class ContentUnavailablePopup : PopupBase<ContentUnavailablePopup, ContentUnavailablePopupData>
    {
        [SerializeField] private TextMeshProUGUI _messageText;
        [SerializeField] private Button _closeButton;

        private void Awake() => _closeButton.onClick.AddListener(Close);

        private void OnDestroy() => _closeButton.onClick.RemoveListener(Close);

        protected override void OnInitialize() => _messageText.text = Data.Message;

        private void Close() => Destroy(gameObject);
    }
}

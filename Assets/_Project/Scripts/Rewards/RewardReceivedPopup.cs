using System;
using System.Threading.Tasks;
using Company.ChestGame.Currency;
using Company.ChestGame.Popups;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Company.ChestGame.Rewards
{
    public class RewardReceivedPopupData : PopupDataBase
    {
        public CurrencyType CurrencyType { get; }
        public long Amount { get; }

        public RewardReceivedPopupData(CurrencyType currencyType, long amount)
        {
            CurrencyType = currencyType;
            Amount = amount;
        }
    }

    public class RewardReceivedPopup : PopupBase<RewardReceivedPopup, RewardReceivedPopupData>
    {
        [SerializeField] private Sprite coinsSprite;
        [SerializeField] private Sprite gemsSprite;
        [SerializeField] private Image  iconImage;
        [SerializeField] private TextMeshProUGUI amountText;
        [SerializeField] private Button closeButton;

        private void Awake()
        {
            closeButton.onClick.AddListener(OnCloseButton);
        }

        protected override void OnInitialize()
        {
            iconImage.sprite = Data.CurrencyType switch
            {
                CurrencyType.Coins => coinsSprite,
                CurrencyType.Gems => gemsSprite,
                _ => throw new NotImplementedException()
            };
            amountText.text = $"+{Data.Amount} {Data.CurrencyType}";
        }

        private void OnCloseButton()
        {
            Destroy(gameObject);
        }


    }
}

using UnityEngine;
using Company.ChestGame.Currency;
using VContainer;
using TMPro;

namespace Company.ChestGame.UI
{
    public class CurrencyWatcher : MonoBehaviour
    {
        [SerializeField] private CurrencyType _currency;
        [SerializeField] private TextMeshProUGUI _text;

        private ICurrencyManager _currencyManager;


        [Inject]
        private void Inject(ICurrencyManager currencyManager)
        {
            _currencyManager = currencyManager;
            Init();
        }

        private void OnDestroy()
        {
            if (_currencyManager != null) _currencyManager.OnCurrencyChanged -= OnCurrencyChanged;
        }

        private void Init()
        {
            _currencyManager.OnCurrencyChanged += OnCurrencyChanged;

            OnCurrencyChanged(_currency, _currencyManager.GetCurrencyAmount(_currency), _currencyManager.GetCurrencyAmount(_currency), "");
        }

        private void OnCurrencyChanged(CurrencyType resourceType, long amount, long currentBalance, string source)
        {
            if (resourceType == _currency)
            {
                _text.text = $"{resourceType}:{currentBalance}";
            }
        }
    }
}

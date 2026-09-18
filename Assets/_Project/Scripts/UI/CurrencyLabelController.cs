using System;
using Company.ChestGame.Currency;
using Company.ChestGame.Mvc;

namespace Company.ChestGame.UI
{
    /// <summary>
    /// A currency label's rules: watches one <see cref="CurrencyType"/> on an
    /// <see cref="ICurrencyManager"/> and formats its balance into <see cref="Text"/>.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md.
    /// </remarks>
    public class CurrencyLabelController : IController
    {
        private readonly ICurrencyManager _currencyManager;
        private readonly CurrencyType _currency;

        /// <summary>The formatted text for the watched currency's current balance.</summary>
        public string Text { get; private set; }

        /// <summary>Raised with the new <see cref="Text"/> whenever the watched currency changes.</summary>
        public event Action<string> OnTextChanged;

        /// <param name="currencyManager">The manager to watch <paramref name="currency"/> on.</param>
        /// <param name="currency">The currency this controller renders.</param>
        public CurrencyLabelController(ICurrencyManager currencyManager, CurrencyType currency)
        {
            _currencyManager = currencyManager;
            _currency = currency;

            _currencyManager.OnCurrencyChanged += HandleCurrencyChanged;
            Text = Format(_currencyManager.GetCurrencyAmount(_currency));
        }

        /// <summary>Unsubscribes from the watched manager and clears <see cref="OnTextChanged"/>.</summary>
        public void Dispose()
        {
            _currencyManager.OnCurrencyChanged -= HandleCurrencyChanged;
            OnTextChanged = null;
        }

        private void HandleCurrencyChanged(CurrencyType resourceType, long amount, long currentBalance, string source)
        {
            if (resourceType != _currency) return;

            Text = Format(currentBalance);
            OnTextChanged?.Invoke(Text);
        }

        private string Format(long balance) => $"{_currency}:{balance}";
    }
}

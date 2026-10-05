using Company.ChestGame.Currency;

namespace Company.ChestGame.UI
{
    /// <summary>
    /// Builds a <see cref="CurrencyLabelController"/> for one <see cref="CurrencyType"/>.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md, "Where a view gets its controller, and who disposes it".
    /// </remarks>
    public class CurrencyLabelControllerFactory
    {
        private readonly ICurrencyManager _currencyManager;

        public CurrencyLabelControllerFactory(ICurrencyManager currencyManager)
        {
            _currencyManager = currencyManager;
        }

        /// <summary>
        /// Builds a controller watching <paramref name="currency"/>. The caller owns disposing it.
        /// </summary>
        public CurrencyLabelController Create(CurrencyType currency) =>
            new(_currencyManager, currency);
    }
}

using Company.ChestGame.Currency;

namespace Company.ChestGame.UI
{
    /// <summary>
    /// Builds a <see cref="CurrencyLabelController"/> for one <see cref="CurrencyType"/>.
    /// </summary>
    /// <remarks>
    /// The scene carries more than one <see cref="CurrencyLabelView"/>, each watching a different
    /// currency, so the controller cannot be a single registered instance; this factory is what is
    /// registered instead. See docs/mvc.md.
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

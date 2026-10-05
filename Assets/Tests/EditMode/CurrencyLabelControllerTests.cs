using System.Collections.Generic;
using Company.ChestGame.Currency;
using Company.ChestGame.Tests.Common;
using Company.ChestGame.UI;
using NUnit.Framework;

namespace Company.ChestGame.Tests.EditMode
{
    /// <summary>
    /// Exercises <see cref="CurrencyLabelController"/> against <see cref="FakeCurrencyManager"/> and, where
    /// event ordering matters, the real <see cref="CurrencyManager"/>,
    /// reachable from edit mode because the controller is plain C# with no Unity dependency.
    /// </summary>
    public class CurrencyLabelControllerTests
    {
        private FakeCurrencyManager _currencyManager;

        [SetUp]
        public void SetUp()
        {
            _currencyManager = new FakeCurrencyManager();
        }

        [Test]
        public void Construction_SeedsTextFromTheCurrentBalance()
        {
            _currencyManager.Balances[CurrencyType.Coins] = 42;

            CurrencyLabelController controller = new(_currencyManager, CurrencyType.Coins);

            Assert.AreEqual("Coins:42", controller.Text);
        }

        [Test]
        public void AChangeToTheWatchedCurrency_UpdatesTextAndRaisesOnTextChanged()
        {
            CurrencyLabelController controller = new(_currencyManager, CurrencyType.Coins);
            List<string> observedText = new();
            controller.OnTextChanged += observedText.Add;

            _currencyManager.AddCurrency(CurrencyType.Coins, 10, "test");

            Assert.AreEqual("Coins:10", controller.Text);
            CollectionAssert.AreEqual(new[] { "Coins:10" }, observedText);
        }

        [Test]
        public void AChangeToADifferentCurrency_DoesNotRaiseOrChangeText()
        {
            CurrencyLabelController controller = new(_currencyManager, CurrencyType.Coins);
            string textAfterConstruction = controller.Text;
            List<string> observedText = new();
            controller.OnTextChanged += observedText.Add;

            _currencyManager.AddCurrency(CurrencyType.Gems, 10, "test");

            Assert.AreEqual(textAfterConstruction, controller.Text);
            CollectionAssert.IsEmpty(observedText);
        }

        [Test]
        public void Dispose_UnsubscribesFromTheCurrencyManager()
        {
            CurrencyLabelController controller = new(_currencyManager, CurrencyType.Coins);
            string textBeforeDispose = controller.Text;

            controller.Dispose();
            _currencyManager.AddCurrency(CurrencyType.Coins, 10, "test");

            Assert.AreEqual(textBeforeDispose, controller.Text);
        }

        /// <remarks>
        /// See docs/architecture.md, "Currency and rewards".
        /// </remarks>
        [Test]
        public void AListenerThatStartsAnotherOperation_LeavesTheTextOnTheTrueBalance()
        {
            CurrencyManager realCurrency = new(new InMemoryCurrencySaveHandler());
            bool nested = false;
            realCurrency.OnCurrencyChanged += (c, a, b, s) =>
            {
                if (c != CurrencyType.Coins || nested) return;
                nested = true;
                realCurrency.AddCurrency(CurrencyType.Coins, 5, "nested");
            };
            CurrencyLabelController controller = new(realCurrency, CurrencyType.Coins);

            realCurrency.AddCurrency(CurrencyType.Coins, 10, "outer");

            Assert.AreEqual(15, realCurrency.GetCurrencyAmount(CurrencyType.Coins));
            Assert.AreEqual("Coins:15", controller.Text);
        }
    }
}

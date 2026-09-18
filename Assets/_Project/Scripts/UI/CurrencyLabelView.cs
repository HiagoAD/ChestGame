using Company.ChestGame.Currency;
using Company.ChestGame.Mvc;
using TMPro;
using UnityEngine;
using VContainer;

namespace Company.ChestGame.UI
{
    /// <summary>
    /// A currency label's view: renders <see cref="CurrencyLabelController.Text"/> to a label.
    /// </summary>
    /// <remarks>
    /// This view builds its own controller through <see cref="CurrencyLabelControllerFactory"/> and
    /// so, unlike a view handed an already-built controller, owns disposing it: see
    /// <see cref="OnUnbind"/>. See docs/mvc.md.
    /// </remarks>
    public class CurrencyLabelView : ViewBase<CurrencyLabelController>
    {
        [SerializeField] private CurrencyType _currency;
        [SerializeField] private TextMeshProUGUI _text;

        [Inject]
        private void Inject(CurrencyLabelControllerFactory factory) => Bind(factory.Create(_currency));

        protected override void OnBind()
        {
            Controller.OnTextChanged += Render;
            Render(Controller.Text);
        }

        /// <summary>Unsubscribes from the controller and disposes it, since this view owns it.</summary>
        protected override void OnUnbind()
        {
            Controller.OnTextChanged -= Render;
            Controller.Dispose();
        }

        private void Render(string text) => _text.text = text;
    }
}

using System;
using TMPro;
using UnityEngine;

namespace Company.ChestGame.Core
{
    /// <summary>
    /// Boot scene's view for <see cref="BootStatusModel"/>, backed by a serialized
    /// <see cref="TMP_Text"/> label.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md, "A view that binds to a model".
    /// See docs/architecture.md, "Telling the player what boot is doing".
    /// </remarks>
    public class BootStatusLabel : MonoBehaviour
    {
        [SerializeField] private TMP_Text _label;

        private BootStatusModel _model;

        /// <summary>
        /// Subscribes to <paramref name="model"/> and renders its current
        /// <see cref="BootStatusModel.Message"/>, unless nothing has been reported yet, in which
        /// case the label keeps the text the scene authored.
        /// </summary>
        /// <param name="model">The model to render.</param>
        /// <exception cref="ArgumentNullException"><paramref name="model"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException">This label is already bound.</exception>
        public void Bind(BootStatusModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (_model != null)
            {
                throw new InvalidOperationException(
                    $"{nameof(BootStatusLabel)} is already bound; a view binds once per instance");
            }

            _model = model;
            _model.OnMessageChanged += Render;
            if (_model.Message != null) Render(_model.Message);
        }

        private void OnDestroy()
        {
            if (_model == null) return;

            _model.OnMessageChanged -= Render;
        }

        private void Render(string message)
        {
            if (_label == null) return;

            _label.text = message;
        }
    }
}

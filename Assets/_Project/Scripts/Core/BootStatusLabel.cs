using TMPro;
using UnityEngine;

namespace Company.ChestGame.Core
{
    /// <summary>
    /// Boot scene's <see cref="IBootStatus"/>, backed by a serialized <see cref="TMP_Text"/> label.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Telling the player what boot is doing".
    /// </remarks>
    public class BootStatusLabel : MonoBehaviour, IBootStatus
    {
        [SerializeField] private TMP_Text _label;

        /// <summary>
        /// Writes <paramref name="message"/> to the label. Does nothing if the label was never wired.
        /// </summary>
        /// <param name="message">The status text to display.</param>
        /// <remarks>
        /// See docs/architecture.md, "Telling the player what boot is doing".
        /// </remarks>
        public void Report(string message)
        {
            if (_label == null) return;

            _label.text = message;
        }
    }
}

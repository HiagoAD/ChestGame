using System;

namespace Company.ChestGame.Core
{
    /// <summary>
    /// <see cref="IBootStatus"/> as a model: the last reported message, plus a change event a view
    /// can bind to.
    /// </summary>
    /// <remarks>
    /// See docs/mvc.md. See docs/architecture.md, "Telling the player what boot is doing".
    /// </remarks>
    public class BootStatusModel : IBootStatus
    {
        /// <summary>The last message reported through <see cref="Report"/>, or <c>null</c> before the first call.</summary>
        public string Message { get; private set; }

        /// <summary>Raised with the new <see cref="Message"/> whenever <see cref="Report"/> is called.</summary>
        public event Action<string> OnMessageChanged;

        /// <summary>Stores <paramref name="message"/> and raises <see cref="OnMessageChanged"/> with it.</summary>
        /// <param name="message">The status text to record.</param>
        public void Report(string message)
        {
            Message = message;
            OnMessageChanged?.Invoke(Message);
        }
    }
}

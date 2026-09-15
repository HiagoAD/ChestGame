using System;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Thrown by a protector when a MAC or signature it recomputes does not match what it was
    /// handed. Internal: nothing outside this assembly ever needs to catch it directly.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "Tamper detection is a different failure from a corrupt payload".
    /// </remarks>
    internal sealed class PayloadTamperedException : Exception
    {
        public PayloadTamperedException(string message) : base(message) { }
    }
}

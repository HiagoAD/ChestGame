using System;

namespace Company.ChestGame.Saving
{
    // Thrown by a protector when a MAC or signature it recomputes does not match what it was
    // handed. Internal: nothing outside this assembly ever needs to catch it directly, since a
    // protector has no key of its own to report a failure against.
    internal sealed class PayloadTamperedException : Exception
    {
        public PayloadTamperedException(string message) : base(message) { }
    }
}

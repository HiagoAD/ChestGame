using System;

namespace Company.ChestGame.Saving
{
    // Thrown by a protector when a MAC or signature it recomputes does not match what it was
    // handed. Internal: a protector has no key to report a failure against, so SaveService is the
    // only thing that ever catches this, translating it into SaveException.PayloadTampered before
    // it can reach a caller. Anything a protector or codec throws for any other reason still lands
    // on SaveException.PayloadUnreadable, the same as before phase 3.
    internal sealed class PayloadTamperedException : Exception
    {
        public PayloadTamperedException(string message) : base(message) { }
    }
}

using System.Collections.Generic;

namespace Company.ChestGame.Saving
{
    // Flushes any number of ISaveFlushable from one call, without the caller naming or referencing
    // their types.
    public interface ISaveFlushRegistry
    {
        // Throws SaveException when flushable.CanFlushBlocking is false: registering is a
        // declaration that this wants flushing, and one that cannot be flushed is a wiring mistake.
        // Idempotent per instance.
        void Register(ISaveFlushable flushable);

        // A no-op if flushable was never registered.
        void Unregister(ISaveFlushable flushable);

        // Flushes everything registered. Never throws: one failure is logged and the rest still
        // flush, so this is safe to call from a lifecycle callback that must not propagate.
        void FlushAll();

        // Everything currently registered, in registration order.
        IReadOnlyList<ISaveFlushable> Registered { get; }
    }
}

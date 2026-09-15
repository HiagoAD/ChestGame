using System.Collections.Generic;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Flushes any number of <see cref="ISaveFlushable"/> from one call, without the caller naming
    /// or referencing their types.
    /// </summary>
    public interface ISaveFlushRegistry
    {
        /// <summary>
        /// Idempotent per instance.
        /// </summary>
        /// <exception cref="SaveException">When <c>flushable.CanFlushBlocking</c> is
        /// false.</exception>
        void Register(ISaveFlushable flushable);

        /// <summary>A no-op if flushable was never registered.</summary>
        void Unregister(ISaveFlushable flushable);

        /// <summary>
        /// Flushes everything registered. Never throws: one failure is logged and the rest still
        /// flush.
        /// </summary>
        void FlushAll();

        /// <summary>Everything currently registered, in registration order.</summary>
        IReadOnlyList<ISaveFlushable> Registered { get; }
    }
}

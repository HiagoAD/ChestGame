using System.Collections.Generic;

namespace Company.ChestGame.Saving
{
    // What lets a composition root flush every scheduler it builds from one place at pause/quit,
    // without naming any of their concrete types - including one defined in an assembly the
    // composition root deliberately does not reference. See docs/saving.md, "The pause/quit flush
    // lives on GameLifetimeScope".
    public interface ISaveFlushRegistry
    {
        // Throws SaveException.SchedulerCannotFlushBlocking(flushable.SaveKey) when
        // flushable.CanFlushBlocking is false: registering here is a declaration that this flushable
        // wants to be flushed at pause/quit, and one that cannot guarantee that flush ever succeeds
        // is a wiring mistake. Idempotent - registering the same instance twice registers it once.
        void Register(ISaveFlushable flushable);

        // A no-op if flushable was never registered.
        void Unregister(ISaveFlushable flushable);

        // Fault-isolating: one flushable throwing must never stop the rest from flushing, and this
        // itself never throws - called from OnApplicationPause/OnApplicationQuit, where nothing may
        // propagate.
        void FlushAll();

        // What a composition actually registered, so "did this scheduler get wired to the pause/quit
        // flush at all" is a question a test can ask of the real composition root rather than a
        // convention nobody notices the absence of. A scheduler that is never registered is never
        // flushed, and nothing else about a running game looks any different.
        IReadOnlyList<ISaveFlushable> Registered { get; }
    }
}

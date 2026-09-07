namespace Company.ChestGame.Saving
{
    // The pause/quit flush seam a scheduler-shaped type participates in without a composition root
    // ever naming its concrete type. SaveScheduler<T> already carries both members with these exact
    // signatures, so it needs no new code beyond declaring it implements this. See docs/saving.md,
    // "The pause/quit flush lives on GameLifetimeScope".
    public interface ISaveFlushable
    {
        // See SaveScheduler<T>.CanFlushBlocking - answered ahead of time, at composition time, from
        // whatever this flushable is built over.
        bool CanFlushBlocking { get; }

        // See SaveScheduler<T>.FlushBlocking - genuinely synchronous end to end, never a blocking
        // wait on work that itself needs the calling thread to finish.
        void FlushBlocking();

        // The save key this flushable writes under. On the seam rather than only on SaveScheduler<T>
        // so a registry holding several can name the one that failed - a flush error on a device
        // that cannot say which save it lost is most of the way to no error at all.
        string SaveKey { get; }
    }
}

namespace Company.ChestGame.Saving
{
    // Something holding unwritten state that can be forced to disk synchronously, at a moment when
    // there is no time left to await anything.
    public interface ISaveFlushable
    {
        // Whether FlushBlocking can ever succeed on this instance. Answerable before any state is
        // pending, so a caller can refuse a bad wiring at construction rather than at the one moment
        // durability matters.
        bool CanFlushBlocking { get; }

        // Writes whatever is pending and returns only once it is durable. Synchronous end to end:
        // it must never block on work that itself needs the calling thread to finish. Throws when
        // CanFlushBlocking is false.
        void FlushBlocking();

        // The save key this writes under, so a caller holding several can name this one in a
        // failure.
        string SaveKey { get; }
    }
}

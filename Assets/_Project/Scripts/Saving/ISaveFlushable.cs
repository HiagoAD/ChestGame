namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Something holding unwritten state that can be forced to disk synchronously, at a moment when
    /// there is no time left to await anything.
    /// </summary>
    public interface ISaveFlushable
    {
        /// <summary>
        /// Whether <see cref="FlushBlocking"/> can ever succeed on this instance. Answerable before
        /// any state is pending, so a caller can refuse a bad wiring at construction rather than at
        /// the one moment durability matters.
        /// </summary>
        bool CanFlushBlocking { get; }

        /// <summary>
        /// Writes whatever is pending and returns only once it is durable. Synchronous end to end:
        /// it must never block on work that itself needs the calling thread to finish.
        /// </summary>
        /// <exception cref="SaveException">When <see cref="CanFlushBlocking"/> is
        /// false.</exception>
        void FlushBlocking();

        /// <summary>
        /// The save key this writes under, so a caller holding several can name this one in a
        /// failure.
        /// </summary>
        string SaveKey { get; }
    }
}

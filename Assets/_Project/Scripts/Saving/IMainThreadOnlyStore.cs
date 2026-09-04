namespace Company.ChestGame.Saving
{
    // A store declaring its own thread affinity, rather than ThreadHoppingStore or SaveScheduler
    // guessing at it. Every Unity API - PlayerPrefs included - is main-thread only, so
    // PlayerPrefsStore is the one ISaveStore that implements this. FileStore, AtomicFileStore and
    // InMemoryStore do not: none of them touch a Unity API on the path a key resolves through, so
    // they can run on a worker thread exactly as written. See docs/saving.md, "The thread hop".
    //
    // Deliberately empty - a marker, not a capability. The interface a caller reaches for
    // (ISaveStore) does not change shape depending on where an implementation is allowed to run;
    // only whether something is willing to hop needs answering, and answering it does not require
    // adding a member every store must implement, including the three that would have to return the
    // same constant.
    public interface IMainThreadOnlyStore
    {
    }
}

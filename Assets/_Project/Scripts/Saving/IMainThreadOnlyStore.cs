namespace Company.ChestGame.Saving
{
    // Marker for an ISaveStore that must run on the main thread. Implement it on any store that
    // touches an API with that restriction; leave it off one that is safe to run anywhere.
    // Deliberately empty - it answers a question about a store, it does not add behaviour to one.
    public interface IMainThreadOnlyStore
    {
    }
}

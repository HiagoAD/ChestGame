namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Marker for an <see cref="ISaveStore"/> that must run on the main thread. Implement it on any
    /// store that touches an API with that restriction; leave it off one that is safe to run
    /// anywhere.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// </remarks>
    public interface IMainThreadOnlyStore
    {
    }
}

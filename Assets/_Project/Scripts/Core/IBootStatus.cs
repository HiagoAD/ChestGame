namespace Company.ChestGame.Core
{
    /// <summary>
    /// Where boot reports what it is doing.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Telling the player what boot is doing".
    /// </remarks>
    public interface IBootStatus
    {
        void Report(string message);
    }
}

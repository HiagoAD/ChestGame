namespace Company.ChestGame.Core
{
    /// <summary>
    /// No-op <see cref="IBootStatus"/> registered when there is no boot label.
    /// </summary>
    /// <remarks>
    /// See docs/architecture.md, "Telling the player what boot is doing".
    /// </remarks>
    public class SilentBootStatus : IBootStatus
    {
        public void Report(string message) { }
    }
}

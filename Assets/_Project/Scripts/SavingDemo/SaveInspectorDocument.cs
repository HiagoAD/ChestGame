namespace Company.ChestGame.Saving.Demo
{
    /// <summary>
    /// The save inspector demo's own sample save state. <see cref="Balance"/> is what
    /// <see cref="SaveTamper"/> rewrites.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The tamper button, and why it edits two different ways".
    /// </remarks>
    public class SaveInspectorDocument
    {
        public long Balance { get; set; } = 1_250;
        public string Nickname { get; set; } = "Inspector";
        public int Level { get; set; } = 3;
    }
}

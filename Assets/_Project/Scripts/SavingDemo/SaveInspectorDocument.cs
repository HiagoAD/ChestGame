namespace Company.ChestGame.Saving.Demo
{
    // The demo's own sample state. Balance is what SaveTamper rewrites; Nickname and Level exist so
    // a rendered save has more than one field worth looking at.
    public class SaveInspectorDocument
    {
        public long Balance { get; set; } = 1_250;
        public string Nickname { get; set; } = "Inspector";
        public int Level { get; set; } = 3;
    }
}

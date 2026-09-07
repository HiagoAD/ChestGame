namespace Company.ChestGame.Saving.Demo
{
    // The demo's own sample state - never CurrencySaveDocument or any other game save model, so
    // this assembly has no reason to reference Currency or anything else under the game. Balance is
    // what SaveTamper rewrites; Nickname and Level exist so a rendered save has more than one field
    // worth looking at.
    public class SaveInspectorDocument
    {
        public long Balance { get; set; } = 1_250;
        public string Nickname { get; set; } = "Inspector";
        public int Level { get; set; } = 3;
    }
}

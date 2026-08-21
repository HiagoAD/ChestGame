namespace Company.ChestGame.Core
{
    // Where boot tells the player what it is doing. An interface rather than a label because the
    // bootstrapper is a plain class and reaching a TextMeshPro component from it would put a scene
    // object in the one part of booting that has none.
    public interface IBootStatus
    {
        void Report(string message);
    }
}

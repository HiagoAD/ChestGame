namespace Company.ChestGame.Core
{
    // Where boot tells the player what it is doing. An interface rather than a label, so the
    // bootstrapper stays free of scene objects.
    public interface IBootStatus
    {
        void Report(string message);
    }
}

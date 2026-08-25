namespace Company.ChestGame.Core
{
    // What boot reports through when there is no label: a container built by a test, or an unwired
    // slot. Registered rather than nothing, so the bootstrapper needs no null check per call.
    public class SilentBootStatus : IBootStatus
    {
        public void Report(string message) { }
    }
}

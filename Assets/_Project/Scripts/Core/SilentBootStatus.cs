namespace Company.ChestGame.Core
{
    // What boot reports through when there is no label to report to: a container built by a test,
    // or a boot scene whose label slot was never wired. Registering this rather than nothing is
    // what keeps the bootstrapper free of a null check it would otherwise need at every call.
    public class SilentBootStatus : IBootStatus
    {
        public void Report(string message) { }
    }
}

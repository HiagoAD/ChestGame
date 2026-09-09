namespace Company.ChestGame.Saving
{
    // Wraps a codec's bytes for storage and reverses it - encryption, obfuscation, or nothing.
    public interface IPayloadProtector
    {
        string Id { get; }

        // Answer true only if Protect's output is still valid JSON. Valid UTF-8 is not enough.
        bool IsTextSafe { get; }

        byte[] Protect(byte[] plain);

        // Reverses Protect. An implementation that can detect an edit throws
        // PayloadTamperedException when the bytes no longer match what it produced; one that cannot
        // returns whatever the edited bytes decode to.
        byte[] Unprotect(byte[] stored);
    }
}

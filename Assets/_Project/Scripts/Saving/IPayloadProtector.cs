namespace Company.ChestGame.Saving
{
    // Wraps a codec's bytes for storage and reverses it - encryption, obfuscation, or nothing.
    public interface IPayloadProtector
    {
        string Id { get; }

        // Whether Protect's output is still valid JSON once this has run on top of the codec. Same
        // meaning, and same trap, as ISaveCodec.IsTextSafe.
        bool IsTextSafe { get; }

        byte[] Protect(byte[] plain);

        byte[] Unprotect(byte[] stored);
    }
}

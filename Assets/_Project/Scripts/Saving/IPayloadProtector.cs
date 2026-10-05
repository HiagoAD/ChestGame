namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Wraps a codec's bytes for storage and reverses it: encryption, obfuscation, or nothing.
    /// </summary>
    public interface IPayloadProtector
    {
        string Id { get; }

        /// <summary>
        /// Answer true only if <see cref="Protect"/>'s output is still valid JSON. Valid UTF-8 is
        /// not enough.
        /// </summary>
        bool IsTextSafe { get; }

        byte[] Protect(byte[] plain);

        /// <summary>
        /// Reverses <see cref="Protect"/>. An implementation that can detect an edit throws
        /// <see cref="PayloadTamperedException"/> when the bytes no longer match what it produced;
        /// one that cannot returns whatever the edited bytes decode to.
        /// </summary>
        byte[] Unprotect(byte[] stored);
    }
}

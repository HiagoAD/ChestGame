namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Compares two byte arrays in constant time: every byte is checked and the result is decided
    /// only once the loop is over, so a mismatch cannot be timed to learn where it occurred.
    /// </summary>
    /// <remarks>
    /// Do not shorten the loop with an early return or a library <c>SequenceEqual</c>. Do not
    /// replace it with <c>CryptographicOperations.FixedTimeEquals</c> either.
    /// See docs/saving.md, "The protectors, and what a key shipping inside the binary buys".
    /// </remarks>
    internal static class ConstantTimeCompare
    {
        public static bool AreEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;

            int difference = 0;
            for (int i = 0; i < a.Length; i++)
            {
                difference |= a[i] ^ b[i];
            }

            return difference == 0;
        }
    }
}

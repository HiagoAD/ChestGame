namespace Company.ChestGame.Saving
{
    // Compares two byte arrays in constant time: every byte is checked and the result is decided
    // only once the loop is over, so a mismatch cannot be timed to learn where it occurred. Do not
    // shorten the loop with an early return or a library SequenceEqual. Do not replace it with
    // CryptographicOperations.FixedTimeEquals either: that compiles here, but it belongs to a
    // cryptography surface this project does not rely on at runtime on all its targets.
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

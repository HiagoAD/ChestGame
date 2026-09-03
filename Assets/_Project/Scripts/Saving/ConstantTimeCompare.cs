namespace Company.ChestGame.Saving
{
    // The one comparison HmacSignedProtector and AesProtector both verify a tag against, in one
    // place, for the reason SaveKeyPath's header gives for not mirroring FileStore's key rules by
    // hand: a comment saying two copies agree is not a guarantee that they do, and nothing would
    // fail if a future edit — CryptographicOperations.FixedTimeEquals, a well-meaning
    // SequenceEqual, an early `return false` inside the loop — landed in one copy and not the
    // other, leaving one protector timing-safe and the other not.
    //
    // Written out by hand rather than through CryptographicOperations.FixedTimeEquals: that type
    // compiles against this project's netstandard2.1 API surface, but it belongs to the same .NET
    // Core 3.0-era cryptography work as AesGcm — see AesProtector — and this document already
    // treats that whole surface as not dependable under IL2CPP. Every byte is compared and the
    // accumulator is only inspected once the loop is over, so neither an early return nor a
    // short-circuited loop leaks how far a mismatch got.
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

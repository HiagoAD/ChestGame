using System;
using System.IO;

namespace Company.ChestGame.Saving
{
    // Every filename-store key rule FileStoreTests pins, in one place FileStore and AtomicFileStore
    // both call, so a fifth rule or a reordered check cannot land in one and not the other. See
    // docs/saving.md for why the order of the checks is load-bearing.
    internal static class SaveKeyPath
    {
        private const string Extension = ".sav";

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        public static void EnsurePresent(string key)
        {
            if (string.IsNullOrEmpty(key)) throw SaveException.NoKey();
        }

        // A bad key is rejected, never rewritten: two keys rewritten into one file name would be
        // one save silently overwriting another.
        public static string ResolveFile(string rootDirectory, string key)
        {
            EnsurePresent(key);

            // Before IsPathRooted, which on Mono throws an untyped ArgumentException for a character
            // like NUL instead of answering the question it was asked.
            if (HasInvalidCharacter(key)) throw SaveException.InvalidKey(key);

            if (Path.IsPathRooted(key) || key.Contains("..")) throw SaveException.KeyEscapesRoot(key);
            if (HasSeparator(key)) throw SaveException.InvalidKey(key);

            string candidate = Path.GetFullPath(Path.Combine(rootDirectory, key + Extension));

            string rootWithSeparator = rootDirectory.EndsWith(Path.DirectorySeparatorChar)
                ? rootDirectory
                : rootDirectory + Path.DirectorySeparatorChar;

            // Unreachable given the three rejections above, and kept as the statement of the
            // invariant rather than leaving it inferred from what they happen to catch.
            if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal)) throw SaveException.KeyEscapesRoot(key);

            return candidate;
        }

        // Separators excluded here and checked separately, so a rooted key still reports
        // KeyEscapesRoot rather than being caught by this first.
        private static bool HasInvalidCharacter(string key)
        {
            foreach (char c in key)
            {
                if (IsSeparator(c)) continue;
                if (Array.IndexOf(InvalidFileNameChars, c) >= 0) return true;
            }

            return false;
        }

        private static bool HasSeparator(string key)
        {
            foreach (char c in key)
            {
                if (IsSeparator(c)) return true;
            }

            return false;
        }

        private static bool IsSeparator(char c) =>
            c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar;
    }
}

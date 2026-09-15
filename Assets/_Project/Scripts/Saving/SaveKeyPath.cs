using System;
using System.IO;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Every key rule a filename-based store needs, gathered in one place so a reordered check or a
    /// new rule cannot land in one caller and not another.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "FileStore".
    /// </remarks>
    internal static class SaveKeyPath
    {
        private const string Extension = ".sav";

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        public static void EnsurePresent(string key)
        {
            if (string.IsNullOrEmpty(key)) throw SaveException.NoKey();
        }

        /// <summary>
        /// A bad key is rejected, never rewritten: two keys rewritten into one file name would be
        /// one save silently overwriting another.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "FileStore".
        /// See docs/saving.md, "SaveKeyPath, and why the key logic is shared rather than mirrored".
        /// </remarks>
        public static string ResolveFile(string rootDirectory, string key)
        {
            EnsurePresent(key);

            if (HasInvalidCharacter(key)) throw SaveException.InvalidKey(key);

            if (Path.IsPathRooted(key) || key.Contains("..")) throw SaveException.KeyEscapesRoot(key);
            if (HasSeparator(key)) throw SaveException.InvalidKey(key);

            string candidate = Path.GetFullPath(Path.Combine(rootDirectory, key + Extension));

            string rootWithSeparator = rootDirectory.EndsWith(Path.DirectorySeparatorChar)
                ? rootDirectory
                : rootDirectory + Path.DirectorySeparatorChar;

            if (!candidate.StartsWith(rootWithSeparator, StringComparison.Ordinal)) throw SaveException.KeyEscapesRoot(key);

            return candidate;
        }

        /// <summary>
        /// Separators are excluded here and checked separately by <see cref="HasSeparator"/>.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "FileStore".
        /// </remarks>
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

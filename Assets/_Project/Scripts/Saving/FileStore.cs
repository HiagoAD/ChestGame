using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    // One file per key under a root directory. The root is a constructor argument so a test can
    // point it somewhere disposable. Phase 1 is synchronous inside the UniTask; the thread hop is
    // phase 5's. See docs/saving.md.
    public class FileStore : ISaveStore
    {
        private const string Extension = ".sav";

        private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

        private readonly string _rootDirectory;

        public FileStore(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw SaveException.NoRootDirectory();

            _rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public static string DefaultRootDirectory() => Path.Combine(Application.persistentDataPath, "Saves");

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string path = PathFor(key);

            try
            {
                // Inside the guard: creating a directory fails the same ways writing to one does.
                Directory.CreateDirectory(_rootDirectory);
                File.WriteAllBytes(path, bytes ?? Array.Empty<byte>());
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                throw SaveException.Io(key, exception);
            }

            return UniTask.CompletedTask;
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string path = PathFor(key);
            if (!File.Exists(path)) return UniTask.FromResult<byte[]>(null);

            try
            {
                return UniTask.FromResult(File.ReadAllBytes(path));
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                throw SaveException.Io(key, exception);
            }
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return UniTask.FromResult(File.Exists(PathFor(key)));
        }

        public UniTask DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string path = PathFor(key);

            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                throw SaveException.Io(key, exception);
            }

            return UniTask.CompletedTask;
        }

        // UnauthorizedAccessException derives from SystemException, not IOException, so catching
        // only IOException lets a permissions failure escape untyped.
        private static bool IsStorageFailure(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException;

        // A bad key is rejected, never rewritten: two keys rewritten into one file name would be
        // one save silently overwriting another.
        private string PathFor(string key)
        {
            if (string.IsNullOrEmpty(key)) throw SaveException.NoKey();

            // Before IsPathRooted, which on Mono throws an untyped ArgumentException for a character
            // like NUL instead of answering the question it was asked.
            if (HasInvalidCharacter(key)) throw SaveException.InvalidKey(key);

            if (Path.IsPathRooted(key) || key.Contains("..")) throw SaveException.KeyEscapesRoot(key);
            if (HasSeparator(key)) throw SaveException.InvalidKey(key);

            string candidate = Path.GetFullPath(Path.Combine(_rootDirectory, key + Extension));

            string rootWithSeparator = _rootDirectory.EndsWith(Path.DirectorySeparatorChar)
                ? _rootDirectory
                : _rootDirectory + Path.DirectorySeparatorChar;

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

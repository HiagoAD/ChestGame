using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    // One file per key under a root directory. The root is a constructor argument so a test can
    // point it somewhere disposable. Every member here stays synchronous inside the UniTask it
    // returns - do not add a real suspension point inside this class. See docs/saving.md for why.
    public class FileStore : ISaveStore
    {
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

        // Every member above is plain, synchronous File/Directory IO wrapped in an already-completed
        // UniTask - nothing here ever suspends, so this is always true.
        public bool CompletesOnCallingThread => true;

        // UnauthorizedAccessException derives from SystemException, not IOException, so catching
        // only IOException lets a permissions failure escape untyped.
        private static bool IsStorageFailure(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException;

        private string PathFor(string key) => SaveKeyPath.ResolveFile(_rootDirectory, key);
    }
}

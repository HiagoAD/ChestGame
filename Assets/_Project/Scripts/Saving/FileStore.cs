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

        // Shared with AtomicFileStore via SaveKeyPath, so the rules FileStoreTests pins cannot drift
        // between the two.
        private string PathFor(string key) => SaveKeyPath.ResolveFile(_rootDirectory, key);
    }
}

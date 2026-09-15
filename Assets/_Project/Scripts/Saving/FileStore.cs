using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// One file per key under a root directory. The root is a constructor argument so a test can
    /// point it somewhere disposable.
    /// </summary>
    /// <remarks>
    /// Every member here stays synchronous inside the UniTask it returns: do not add a real
    /// suspension point inside this class.
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// </remarks>
    public class FileStore : ISaveStore
    {
        private readonly string _rootDirectory;

        public FileStore(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw SaveException.NoRootDirectory();

            _rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public static string DefaultRootDirectory() => Path.Combine(Application.persistentDataPath, "Saves");

        /// <remarks>
        /// See docs/saving.md, "FileStore".
        /// </remarks>
        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string path = PathFor(key);

            try
            {
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

        public bool CompletesOnCallingThread => true;

        /// <summary>
        /// <see cref="UnauthorizedAccessException"/> derives from <see cref="SystemException"/>, not
        /// <see cref="IOException"/>, so both are checked explicitly.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "FileStore".
        /// </remarks>
        private static bool IsStorageFailure(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException;

        private string PathFor(string key) => SaveKeyPath.ResolveFile(_rootDirectory, key);
    }
}

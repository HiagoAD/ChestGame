using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// One file per key. Survives a kill at any point during a write: the new bytes land in a temp
    /// file first, and only then get swapped into place, with the file they replace kept as a
    /// <c>.bak</c> rather than deleted. Reading prefers the live file and falls back to the
    /// <c>.bak</c> when the live one is absent or unreadable.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "AtomicFileStore".
    /// </remarks>
    public class AtomicFileStore : ISaveStore
    {
        private const string TempExtension = ".tmp";
        private const string BackupExtension = ".bak";

        private readonly string _rootDirectory;

        public AtomicFileStore(string rootDirectory)
        {
            if (string.IsNullOrEmpty(rootDirectory)) throw SaveException.NoRootDirectory();

            _rootDirectory = Path.GetFullPath(rootDirectory);
        }

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string livePath = SaveKeyPath.ResolveFile(_rootDirectory, key);
            string tempPath = livePath + TempExtension;
            string backupPath = livePath + BackupExtension;

            try
            {
                Directory.CreateDirectory(_rootDirectory);
                WriteAndFlush(tempPath, bytes ?? Array.Empty<byte>());
                Swap(tempPath, livePath, backupPath);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                TryDeleteTempFile(tempPath);
                throw SaveException.Io(key, exception);
            }

            return UniTask.CompletedTask;
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string livePath = SaveKeyPath.ResolveFile(_rootDirectory, key);
            string backupPath = livePath + BackupExtension;

            bool liveExists = File.Exists(livePath);
            bool backupExists = File.Exists(backupPath);
            if (!liveExists && !backupExists) return UniTask.FromResult<byte[]>(null);

            Exception liveFailure = null;
            if (liveExists)
            {
                try
                {
                    return UniTask.FromResult(File.ReadAllBytes(livePath));
                }
                catch (Exception exception) when (IsStorageFailure(exception))
                {
                    liveFailure = exception;
                }
            }

            if (backupExists)
            {
                try
                {
                    return UniTask.FromResult(File.ReadAllBytes(backupPath));
                }
                catch (Exception exception) when (IsStorageFailure(exception))
                {
                    throw SaveException.Io(key, exception);
                }
            }

            throw SaveException.Io(key, liveFailure);
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string livePath = SaveKeyPath.ResolveFile(_rootDirectory, key);
            string backupPath = livePath + BackupExtension;

            return UniTask.FromResult(File.Exists(livePath) || File.Exists(backupPath));
        }

        public UniTask DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string livePath = SaveKeyPath.ResolveFile(_rootDirectory, key);
            string backupPath = livePath + BackupExtension;

            try
            {
                if (File.Exists(livePath)) File.Delete(livePath);
                if (File.Exists(backupPath)) File.Delete(backupPath);
            }
            catch (Exception exception) when (IsStorageFailure(exception))
            {
                throw SaveException.Io(key, exception);
            }

            return UniTask.CompletedTask;
        }

        /// <summary>
        /// Flushes past the OS's own buffers before returning, not merely through <c>Dispose</c>.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "AtomicFileStore".
        /// </remarks>
        private static void WriteAndFlush(string path, byte[] bytes)
        {
            using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        /// <summary>
        /// Prefers <see cref="File.Replace(string, string, string)"/>, falling back to a manual
        /// copy-then-delete-then-move sequence that still leaves the previous file as <c>.bak</c>.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "AtomicFileStore".
        /// </remarks>
        private static void Swap(string tempPath, string livePath, string backupPath)
        {
            if (!File.Exists(livePath))
            {
                File.Move(tempPath, livePath);
                return;
            }

            try
            {
                File.Replace(tempPath, livePath, backupPath, ignoreMetadataErrors: true);
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException || exception is IOException)
            {
                File.Copy(livePath, backupPath, overwrite: true);
                File.Delete(livePath);
                File.Move(tempPath, livePath);
            }
        }

        private static void TryDeleteTempFile(string tempPath)
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch
            {
            }
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
    }
}

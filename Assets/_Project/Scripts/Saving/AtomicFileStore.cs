using System;
using System.IO;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Company.ChestGame.Saving
{
    // One file per key, like FileStore, but survives a kill at any point during a write: the new
    // bytes land in a temp file first, and only then get swapped into place, with the file they
    // replace kept as a .bak rather than deleted. Read prefers the live file and falls back to the
    // .bak when it is absent or unreadable - that fallback is the reason this class exists. See
    // docs/saving.md for what it does and does not protect against.
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
                // A stale temp file next to a live save reads as a second, half-recoverable copy
                // to anyone looking at the directory. Cleanup failure must not replace or mask the
                // failure that put us here.
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

        // Flush(true), not just Dispose: Dispose only empties the stream's own buffer into the OS,
        // and a kill right after that can still lose the write at the OS level.
        private static void WriteAndFlush(string path, byte[] bytes)
        {
            using FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.None);
            stream.Write(bytes, 0, bytes.Length);
            stream.Flush(true);
        }

        // File.Replace is preferred because it is the platform's own swap-and-keep-a-backup
        // primitive, but it is not guaranteed available - see docs/saving.md - so a failure falls
        // back to a manual sequence that still leaves the previous file as .bak.
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
                // Best-effort only: the write already failed for its own reason above.
            }
        }

        // UnauthorizedAccessException derives from SystemException, not IOException, so catching
        // only IOException lets a permissions failure escape untyped.
        private static bool IsStorageFailure(Exception exception) =>
            exception is IOException || exception is UnauthorizedAccessException;
    }
}

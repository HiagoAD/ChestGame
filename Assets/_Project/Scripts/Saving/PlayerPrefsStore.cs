using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    // Bytes as a base64 PlayerPrefs string - PlayerPrefs only holds strings, so the store resolves
    // that mismatch rather than every caller knowing about it. The prefix is a constructor argument
    // for the same reason FileStore's root is: a test can namespace itself away from the real editor
    // prefs. See docs/saving.md for which of FileStore's key rules carry over here and which do not.
    public class PlayerPrefsStore : ISaveStore
    {
        private readonly string _keyPrefix;

        public PlayerPrefsStore(string keyPrefix)
        {
            if (string.IsNullOrEmpty(keyPrefix)) throw SaveException.NoKeyPrefix();

            _keyPrefix = keyPrefix;
        }

        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            PlayerPrefs.SetString(PrefsKeyFor(key), Convert.ToBase64String(bytes ?? Array.Empty<byte>()));

            // Buffered until quit otherwise, which is not persistence.
            PlayerPrefs.Save();

            return UniTask.CompletedTask;
        }

        public UniTask<byte[]> ReadAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string prefsKey = PrefsKeyFor(key);
            if (!PlayerPrefs.HasKey(prefsKey)) return UniTask.FromResult<byte[]>(null);

            try
            {
                return UniTask.FromResult(Convert.FromBase64String(PlayerPrefs.GetString(prefsKey)));
            }
            catch (FormatException exception)
            {
                throw SaveException.Io(key, exception);
            }
        }

        public UniTask<bool> ExistsAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            return UniTask.FromResult(PlayerPrefs.HasKey(PrefsKeyFor(key)));
        }

        public UniTask DeleteAsync(string key, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            PlayerPrefs.DeleteKey(PrefsKeyFor(key));
            PlayerPrefs.Save();

            return UniTask.CompletedTask;
        }

        // Only NoKey carries over from SaveKeyPath: a PlayerPrefs key has no file system to escape
        // and no separator that means anything to it, so the rest of FileStore's rules do not apply.
        private string PrefsKeyFor(string key)
        {
            SaveKeyPath.EnsurePresent(key);

            return _keyPrefix + key;
        }
    }
}

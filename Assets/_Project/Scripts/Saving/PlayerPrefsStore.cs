using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Company.ChestGame.Saving
{
    /// <summary>
    /// Bytes as a base64 PlayerPrefs string. PlayerPrefs only holds strings, so the store resolves
    /// that mismatch rather than every caller knowing about it. The prefix is a constructor argument
    /// so a test can namespace itself away from the real editor prefs.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The thread hop, and why it is not inside SaveService".
    /// </remarks>
    public class PlayerPrefsStore : ISaveStore, IMainThreadOnlyStore
    {
        private readonly string _keyPrefix;

        public PlayerPrefsStore(string keyPrefix)
        {
            if (string.IsNullOrEmpty(keyPrefix)) throw SaveException.NoKeyPrefix();

            _keyPrefix = keyPrefix;
        }

        /// <remarks>
        /// See docs/saving.md, "PlayerPrefsStore, and why it base64s".
        /// </remarks>
        public UniTask WriteAsync(string key, byte[] bytes, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string prefsKey = PrefsKeyFor(key);

            try
            {
                PlayerPrefs.SetString(prefsKey, Convert.ToBase64String(bytes ?? Array.Empty<byte>()));

                PlayerPrefs.Save();
            }
            catch (PlayerPrefsException exception)
            {
                throw SaveException.Io(key, exception);
            }

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

            string prefsKey = PrefsKeyFor(key);

            try
            {
                PlayerPrefs.DeleteKey(prefsKey);
                PlayerPrefs.Save();
            }
            catch (PlayerPrefsException exception)
            {
                throw SaveException.Io(key, exception);
            }

            return UniTask.CompletedTask;
        }

        public bool CompletesOnCallingThread => true;

        /// <summary>
        /// Only presence is checked: a PlayerPrefs key has no file system to escape and no
        /// separator that means anything to it.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "PlayerPrefsStore, and why it base64s".
        /// </remarks>
        private string PrefsKeyFor(string key)
        {
            SaveKeyPath.EnsurePresent(key);

            return _keyPrefix + key;
        }
    }
}

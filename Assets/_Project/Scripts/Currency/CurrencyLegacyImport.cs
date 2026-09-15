using Company.ChestGame.Saving;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Company.ChestGame.Currency
{
    /// <summary>
    /// Reads exactly what <c>DefaultResourceBankSaveHandle&lt;CurrencyType&gt;</c> has always
    /// written: a bare <c>{"ResourceAmount":{...}}</c> under "ResourceBankSaveData_CurrencyType" in
    /// PlayerPrefs, with no envelope and no version field at all.
    /// </summary>
    /// <remarks>
    /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
    /// </remarks>
    public class CurrencyLegacyImport : ILegacyImport
    {
        /// <summary>
        /// Exactly what <c>DefaultResourceBankSaveHandle&lt;T&gt;.SAVE_KEY</c> evaluates to for
        /// <see cref="CurrencyType"/>.
        /// </summary>
        /// <remarks>
        /// Never change it: it is the only bridge back to an already-installed player's existing
        /// save.
        /// </remarks>
        public const string DefaultLegacyKey = "ResourceBankSaveData_CurrencyType";

        /// <summary>
        /// Where the bytes land instead of being deleted - see <see cref="Clear"/>. Suffixed onto
        /// whichever key this instance was built with, so a redirected test key and its marker stay
        /// paired.
        /// </summary>
        private const string MigratedSuffix = ".migrated";

        private readonly string _legacyKey;

        /// <param name="legacyKey">Redirects away from the real PlayerPrefs key, for a test.</param>
        public CurrencyLegacyImport(string legacyKey = null)
        {
            _legacyKey = string.IsNullOrEmpty(legacyKey) ? DefaultLegacyKey : legacyKey;
        }

        /// <summary>
        /// Where the data belongs once imported, as opposed to the legacy key this instance reads
        /// from. Not the literal, so the two can never drift.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "TargetKey, and the defect a second save key exposed".
        /// </remarks>
        public string TargetKey => CurrencyResourceBankSaveHandle.SaveKey;

        /// <summary>
        /// Whether there is a value here that behaves like data, not merely whether the key exists.
        /// </summary>
        /// <returns>
        /// False for a missing key, an empty or whitespace-only string, or the literal "null" - all
        /// of which send <see cref="Import"/> down the same first-run path as a missing key. True
        /// for everything else, including a malformed value, which still reaches
        /// <see cref="Import"/> and still throws.
        /// </returns>
        /// <remarks>
        /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
        /// </remarks>
        public bool IsPresent()
        {
            if (!PlayerPrefs.HasKey(_legacyKey)) return false;

            string raw = PlayerPrefs.GetString(_legacyKey);
            return !string.IsNullOrWhiteSpace(raw) && raw.Trim() != "null";
        }

        /// <remarks>
        /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
        /// </remarks>
        public JObject Import()
        {
            return JObject.Parse(PlayerPrefs.GetString(_legacyKey));
        }

        /// <summary>
        /// Moves the legacy data to <c>_legacyKey + MigratedSuffix</c> instead of deleting it.
        /// </summary>
        /// <remarks>
        /// See docs/saving.md, "The legacy import: CurrencyLegacyImport".
        /// </remarks>
        public void Clear()
        {
            string raw = PlayerPrefs.GetString(_legacyKey);

            PlayerPrefs.SetString(_legacyKey + MigratedSuffix, raw);
            PlayerPrefs.DeleteKey(_legacyKey);

            PlayerPrefs.Save();
        }
    }
}
